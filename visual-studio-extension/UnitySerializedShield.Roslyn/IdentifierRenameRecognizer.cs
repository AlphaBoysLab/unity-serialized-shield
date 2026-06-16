using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// Safety filter that confirms an edit is a clean identifier substitution
    /// (every occurrence of the old name replaced by the new name and nothing
    /// else changed) — the shape a Rename Symbol / Ctrl+R produces.
    ///
    /// This guards against migrating when an edit changed unrelated text as well.
    /// It does NOT by itself distinguish a deliberate rename from someone typing a
    /// new name one keystroke at a time on a field with no references; the precise
    /// trigger for that is hooking the rename command (see SerializedFieldRenameWatcher).
    /// </summary>
    public static class IdentifierRenameRecognizer
    {
        /// <summary>
        /// True if applying every <paramref name="renames"/> substitution (as whole
        /// identifiers) to <paramref name="previousText"/> reproduces
        /// <paramref name="currentText"/> exactly — i.e. the edit changed only the
        /// renamed identifiers.
        /// </summary>
        public static bool IsRenameShaped(
            string previousText,
            string currentText,
            IReadOnlyList<RenamedSerializedField> renames)
        {
            if (renames.Count == 0)
            {
                return false;
            }

            var substituted = previousText;

            foreach (var rename in renames)
            {
                substituted = ReplaceWholeIdentifier(substituted, rename.PreviousName, rename.CurrentName);
            }

            return substituted == currentText;
        }

        private static string ReplaceWholeIdentifier(string text, string oldName, string newName)
        {
            // Match oldName only when it is a complete identifier token: not preceded
            // by an identifier char or '@', and not followed by an identifier char.
            var pattern = $@"(?<![\w@]){Regex.Escape(oldName)}(?![\w])";

            return Regex.Replace(text, pattern, newName.Replace("$", "$$"));
        }
    }
}
