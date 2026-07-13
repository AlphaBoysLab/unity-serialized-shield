using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// Compares the serialized fields of a document before and after an edit and
    /// reports identifier-only renames that still need a migration attribute.
    /// </summary>
    internal static class SerializedFieldRenameDetector
    {
        public static IReadOnlyList<SerializedFieldRename> Detect(
            SyntaxNode previousRoot,
            SyntaxNode currentRoot,
            SemanticModel? previousModel = null,
            SemanticModel? currentModel = null)
        {
            var previousFields = SerializedFieldCollector.Collect(previousRoot, previousModel);
            var currentFields = SerializedFieldCollector.Collect(currentRoot, currentModel);

            var currentByKey = new Dictionary<string, SerializedFieldInfo>(System.StringComparer.Ordinal);
            var previousNames = new HashSet<string>(System.StringComparer.Ordinal);
            var currentNames = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var field in currentFields)
            {
                currentByKey[BuildKey(field)] = field;
                currentNames.Add(ScopedName(field));
            }

            foreach (var field in previousFields)
            {
                previousNames.Add(ScopedName(field));
            }

            var renames = new List<SerializedFieldRename>();

            foreach (var previous in previousFields)
            {
                if (!currentByKey.TryGetValue(BuildKey(previous), out var current))
                {
                    continue;
                }

                if (current.Name == previous.Name)
                {
                    continue;
                }

                // A rename replaces the old name with the new one. If the OLD name
                // still exists in the type, the fields were reordered or swapped,
                // and if the NEW name already existed before the edit, a deleted
                // field's data would be poured into an unrelated survivor. Both
                // would migrate wrong data — skip them.
                if (currentNames.Contains(previous.ContainingTypeKey + "::" + previous.Name)
                    || previousNames.Contains(current.ContainingTypeKey + "::" + current.Name))
                {
                    continue;
                }

                // The new declaration already carries the migration attribute for the
                // old name (e.g. a re-run) — nothing to add.
                if (current.HasFormerlySerializedAsForName || HasFormerlySerializedAsFor(current, previous.Name))
                {
                    continue;
                }

                renames.Add(new SerializedFieldRename(previous.Name, current.Name, current));
            }

            return renames;
        }

        private static bool HasFormerlySerializedAsFor(SerializedFieldInfo field, string previousName)
        {
            // Re-collect intent: HasFormerlySerializedAsForName checks the *current*
            // name; here we need the previous name specifically (in its serialized
            // form — backing-field syntax for auto-properties).
            return SerializedFieldAttributes.HasFormerlySerializedAs(
                field.Declaration,
                field.GetSerializedName(previousName));
        }

        private static string ScopedName(SerializedFieldInfo field)
        {
            return field.ContainingTypeKey + "::" + field.Name;
        }

        private static string BuildKey(SerializedFieldInfo field)
        {
            return field.ContainingTypeKey + "::" + field.MatchKey + "::" + field.OrdinalInGroup;
        }
    }
}
