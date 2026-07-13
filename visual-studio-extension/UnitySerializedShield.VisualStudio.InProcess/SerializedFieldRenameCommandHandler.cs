using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using VsCommanding = Microsoft.VisualStudio.Commanding;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Observes the editor Rename command so the workspace watcher can tell a real
    /// rename from ordinary typing. It never blocks the command — it only arms
    /// <see cref="RenameSignal"/> (recording the identifier under the caret, i.e.
    /// the symbol being renamed) and returns "unhandled" so Roslyn's own rename
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
            var identifier = TryGetIdentifierAtCaret(args);
            RenameSignal.Arm(identifier);
            DiagnosticLog.Write($"Rename command observed; RenameSignal armed (identifier: {identifier ?? "<unknown>"}).");

            // Return false: we are only listening, not handling the rename.
            return false;
        }

        private static string? TryGetIdentifierAtCaret(RenameCommandArgs args)
        {
            try
            {
                var caret = args.TextView.Caret.Position.BufferPosition;

                return IdentifierTextUtility.GetIdentifierAt(
                    caret.GetContainingLine().GetText(),
                    caret.Position - caret.GetContainingLine().Start.Position);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Could not read the identifier under the caret: {exception.Message}");
                return null;
            }
        }
    }

    /// <summary>Shared identifier extraction for the rename observers.</summary>
    internal static class IdentifierTextUtility
    {
        /// <summary>
        /// Returns the C# identifier that contains (or immediately precedes) the
        /// given column in <paramref name="lineText"/>, without any verbatim '@'
        /// prefix, or null when there is none.
        /// </summary>
        public static string? GetIdentifierAt(string lineText, int column)
        {
            if (lineText.Length == 0)
            {
                return null;
            }

            column = Math.Max(0, Math.Min(column, lineText.Length - 1));

            // Allow the caret to sit just past the identifier's last character.
            if (!IsIdentifierChar(lineText[column]) && column > 0 && IsIdentifierChar(lineText[column - 1]))
            {
                column--;
            }

            if (!IsIdentifierChar(lineText[column]))
            {
                return null;
            }

            var start = column;
            var end = column;

            while (start > 0 && IsIdentifierChar(lineText[start - 1]))
            {
                start--;
            }

            while (end + 1 < lineText.Length && IsIdentifierChar(lineText[end + 1]))
            {
                end++;
            }

            var identifier = lineText.Substring(start, end - start + 1);

            // A digit can't start an identifier; the caret was in a number literal.
            if (char.IsDigit(identifier[0]))
            {
                return null;
            }

            return identifier;
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }
}
