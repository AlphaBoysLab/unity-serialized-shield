using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>A serialized-field rename surfaced to callers.</summary>
    public sealed record RenamedSerializedField(string PreviousName, string CurrentName);

    /// <summary>
    /// Public entry point for Roslyn-based serialized-field rename protection.
    ///
    /// Given a document's syntax before and after an edit, it detects renamed
    /// Unity-serialized fields and returns the new source with
    /// <c>[FormerlySerializedAs("old")]</c> applied at each declaration.
    ///
    /// The semantic-model overloads are used by the in-process VSIX host, where a
    /// live <see cref="VisualStudioWorkspace"/> resolves containing types exactly.
    /// </summary>
    public static class SerializedFieldMigrator
    {
        /// <summary>
        /// Returns the migrated form of <paramref name="currentSource"/>, or the
        /// unchanged string if no serialized-field rename was detected.
        /// </summary>
        public static string Migrate(string previousSource, string currentSource)
        {
            var previousRoot = CSharpSyntaxTree.ParseText(previousSource).GetRoot();
            var currentRoot = CSharpSyntaxTree.ParseText(currentSource).GetRoot();
            var newRoot = Migrate(previousRoot, currentRoot, null, null, out _);

            return newRoot is null ? currentSource : newRoot.ToFullString();
        }

        /// <summary>
        /// Detects renames and applies migration attributes at the syntax level.
        /// Returns the new root, or <c>null</c> when there was nothing to change.
        /// </summary>
        public static SyntaxNode? Migrate(
            SyntaxNode previousRoot,
            SyntaxNode currentRoot,
            SemanticModel? previousModel,
            SemanticModel? currentModel,
            out IReadOnlyList<RenamedSerializedField> renames)
        {
            var detected = SerializedFieldRenameDetector.Detect(previousRoot, currentRoot, previousModel, currentModel);
            renames = detected
                .Select(rename => new RenamedSerializedField(rename.PreviousName, rename.CurrentName))
                .ToList();

            if (detected.Count == 0)
            {
                return null;
            }

            var endOfLine = DetectEndOfLine(currentRoot.ToFullString());

            return FormerlySerializedAsRewriter.AddMigrationAttributes(currentRoot, detected, endOfLine);
        }

        /// <summary>Reports detected serialized-field renames without rewriting.</summary>
        public static IReadOnlyList<RenamedSerializedField> FindRenames(string previousSource, string currentSource)
        {
            var previousRoot = CSharpSyntaxTree.ParseText(previousSource).GetRoot();
            var currentRoot = CSharpSyntaxTree.ParseText(currentSource).GetRoot();

            return SerializedFieldRenameDetector.Detect(previousRoot, currentRoot)
                .Select(rename => new RenamedSerializedField(rename.PreviousName, rename.CurrentName))
                .ToList();
        }

        private static string DetectEndOfLine(string text)
        {
            var index = text.IndexOf('\n');

            if (index > 0 && text[index - 1] == '\r')
            {
                return "\r\n";
            }

            return index >= 0 ? "\n" : "\r\n";
        }
    }
}
