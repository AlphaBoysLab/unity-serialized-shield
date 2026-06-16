using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// Adds <c>[FormerlySerializedAs("old")]</c> attributes (and the
    /// <c>using UnityEngine.Serialization;</c> directive when missing) to a
    /// syntax tree for a set of detected renames, preserving indentation.
    /// </summary>
    internal static class FormerlySerializedAsRewriter
    {
        public static SyntaxNode AddMigrationAttributes(
            SyntaxNode root,
            IReadOnlyList<SerializedFieldRename> renames,
            string endOfLine)
        {
            if (renames.Count == 0)
            {
                return root;
            }

            var replacements = new Dictionary<FieldDeclarationSyntax, FieldDeclarationSyntax>();

            foreach (var rename in renames)
            {
                var field = rename.CurrentField.Declaration;

                // Multiple renames could target the same declaration; build on the
                // previously transformed version so attributes stack correctly.
                var source = replacements.TryGetValue(field, out var existing) ? existing : field;
                replacements[field] = AddMigrationAttribute(source, rename.PreviousName, endOfLine);
            }

            var newRoot = root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);

            return EnsureSerializationUsing(newRoot, endOfLine);
        }

        private static FieldDeclarationSyntax AddMigrationAttribute(
            FieldDeclarationSyntax field,
            string previousName,
            string endOfLine)
        {
            var leadingTrivia = field.GetLeadingTrivia();
            var indent = leadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
            var indentText = indent.IsKind(SyntaxKind.WhitespaceTrivia) ? indent.ToString() : string.Empty;

            var attributeArgument = AttributeArgument(
                LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    Literal(previousName)));

            var attribute = Attribute(
                IdentifierName(UnitySerialization.FormerlySerializedAsAttributeShortName),
                AttributeArgumentList(SingletonSeparatedList(attributeArgument)));

            var attributeList = AttributeList(SingletonSeparatedList(attribute))
                .WithLeadingTrivia(leadingTrivia)
                .WithTrailingTrivia(EndOfLine(endOfLine), Whitespace(indentText));

            // The new attribute list now owns the field's original leading trivia
            // (newlines/comments) and re-emits the indentation, so the field itself
            // starts with no leading trivia.
            var fieldWithoutLeading = field.WithLeadingTrivia(SyntaxTriviaList.Empty);

            return fieldWithoutLeading.WithAttributeLists(
                fieldWithoutLeading.AttributeLists.Insert(0, attributeList));
        }

        private static SyntaxNode EnsureSerializationUsing(SyntaxNode root, string endOfLine)
        {
            if (root is not CompilationUnitSyntax compilationUnit)
            {
                return root;
            }

            var alreadyImported = compilationUnit
                .DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Any(directive => directive.Name?.ToString() == UnitySerialization.SerializationNamespace);

            if (alreadyImported)
            {
                return root;
            }

            var usingDirective = UsingDirective(ParseName(UnitySerialization.SerializationNamespace).WithLeadingTrivia(Space))
                .WithTrailingTrivia(EndOfLine(endOfLine));

            if (compilationUnit.Usings.Count > 0)
            {
                var last = compilationUnit.Usings[compilationUnit.Usings.Count - 1];
                usingDirective = usingDirective.WithLeadingTrivia(last.GetLeadingTrivia());
                return compilationUnit.InsertNodesAfter(last, new[] { usingDirective });
            }

            return compilationUnit.WithUsings(compilationUnit.Usings.Insert(0, usingDirective));
        }
    }
}
