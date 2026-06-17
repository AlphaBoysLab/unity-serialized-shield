using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
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
    /// if the extension cache is stale.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class UnitySerializedShieldPackage : AsyncPackage
    {
        public const string PackageGuidString = "8f1d3c54-7a21-4b8e-9c33-1f5a2d7e9b40";

        private SerializedFieldRenameWatcher? watcher;
        private EnvDTE.DTE? dte;
        private EnvDTE.CommandEvents? commandEvents;
        private readonly Dictionary<string, bool> renameCommandCache = new();

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

            watcher = new SerializedFieldRenameWatcher(workspace, this.JoinableTaskFactory);
            watcher.Start();
            DiagnosticLog.Write("Watcher started; subscribed to WorkspaceChanged.");

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

        private void OnBeforeExecuteCommand(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            if (IsRenameCommand(guid, id))
            {
                RenameSignal.MarkInvoked();
                DiagnosticLog.Write($"Rename command observed via DTE ({guid}:{id}); RenameSignal armed.");
            }
        }

        private bool IsRenameCommand(string guid, int id)
        {
            var key = guid + ":" + id;

            if (renameCommandCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var isRename = false;

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var name = dte?.Commands.Item(guid, id)?.Name ?? string.Empty;
                isRename = name.IndexOf("Rename", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                // Some commands cannot be resolved by name; treat them as non-rename.
            }

            renameCommandCache[key] = isRename;
            return isRename;
        }

        protected override void Dispose(bool disposing)
        {
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
