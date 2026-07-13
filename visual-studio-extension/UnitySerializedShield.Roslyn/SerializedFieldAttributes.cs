using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>Shared helpers for reading attributes on a member declaration.</summary>
    internal static class SerializedFieldAttributes
    {
        public static string GetSimpleName(AttributeSyntax attribute)
        {
            return GetSimpleName(attribute.Name);
        }

        public static string GetSimpleName(NameSyntax name)
        {
            return name switch
            {
                QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                SimpleNameSyntax simple => simple.Identifier.ValueText,
                _ => name.ToString(),
            };
        }

        /// <summary>
        /// True if the member has <c>[FormerlySerializedAs("name")]</c> (any
        /// attribute target, simple or fully qualified attribute name).
        /// </summary>
        public static bool HasFormerlySerializedAs(MemberDeclarationSyntax member, string name)
        {
            foreach (var attribute in member.AttributeLists.SelectMany(list => list.Attributes))
            {
                if (!UnitySerialization.FormerlySerializedAsAttributeNames.Contains(GetSimpleName(attribute)))
                {
                    continue;
                }

                var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();

                if (argument?.Expression is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    && literal.Token.ValueText == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
