using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Auto-loaded package that subscribes a <see cref="SerializedFieldRenameWatcher"/>
    /// to the live Roslyn <see cref="VisualStudioWorkspace"/> as soon as a solution
    /// is open. This is what enables solution-wide, declaration-anchored
    /// [FormerlySerializedAs] insertion on serialized-field renames.
    ///
    /// It also arms <see cref="RenameSignal"/> from DTE command events so a rename is
    /// distinguished from typing WITHOUT depending on the MEF command handler — the
    /// package always loads (via its pkgdef), whereas MEF composition can be skipped
    /// if the extension cache is stale. Only the actual SYMBOL rename command arms
    /// the signal (never File.Rename or other commands that merely contain
    /// "Rename"), and Escape/Undo/Redo disarm it so a cancelled or reverted rename
    /// can never leave a live trigger window behind.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class UnitySerializedShieldPackage : AsyncPackage
    {
        public const string PackageGuidString = "8f1d3c54-7a21-4b8e-9c33-1f5a2d7e9b40";

        private enum CommandClass
        {
            None,
            Rename,
            Disarm,
        }

        // Exact command names. "Refactor.Rename" is the symbol rename (Ctrl+R,R /
        // F2 / context menu); File.Rename and friends must NOT arm the signal.
        private static readonly HashSet<string> RenameCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Refactor.Rename",
            "EditorContextMenus.CodeWindow.Rename",
        };

        // Commands that cancel or revert an in-flight rename: the signal must die
        // with them so it cannot re-trigger on later edits (Esc-cancel, undo/redo).
        private static readonly HashSet<string> DisarmCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Edit.SelectionCancel",
            "Edit.Undo",
            "Edit.Redo",
        };

        private SerializedFieldRenameWatcher? watcher;
        private EnvDTE.DTE? dte;
        private EnvDTE.CommandEvents? commandEvents;
        private readonly Dictionary<string, CommandClass> commandClassCache = new Dictionary<string, CommandClass>();

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            DiagnosticLog.Write("Package InitializeAsync started.");

            var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            var workspace = componentModel?.GetService<VisualStudioWorkspace>();

            if (workspace is null)
            {
                DiagnosticLog.Write("VisualStudioWorkspace was NOT available; watcher not started.");
                return;
            }

            var isRenameSessionActive = TryCreateInlineRenameProbe(componentModel);

            watcher = new SerializedFieldRenameWatcher(workspace, this.JoinableTaskFactory, isRenameSessionActive);
            watcher.Start();
            DiagnosticLog.Write(
                $"Watcher started; subscribed to WorkspaceChanged (inline-rename gate: {(isRenameSessionActive is null ? "off" : "on")}).");

            // Hook DTE command events on the UI thread so we can recognize the Rename
            // command independently of MEF. Keep the CommandEvents reference alive or
            // the events stop firing.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            dte = await GetServiceAsync(typeof(SDTE)) as EnvDTE.DTE;

            if (dte is not null)
            {
                commandEvents = dte.Events.CommandEvents;
                commandEvents.BeforeExecute += OnBeforeExecuteCommand;
                DiagnosticLog.Write("Subscribed to DTE command events for rename detection.");
            }
            else
            {
                DiagnosticLog.Write("DTE was NOT available; rename command events not hooked.");
            }
        }

        // Builds a best-effort probe for "is an inline-rename session active" WITHOUT
        // a compile-time dependency on the unstable VS-internal editor assemblies.
        // IInlineRenameService lives in Microsoft.CodeAnalysis.EditorFeatures.dll,
        // which VS ships but does not publish on NuGet at this version, so we resolve
        // it reflectively. Returns null if anything is missing — the watcher then
        // relies on its reactive self-heal instead, with no loss of correctness.
        private static Func<bool>? TryCreateInlineRenameProbe(IComponentModel? componentModel)
        {
            if (componentModel is null)
            {
                return null;
            }

            try
            {
                const string typeName = "Microsoft.CodeAnalysis.Editor.IInlineRenameService";

                var serviceType = Type.GetType($"{typeName}, Microsoft.CodeAnalysis.EditorFeatures", throwOnError: false);

                if (serviceType is null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        serviceType = assembly.GetType(typeName, throwOnError: false);

                        if (serviceType is not null)
                        {
                            break;
                        }
                    }
                }

                if (serviceType is null)
                {
                    DiagnosticLog.Write("IInlineRenameService type not found; inline-rename gate off.");
                    return null;
                }

                var getService = typeof(IComponentModel).GetMethod("GetService")?.MakeGenericMethod(serviceType);
                var service = getService?.Invoke(componentModel, null);
                var activeSessionProperty = serviceType.GetProperty("ActiveSession");

                if (service is null || activeSessionProperty is null)
                {
                    DiagnosticLog.Write("IInlineRenameService/ActiveSession not resolvable; inline-rename gate off.");
                    return null;
                }

                return () => activeSessionProperty.GetValue(service) != null;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Inline-rename probe setup failed; gate off: {exception.Message}");
                return null;
            }
        }

        private void OnBeforeExecuteCommand(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            // DTE command events are raised on the UI thread.
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                switch (ClassifyCommand(guid, id))
                {
                    case CommandClass.Rename:
                        RenameSignal.Arm(TryGetIdentifierAtCaret());
                        DiagnosticLog.Write($"Rename command observed via DTE ({guid}:{id}); RenameSignal armed.");
                        break;
                    case CommandClass.Disarm:
                        RenameSignal.Disarm();
                        break;
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"OnBeforeExecuteCommand threw: {exception}");
            }
        }

        private CommandClass ClassifyCommand(string guid, int id)
        {
            var key = guid + ":" + id;

            if (commandClassCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            string name;

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                name = dte?.Commands.Item(guid, id)?.Name ?? string.Empty;
            }
            catch
            {
                // Some commands cannot be resolved by name. Treat this occurrence
                // as non-rename but do NOT cache it: the failure may be transient
                // (COM hiccup) and must not permanently mask a real command.
                return CommandClass.None;
            }

            var commandClass = CommandClass.None;

            if (RenameCommandNames.Contains(name))
            {
                commandClass = CommandClass.Rename;
            }
            else if (DisarmCommandNames.Contains(name))
            {
                commandClass = CommandClass.Disarm;
            }

            commandClassCache[key] = commandClass;
            return commandClass;
        }

        // Reads the identifier under the caret via DTE so the rename signal can be
        // scoped to the symbol actually being renamed. Best effort — returns null
        // when anything is unavailable.
        private string? TryGetIdentifierAtCaret()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (!(dte?.ActiveDocument?.Selection is EnvDTE.TextSelection selection))
                {
                    return null;
                }

                var point = selection.ActivePoint;
                var line = point.CreateEditPoint().GetLines(point.Line, point.Line + 1);

                return IdentifierTextUtility.GetIdentifierAt(line, point.LineCharOffset - 1);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Could not read the identifier under the caret via DTE: {exception.Message}");
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (disposing)
            {
                if (commandEvents is not null)
                {
                    commandEvents.BeforeExecute -= OnBeforeExecuteCommand;
                    commandEvents = null;
                }

                dte = null;
                watcher?.Dispose();
                watcher = null;
            }

            base.Dispose(disposing);
        }
    }
}
