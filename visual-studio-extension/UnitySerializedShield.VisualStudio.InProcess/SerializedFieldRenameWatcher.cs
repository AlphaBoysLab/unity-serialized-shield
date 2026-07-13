using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using UnitySerializedShield.Roslyn;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Watches the Roslyn workspace for edits and, when a Unity-serialized field
    /// is renamed, writes <c>[FormerlySerializedAs("old")]</c> onto the field's
    /// DECLARATION document.
    ///
    /// Because <see cref="VisualStudioWorkspace.WorkspaceChanged"/> reports every
    /// document a change touched, a solution-wide Rename Symbol triggered from a
    /// reference in one file still surfaces the declaration document's edit here —
    /// so the migration attribute lands on the declaration regardless of where the
    /// rename started. That is the cross-file behavior the text-diff model lacked.
    ///
    /// Every edit must pass the one-shot <see cref="RenameSignal"/> gate — armed
    /// only by the actual Rename Symbol command — plus the Roslyn-side rename-shape
    /// gate inside <see cref="SerializedFieldMigrator"/>. Multi-document changes
    /// are NOT assumed to be renames (Fix-All / Code Cleanup / other extensions
    /// also produce them).
    /// </summary>
    internal sealed class SerializedFieldRenameWatcher : IDisposable
    {
        private readonly VisualStudioWorkspace workspace;
        private readonly JoinableTaskFactory joinableTaskFactory;

        // Guard against reacting to our own edit echo. The detector is already
        // idempotent (it returns no rename once the attribute exists), so this is
        // an optimization, not the sole safety net.
        private readonly ConcurrentDictionary<DocumentId, byte> documentsBeingEdited = new();

        // Recently applied migrations, so undoing a rename (new -> old, i.e. the
        // exact inverse) is never re-detected as a fresh rename. Monotonic clock.
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly TimeSpan InverseSuppressionWindow = TimeSpan.FromMinutes(2);
        private readonly object recentMigrationsGate = new object();
        private readonly List<(string PreviousName, string CurrentName, TimeSpan At)> recentMigrations = new();

        public SerializedFieldRenameWatcher(VisualStudioWorkspace workspace, JoinableTaskFactory joinableTaskFactory)
        {
            this.workspace = workspace;
            this.joinableTaskFactory = joinableTaskFactory;
        }

        public void Start() => workspace.WorkspaceChanged += OnWorkspaceChanged;

        public void Dispose() => workspace.WorkspaceChanged -= OnWorkspaceChanged;

        // Inline rename can take time to commit after F2 is pressed, so allow a
        // generous window between the rename command and the resulting edit. The
        // signal is disarmed on the first applied migration (one-shot) and on
        // Escape/Undo/Redo, so the window length no longer enables re-triggering.
        private static readonly TimeSpan RenameSignalWindow = TimeSpan.FromSeconds(60);

        private void OnWorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            // This runs on every workspace tick; it must never throw into VS.
            try
            {
                HandleWorkspaceChanged(e);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"OnWorkspaceChanged threw: {exception}");
            }
        }

        private void HandleWorkspaceChanged(WorkspaceChangeEventArgs e)
        {
            if (!IsDocumentEditKind(e.Kind))
            {
                return;
            }

            // Cheap gates FIRST — this is the high-frequency path (every
            // keystroke), so nothing may be materialized before they pass.
            if (!RenameSignal.IsArmedWithin(RenameSignalWindow))
            {
                return;
            }

            if (!ExtensionOptions.IsEnabled)
            {
                return;
            }

            foreach (var documentId in GetChangedDocumentIds(e))
            {
                if (documentsBeingEdited.ContainsKey(documentId))
                {
                    continue;
                }

                var previousDocument = e.OldSolution.GetDocument(documentId);
                var currentDocument = e.NewSolution.GetDocument(documentId);

                if (previousDocument is null || currentDocument is null || !IsCSharp(currentDocument))
                {
                    continue;
                }

                // Only act inside Unity projects (a UnityEngine reference in the
                // compilation); other C# solutions must never be touched.
                if (!IsUnityProject(currentDocument.Project))
                {
                    continue;
                }

                var capturedId = documentId;

                // Fire and forget; failures must never disrupt the editor.
                _ = joinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ProcessDocumentAsync(capturedId, previousDocument);
                    }
                    catch (Exception exception)
                    {
                        DiagnosticLog.Write($"ProcessDocument threw: {exception}");
                    }
                });
            }
        }

        private async Task ProcessDocumentAsync(DocumentId documentId, Document previousDocument)
        {
            // All parsing and detection happens OFF the UI thread; only the final
            // verify-and-apply hop runs on it.
            var previousRoot = await previousDocument.GetSyntaxRootAsync().ConfigureAwait(false);
            var previousModel = await previousDocument.GetSemanticModelAsync().ConfigureAwait(false);

            if (previousRoot is null)
            {
                return;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var latestDocument = workspace.CurrentSolution.GetDocument(documentId);

                if (latestDocument is null)
                {
                    return;
                }

                var latestVersion = await latestDocument.GetTextVersionAsync().ConfigureAwait(false);
                var latestRoot = await latestDocument.GetSyntaxRootAsync().ConfigureAwait(false);
                var latestModel = await latestDocument.GetSemanticModelAsync().ConfigureAwait(false);

                if (latestRoot is null)
                {
                    return;
                }

                // The migrator applies the full pure gate: serialized-field rename
                // detected AND the edit is a clean whole-identifier substitution.
                var migratedRoot = SerializedFieldMigrator.Migrate(
                    previousRoot,
                    latestRoot,
                    previousModel,
                    latestModel,
                    out var renames);

                if (migratedRoot is null || renames.Count == 0)
                {
                    return;
                }

                // Scope the one-shot signal to the symbol the rename started on,
                // when the command observers could capture it.
                var armedIdentifier = RenameSignal.ArmedIdentifier;

                if (armedIdentifier is not null
                    && !renames.Any(r => r.PreviousName == armedIdentifier || r.CurrentName == armedIdentifier))
                {
                    DiagnosticLog.Write(
                        $"Skipped: detected rename does not involve the armed identifier '{armedIdentifier}'.");
                    return;
                }

                // Undo protection: the exact inverse of a migration we just applied
                // is the user pressing Ctrl+Z, not a new rename.
                if (IsInverseOfRecentMigration(renames))
                {
                    DiagnosticLog.Write("Skipped: change is the inverse of a just-applied migration (undo).");
                    return;
                }

                // Detect AND apply on the UI thread. Because the UI thread is single
                // threaded, concurrent change events for the same rename are serialized:
                // the first event applies the attribute, and every later event then
                // recomputes against a document that already carries it, so the
                // (idempotent) detector returns nothing and no duplicate edit is applied.
                await joinableTaskFactory.SwitchToMainThreadAsync();

                if (documentsBeingEdited.ContainsKey(documentId))
                {
                    return;
                }

                var freshDocument = workspace.CurrentSolution.GetDocument(documentId);

                if (freshDocument is null)
                {
                    return;
                }

                var freshVersion = await freshDocument.GetTextVersionAsync();

                if (freshVersion != latestVersion)
                {
                    // The user typed while we were analyzing. NEVER overwrite those
                    // keystrokes with a stale tree — recompute from the fresh text.
                    DiagnosticLog.Write($"Document changed during analysis (attempt {attempt + 1}); recomputing.");
                    await TaskScheduler.Default;
                    continue;
                }

                ApplyMigration(documentId, freshDocument, migratedRoot, renames);
                return;
            }

            DiagnosticLog.Write("Gave up applying migration: the document kept changing during analysis.");
        }

        // Runs on the UI thread with no awaits between the freshness check and
        // TryApplyChanges, so the verified snapshot cannot go stale in between.
        private void ApplyMigration(
            DocumentId documentId,
            Document freshDocument,
            SyntaxNode migratedRoot,
            IReadOnlyList<RenamedSerializedField> renames)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var updatedSolution = freshDocument.Project.Solution.WithDocumentSyntaxRoot(documentId, migratedRoot);

            var openDocument = FindOpenDocument(freshDocument.FilePath);

            documentsBeingEdited.TryAdd(documentId, 0);

            var renameSummary = string.Join(", ", renames.Select(r => $"{r.PreviousName} -> {r.CurrentName}"));

            try
            {
                var applied = workspace.TryApplyChanges(updatedSolution);

                if (applied)
                {
                    // One-shot: the rename that armed the signal has now been
                    // handled; later edits must re-arm via the Rename command.
                    RenameSignal.Disarm();
                    RecordMigrations(renames);

                    // Persist the declaration file so Unity recompiles with the
                    // attribute present. Rename Symbol already dirtied this
                    // document, so saving writes the rename AND our attribute
                    // together as one consistent file. This must NOT be gated on
                    // the document being clean beforehand: an inline rename always
                    // leaves it dirty, and an open buffer's edits never reach disk
                    // without a save — so Unity would otherwise never see the
                    // attribute (the whole point of the tool). Closed files are
                    // written to disk by TryApplyChanges itself. The signal is
                    // already disarmed above, so the save's own change event is
                    // ignored and cannot re-trigger a migration.
                    if (openDocument is not null)
                    {
                        SaveDocument(openDocument);
                    }

                    NotifyUser($"UnitySerializedShield added [FormerlySerializedAs] for {renameSummary}.");
                }
                else
                {
                    // Fail LOUD: silently losing the migration attribute is the
                    // exact data-loss scenario this extension exists to prevent.
                    NotifyUser(
                        $"UnitySerializedShield could not add [FormerlySerializedAs] for {renameSummary} in "
                        + $"{System.IO.Path.GetFileName(freshDocument.FilePath)} — add it manually before opening Unity.");
                }

                DiagnosticLog.Write(
                    $"Applied [{renameSummary}] to {freshDocument.FilePath} (TryApplyChanges={applied}).");
            }
            finally
            {
                documentsBeingEdited.TryRemove(documentId, out _);
            }
        }

        private bool IsInverseOfRecentMigration(IReadOnlyList<RenamedSerializedField> renames)
        {
            lock (recentMigrationsGate)
            {
                var now = Clock.Elapsed;
                recentMigrations.RemoveAll(entry => now - entry.At > InverseSuppressionWindow);

                return renames.Any(rename => recentMigrations.Any(entry =>
                    entry.PreviousName == rename.CurrentName && entry.CurrentName == rename.PreviousName));
            }
        }

        private void RecordMigrations(IReadOnlyList<RenamedSerializedField> renames)
        {
            lock (recentMigrationsGate)
            {
                var now = Clock.Elapsed;

                foreach (var rename in renames)
                {
                    recentMigrations.Add((rename.PreviousName, rename.CurrentName, now));
                }
            }
        }

        // Finds the open editor document for a file path, or null when the file is
        // not open (closed documents are persisted by the workspace apply itself).
        private static EnvDTE.Document? FindOpenDocument(string? filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            if (!(Package.GetGlobalService(typeof(SDTE)) is EnvDTE.DTE dte))
            {
                return null;
            }

            foreach (EnvDTE.Document document in dte.Documents)
            {
                if (string.Equals(document.FullName, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return document;
                }
            }

            return null;
        }

        private static void SaveDocument(EnvDTE.Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (!document.Saved)
                {
                    document.Save();
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Saving after migration failed: {exception.Message}");
                NotifyUser(
                    "UnitySerializedShield added [FormerlySerializedAs] but could not save the file — "
                    + "save it manually before opening Unity.");
            }
        }

        // Surfaces a failure where the user can see it (status bar) in addition to
        // the diagnostic log.
        private static void NotifyUser(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DiagnosticLog.Write(message);

            try
            {
                if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusbar)
                {
                    statusbar.SetText(message);
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Status bar notification failed: {exception.Message}");
            }
        }

        private static bool IsUnityProject(Project project)
        {
            foreach (var reference in project.MetadataReferences)
            {
                var display = (reference as PortableExecutableReference)?.FilePath ?? reference.Display;

                if (display is not null
                    && display.IndexOf("UnityEngine", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<DocumentId> GetChangedDocumentIds(WorkspaceChangeEventArgs e)
        {
            if (e.DocumentId is not null)
            {
                yield return e.DocumentId;
                yield break;
            }

            // Multi-document change (e.g. a solution-wide rename can surface as a
            // single ProjectChanged/SolutionChanged event with no DocumentId).
            var solutionChanges = e.NewSolution.GetChanges(e.OldSolution);

            foreach (var projectChange in solutionChanges.GetProjectChanges())
            {
                foreach (var changedDocumentId in projectChange.GetChangedDocuments())
                {
                    yield return changedDocumentId;
                }
            }
        }

        private static bool IsDocumentEditKind(WorkspaceChangeKind kind)
        {
            return kind == WorkspaceChangeKind.DocumentChanged
                || kind == WorkspaceChangeKind.ProjectChanged
                || kind == WorkspaceChangeKind.SolutionChanged;
        }

        private static bool IsCSharp(Document document)
        {
            return document.Project.Language == LanguageNames.CSharp
                && document.SupportsSyntaxTree
                && (document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }
}
