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
    ///
    /// For <c>[field: SerializeField]</c> auto-properties, the attribute is
    /// emitted as <c>[field: FormerlySerializedAs("&lt;old&gt;k__BackingField")]</c>
    /// because Unity serializes the compiler-generated backing field.
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

            var replacements = new Dictionary<MemberDeclarationSyntax, MemberDeclarationSyntax>();

            foreach (var rename in renames)
            {
                var member = rename.CurrentField.Declaration;

                // Multiple renames could target the same declaration; build on the
                // previously transformed version so attributes stack correctly.
                var source = replacements.TryGetValue(member, out var existing) ? existing : member;
                replacements[member] = AddMigrationAttribute(
                    source,
                    rename.CurrentField.GetSerializedName(rename.PreviousName),
                    rename.CurrentField.IsAutoProperty,
                    endOfLine);
            }

            var newRoot = root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);

            return EnsureSerializationUsing(newRoot, endOfLine);
        }

        private static MemberDeclarationSyntax AddMigrationAttribute(
            MemberDeclarationSyntax member,
            string previousSerializedName,
            bool targetBackingField,
            string endOfLine)
        {
            var leadingTrivia = member.GetLeadingTrivia();
            var indent = leadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
            var indentText = indent.IsKind(SyntaxKind.WhitespaceTrivia) ? indent.ToString() : string.Empty;

            var attributeArgument = AttributeArgument(
                LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    Literal(previousSerializedName)));

            var attribute = Attribute(
                IdentifierName(UnitySerialization.FormerlySerializedAsAttributeShortName),
                AttributeArgumentList(SingletonSeparatedList(attributeArgument)));

            var attributeList = AttributeList(SingletonSeparatedList(attribute));

            if (targetBackingField)
            {
                // `[field: ...]` — the attribute must reach the backing field.
                attributeList = attributeList.WithTarget(
                    AttributeTargetSpecifier(
                        Token(SyntaxKind.FieldKeyword),
                        Token(SyntaxKind.ColonToken).WithTrailingTrivia(Space)));
            }

            attributeList = attributeList
                .WithLeadingTrivia(leadingTrivia)
                .WithTrailingTrivia(EndOfLine(endOfLine), Whitespace(indentText));

            // The new attribute list now owns the member's original leading trivia
            // (newlines/comments) and re-emits the indentation, so the member itself
            // starts with no leading trivia.
            var memberWithoutLeading = member.WithLeadingTrivia(SyntaxTriviaList.Empty);

            return memberWithoutLeading.WithAttributeLists(
                memberWithoutLeading.AttributeLists.Insert(0, attributeList));
        }

        private static SyntaxNode EnsureSerializationUsing(SyntaxNode root, string endOfLine)
        {
            if (root is not CompilationUnitSyntax compilationUnit)
            {
                return root;
            }

            // Only a plain `using UnityEngine.Serialization;` makes the SHORT
            // attribute name resolvable. An alias (`using UES = ...;`) or a static
            // using does NOT import the namespace, so it must not count — inserting
            // the short name would then fail to compile (CS0246).
            var alreadyImported = compilationUnit
                .DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Any(directive => directive.Alias is null
                    && directive.StaticKeyword.IsKind(SyntaxKind.None)
                    && directive.Name?.ToString() == UnitySerialization.SerializationNamespace);

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
