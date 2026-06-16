using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices;
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
    /// </summary>
    internal sealed class SerializedFieldRenameWatcher : IDisposable
    {
        private readonly VisualStudioWorkspace workspace;
        private readonly JoinableTaskFactory joinableTaskFactory;

        // Guard against reacting to our own edit echo. The detector is already
        // idempotent (it returns no rename once the attribute exists), so this is
        // an optimization, not the sole safety net.
        private readonly ConcurrentDictionary<DocumentId, byte> documentsBeingEdited = new();

        public SerializedFieldRenameWatcher(VisualStudioWorkspace workspace, JoinableTaskFactory joinableTaskFactory)
        {
            this.workspace = workspace;
            this.joinableTaskFactory = joinableTaskFactory;
        }

        public void Start() => workspace.WorkspaceChanged += OnWorkspaceChanged;

        public void Dispose() => workspace.WorkspaceChanged -= OnWorkspaceChanged;

        // Inline rename can take time to commit after F2 is pressed, so allow a
        // generous window between the rename command and the resulting edit.
        private static readonly TimeSpan RenameSignalWindow = TimeSpan.FromSeconds(60);

        private void OnWorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            if (!IsDocumentEditKind(e.Kind))
            {
                return;
            }

            var changedDocumentIds = new List<DocumentId>(GetChangedDocumentIds(e));

            // A multi-document change is unambiguously a solution-wide rename. A
            // single-document change is only treated as a rename when the Rename
            // command was invoked recently — never plain typing. This is the
            // high-frequency path (every keystroke), so it is intentionally silent.
            var isMultiDocumentChange = changedDocumentIds.Count > 1;

            if (!isMultiDocumentChange && !RenameSignal.WasInvokedWithin(RenameSignalWindow))
            {
                return;
            }

            foreach (var documentId in changedDocumentIds)
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

                // Fire and forget; failures must never disrupt the editor.
                _ = joinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ProcessDocumentAsync(documentId, previousDocument);
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
            var previousRoot = await previousDocument.GetSyntaxRootAsync().ConfigureAwait(false);
            var previousModel = await previousDocument.GetSemanticModelAsync().ConfigureAwait(false);

            if (previousRoot is null)
            {
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

            var latestDocument = workspace.CurrentSolution.GetDocument(documentId);

            if (latestDocument is null)
            {
                return;
            }

            var latestRoot = await latestDocument.GetSyntaxRootAsync();
            var latestModel = await latestDocument.GetSemanticModelAsync();

            if (latestRoot is null)
            {
                return;
            }

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

            // Only act on a genuine whole-identifier rename (Rename Symbol / Ctrl+R),
            // never while a field name is being typed character by character.
            if (!IdentifierRenameRecognizer.IsRenameShaped(
                    previousRoot.ToFullString(),
                    latestRoot.ToFullString(),
                    renames))
            {
                return;
            }

            var updatedSolution = workspace.CurrentSolution.WithDocumentSyntaxRoot(documentId, migratedRoot);

            documentsBeingEdited.TryAdd(documentId, 0);

            try
            {
                var applied = workspace.TryApplyChanges(updatedSolution);
                DiagnosticLog.Write(
                    $"Applied [{string.Join(", ", System.Linq.Enumerable.Select(renames, r => $"{r.PreviousName}->{r.CurrentName}"))}] "
                    + $"to {latestDocument.FilePath} (TryApplyChanges={applied}).");
            }
            finally
            {
                documentsBeingEdited.TryRemove(documentId, out _);
            }
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
