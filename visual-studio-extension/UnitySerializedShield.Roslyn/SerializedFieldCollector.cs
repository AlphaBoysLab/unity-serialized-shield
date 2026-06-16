using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// Walks a syntax tree and returns the Unity-serialized fields in it.
    ///
    /// Detection is syntactic so it works without referenced Unity assemblies,
    /// but an optional <see cref="SemanticModel"/> is used to confirm the
    /// containing type derives from a serializable Unity base when available.
    ///
    /// The collector is intentionally conservative: ambiguous declarations are
    /// skipped rather than risk inserting a wrong migration attribute.
    /// </summary>
    internal static class SerializedFieldCollector
    {
        public static IReadOnlyList<SerializedFieldInfo> Collect(SyntaxNode root, SemanticModel? semanticModel = null)
        {
            var results = new List<SerializedFieldInfo>();
            var groupCounters = new Dictionary<string, int>(System.StringComparer.Ordinal);

            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                if (!TryDescribe(field, semanticModel, out var name, out var containingTypeKey, out var matchKeyBase, out var hasFsa))
                {
                    continue;
                }

                var groupKey = containingTypeKey + "::" + matchKeyBase;
                groupCounters.TryGetValue(groupKey, out var ordinal);
                groupCounters[groupKey] = ordinal + 1;

                results.Add(new SerializedFieldInfo(
                    field,
                    field.Declaration.Variables[0],
                    name,
                    containingTypeKey,
                    matchKeyBase,
                    ordinal,
                    hasFsa));
            }

            return results;
        }

        private static bool TryDescribe(
            FieldDeclarationSyntax field,
            SemanticModel? semanticModel,
            out string name,
            out string containingTypeKey,
            out string matchKey,
            out bool hasFormerlySerializedAsForName)
        {
            name = string.Empty;
            containingTypeKey = string.Empty;
            matchKey = string.Empty;
            hasFormerlySerializedAsForName = false;

            // Conservative skip: multi-declarator fields such as `int a, b;`.
            if (field.Declaration.Variables.Count != 1)
            {
                return false;
            }

            var modifiers = field.Modifiers;

            if (modifiers.Any(SyntaxKind.StaticKeyword)
                || modifiers.Any(SyntaxKind.ConstKeyword)
                || modifiers.Any(SyntaxKind.ReadOnlyKeyword))
            {
                return false;
            }

            var attributeNames = field.AttributeLists
                .SelectMany(list => list.Attributes)
                .Select(SerializedFieldAttributes.GetSimpleName)
                .ToList();

            if (attributeNames.Any(UnitySerialization.NonSerializedAttributeNames.Contains))
            {
                return false;
            }

            var hasSerializeField = attributeNames.Any(UnitySerialization.SerializeFieldAttributeNames.Contains);
            var isPublic = modifiers.Any(SyntaxKind.PublicKeyword);

            if (!hasSerializeField)
            {
                // Public fields are serialized only inside a serializable Unity type.
                if (!isPublic || !IsInSerializableUnityType(field, semanticModel))
                {
                    return false;
                }
            }

            var variable = field.Declaration.Variables[0];
            name = variable.Identifier.ValueText;

            containingTypeKey = GetContainingTypeKey(field);

            // Structural identity that survives an identifier-only rename:
            // type + non-migration attributes + modifiers (name and initializer excluded).
            var typeText = Normalize(field.Declaration.Type.ToString());
            var modifierText = Normalize(string.Join(" ", modifiers.Select(m => m.ValueText)));
            var attributeText = Normalize(string.Join(",", attributeNames
                .Where(n => !UnitySerialization.FormerlySerializedAsAttributeNames.Contains(n))
                .OrderBy(n => n, System.StringComparer.Ordinal)));
            matchKey = typeText + "|" + modifierText + "|" + attributeText;

            hasFormerlySerializedAsForName = SerializedFieldAttributes.HasFormerlySerializedAs(field, name);
            return true;
        }

        private static bool IsInSerializableUnityType(FieldDeclarationSyntax field, SemanticModel? semanticModel)
        {
            var typeDeclaration = field.FirstAncestorOrSelf<TypeDeclarationSyntax>();

            if (typeDeclaration is null)
            {
                return false;
            }

            // Semantic confirmation when a model is available (most accurate).
            if (semanticModel is not null
                && semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol)
            {
                for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
                {
                    if (UnitySerialization.SerializableUnityBaseMetadataNames.Contains(baseType.ToDisplayString()))
                    {
                        return true;
                    }
                }

                if (HasSerializableAttributeSymbol(typeSymbol))
                {
                    return true;
                }
            }

            // Syntactic fallback: base list names or [Serializable] on the type.
            if (typeDeclaration.BaseList is not null)
            {
                foreach (var baseType in typeDeclaration.BaseList.Types)
                {
                    if (UnitySerialization.SerializableUnityBaseTypeNames.Contains(GetSimpleTypeName(baseType.Type)))
                    {
                        return true;
                    }
                }
            }

            return typeDeclaration.AttributeLists
                .SelectMany(list => list.Attributes)
                .Select(SerializedFieldAttributes.GetSimpleName)
                .Any(n => n is "Serializable" or "SerializableAttribute");
        }

        private static bool HasSerializableAttributeSymbol(INamedTypeSymbol typeSymbol)
        {
            foreach (var attribute in typeSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == "System.SerializableAttribute")
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetContainingTypeKey(SyntaxNode node)
        {
            var parts = new List<string>();

            for (var current = node.Parent; current is not null; current = current.Parent)
            {
                switch (current)
                {
                    case BaseTypeDeclarationSyntax type:
                        parts.Insert(0, type.Identifier.ValueText);
                        break;
                    case BaseNamespaceDeclarationSyntax ns:
                        parts.Insert(0, ns.Name.ToString());
                        break;
                }
            }

            return string.Join(".", parts);
        }

        private static string GetSimpleTypeName(TypeSyntax type)
        {
            return type switch
            {
                NameSyntax name => SerializedFieldAttributes.GetSimpleName(name),
                _ => type.ToString(),
            };
        }

        private static string Normalize(string text)
        {
            return string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
