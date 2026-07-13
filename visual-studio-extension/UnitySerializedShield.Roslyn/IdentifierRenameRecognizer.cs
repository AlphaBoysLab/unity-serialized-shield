using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// Safety filter that confirms an edit is a clean identifier substitution
    /// (every occurrence of the old name replaced by the new name and nothing
    /// else changed) — the shape a Rename Symbol / Ctrl+R produces.
    ///
    /// The comparison is token-level and trivia-ignoring: comments, whitespace,
    /// and documentation are excluded, and string literals are compared verbatim.
    /// Rename Symbol never edits comments or strings, so an old field name that
    /// merely appears in a comment or a string must not disable protection.
    ///
    /// This guards against migrating when an edit changed unrelated code as well.
    /// It does NOT by itself distinguish a deliberate rename from someone typing a
    /// new name one keystroke at a time on a field with no references; the precise
    /// trigger for that is hooking the rename command (see SerializedFieldRenameWatcher).
    /// </summary>
    public static class IdentifierRenameRecognizer
    {
        /// <summary>
        /// True if <paramref name="currentText"/> differs from
        /// <paramref name="previousText"/> only by the given identifier renames:
        /// every code token is unchanged except identifier tokens whose value is a
        /// rename's old name, all of which must now carry that rename's new name.
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

            var previousRoot = CSharpSyntaxTree.ParseText(previousText).GetRoot();
            var currentRoot = CSharpSyntaxTree.ParseText(currentText).GetRoot();

            return IsRenameShaped(previousRoot, currentRoot, renames);
        }

        /// <summary>
        /// Syntax-root overload so callers that already parsed both versions (the
        /// VSIX host) avoid re-parsing full documents on the hot path.
        /// </summary>
        public static bool IsRenameShaped(
            SyntaxNode previousRoot,
            SyntaxNode currentRoot,
            IReadOnlyList<RenamedSerializedField> renames)
        {
            if (renames.Count == 0)
            {
                return false;
            }

            using var previousTokens = previousRoot.DescendantTokens().GetEnumerator();
            using var currentTokens = currentRoot.DescendantTokens().GetEnumerator();

            while (true)
            {
                var hasPrevious = previousTokens.MoveNext();
                var hasCurrent = currentTokens.MoveNext();

                if (hasPrevious != hasCurrent)
                {
                    // Token counts differ — code was added or removed, not renamed.
                    return false;
                }

                if (!hasPrevious)
                {
                    return true;
                }

                if (!TokensMatch(previousTokens.Current, currentTokens.Current, renames))
                {
                    return false;
                }
            }
        }

        private static bool TokensMatch(
            SyntaxToken previous,
            SyntaxToken current,
            IReadOnlyList<RenamedSerializedField> renames)
        {
            if (previous.RawKind != current.RawKind)
            {
                return false;
            }

            if (!previous.IsKind(SyntaxKind.IdentifierToken))
            {
                // Keywords, punctuation, and literals (including strings that
                // mention the old name) must be byte-identical.
                return previous.Text == current.Text;
            }

            // ValueText strips a verbatim '@' prefix, so `@class` compares as
            // "class" — matching how the collector reports field names.
            var previousName = previous.ValueText;
            var currentName = current.ValueText;

            foreach (var rename in renames)
            {
                if (previousName == rename.PreviousName)
                {
                    // EVERY occurrence of a renamed identifier must now carry the
                    // new name; a partially applied substitution is manual typing.
                    return currentName == rename.CurrentName;
                }
            }

            return previous.Text == current.Text;
        }
    }
}
