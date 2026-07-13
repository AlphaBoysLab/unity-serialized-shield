using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// Walks a syntax tree and returns the Unity-serialized members in it:
    /// serialized field declarations and <c>[field: SerializeField]</c>
    /// auto-properties (whose backing field Unity serializes).
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

            foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                bool described;
                string name;
                string containingTypeKey;
                string matchKeyBase;
                bool hasFsa;
                bool isAutoProperty;

                switch (member)
                {
                    case FieldDeclarationSyntax field:
                        described = TryDescribeField(field, semanticModel, out name, out containingTypeKey, out matchKeyBase, out hasFsa);
                        isAutoProperty = false;
                        break;
                    case PropertyDeclarationSyntax property:
                        described = TryDescribeAutoProperty(property, out name, out containingTypeKey, out matchKeyBase, out hasFsa);
                        isAutoProperty = true;
                        break;
                    default:
                        continue;
                }

                if (!described)
                {
                    continue;
                }

                var groupKey = containingTypeKey + "::" + matchKeyBase;
                groupCounters.TryGetValue(groupKey, out var ordinal);
                groupCounters[groupKey] = ordinal + 1;

                results.Add(new SerializedFieldInfo(
                    member,
                    name,
                    containingTypeKey,
                    matchKeyBase,
                    ordinal,
                    hasFsa,
                    isAutoProperty));
            }

            return results;
        }

        private static bool TryDescribeField(
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

        private static bool TryDescribeAutoProperty(
            PropertyDeclarationSyntax property,
            out string name,
            out string containingTypeKey,
            out string matchKey,
            out bool hasFormerlySerializedAsForName)
        {
            name = string.Empty;
            containingTypeKey = string.Empty;
            matchKey = string.Empty;
            hasFormerlySerializedAsForName = false;

            if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                return false;
            }

            // Only auto-properties have a compiler-generated backing field.
            if (property.ExpressionBody is not null
                || property.AccessorList is null
                || property.AccessorList.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
            {
                return false;
            }

            // Unity serializes the backing field only when [field: SerializeField]
            // (or SerializeReference) targets it explicitly.
            var fieldTargetedNames = property.AttributeLists
                .Where(list => list.Target?.Identifier.IsKind(SyntaxKind.FieldKeyword) ?? false)
                .SelectMany(list => list.Attributes)
                .Select(SerializedFieldAttributes.GetSimpleName)
                .ToList();

            if (!fieldTargetedNames.Any(UnitySerialization.SerializeFieldAttributeNames.Contains)
                || fieldTargetedNames.Any(UnitySerialization.NonSerializedAttributeNames.Contains))
            {
                return false;
            }

            name = property.Identifier.ValueText;
            containingTypeKey = GetContainingTypeKey(property);

            var allAttributeNames = property.AttributeLists
                .SelectMany(list => list.Attributes)
                .Select(SerializedFieldAttributes.GetSimpleName);
            var typeText = Normalize(property.Type.ToString());
            var modifierText = Normalize(string.Join(" ", property.Modifiers.Select(m => m.ValueText)));
            var attributeText = Normalize(string.Join(",", allAttributeNames
                .Where(n => !UnitySerialization.FormerlySerializedAsAttributeNames.Contains(n))
                .OrderBy(n => n, System.StringComparer.Ordinal)));
            matchKey = "property|" + typeText + "|" + modifierText + "|" + attributeText;

            hasFormerlySerializedAsForName = SerializedFieldAttributes.HasFormerlySerializedAs(
                property,
                "<" + name + ">k__BackingField");
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
                && semanticModel.SyntaxTree == typeDeclaration.SyntaxTree
                && semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol)
            {
                var sawErrorType = false;

                for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
                {
                    if (baseType.TypeKind == TypeKind.Error)
                    {
                        sawErrorType = true;
                        break;
                    }

                    if (UnitySerialization.SerializableUnityBaseMetadataNames.Contains(baseType.ToDisplayString()))
                    {
                        return true;
                    }
                }

                if (HasSerializableAttributeSymbol(typeSymbol))
                {
                    return true;
                }

                // The whole base chain resolved and contains no Unity base: trust
                // the semantic answer instead of the permissive syntactic guess.
                if (!sawErrorType)
                {
                    return HasSerializableAttributeSyntax(typeDeclaration);
                }
            }

            // Syntactic fallback: a known Unity base name, or — permissively — any
            // unrecognized base class, which may derive from a Unity base declared
            // in another file that a syntax-only view cannot see (interfaces are
            // excluded by the I-prefix convention).
            if (typeDeclaration.BaseList is not null)
            {
                foreach (var baseType in typeDeclaration.BaseList.Types)
                {
                    var simpleName = GetSimpleTypeName(baseType.Type);

                    if (UnitySerialization.SerializableUnityBaseTypeNames.Contains(simpleName)
                        || !LooksLikeInterfaceName(simpleName))
                    {
                        return true;
                    }
                }
            }

            return HasSerializableAttributeSyntax(typeDeclaration);
        }

        private static bool HasSerializableAttributeSyntax(TypeDeclarationSyntax typeDeclaration)
        {
            return typeDeclaration.AttributeLists
                .SelectMany(list => list.Attributes)
                .Select(SerializedFieldAttributes.GetSimpleName)
                .Any(n => n is "Serializable" or "SerializableAttribute");
        }

        private static bool LooksLikeInterfaceName(string name)
        {
            return name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]);
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
