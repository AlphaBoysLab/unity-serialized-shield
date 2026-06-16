using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using VsCommanding = Microsoft.VisualStudio.Commanding;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Observes the editor Rename command so the workspace watcher can tell a real
    /// rename from ordinary typing. It never blocks the command — it only arms
    /// <see cref="RenameSignal"/> and returns "unhandled" so Roslyn's own rename
    /// proceeds normally.
    ///
    /// Ordered BEFORE Roslyn's own Rename handler so we still observe the command
    /// even though Roslyn marks it handled.
    /// </summary>
    [Export(typeof(VsCommanding.ICommandHandler))]
    [ContentType("CSharp")]
    [Name("UnitySerializedShield Rename Observer")]
    // "Rename" is Roslyn's PredefinedCommandHandlerNames.Rename; run before it so
    // we observe the command before Roslyn marks it handled.
    [Order(Before = "Rename")]
    internal sealed class SerializedFieldRenameCommandHandler : VsCommanding.ICommandHandler<RenameCommandArgs>
    {
        public string DisplayName => "UnitySerializedShield Rename Observer";

        public CommandState GetCommandState(RenameCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(RenameCommandArgs args, CommandExecutionContext executionContext)
        {
            RenameSignal.MarkInvoked();
            DiagnosticLog.Write("Rename command observed; RenameSignal armed.");

            // Return false: we are only listening, not handling the rename.
            return false;
        }
    }
}
