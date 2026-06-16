using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Auto-loaded package that subscribes a <see cref="SerializedFieldRenameWatcher"/>
    /// to the live Roslyn <see cref="VisualStudioWorkspace"/> as soon as a solution
    /// is open. This is what enables solution-wide, declaration-anchored
    /// [FormerlySerializedAs] insertion on serialized-field renames.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class UnitySerializedShieldPackage : AsyncPackage
    {
        public const string PackageGuidString = "8f1d3c54-7a21-4b8e-9c33-1f5a2d7e9b40";

        private SerializedFieldRenameWatcher? watcher;

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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                watcher?.Dispose();
                watcher = null;
            }

            base.Dispose(disposing);
        }
    }
}
