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

            foreach (var field in currentFields)
            {
                currentByKey[BuildKey(field)] = field;
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
            // name; here we need the previous name specifically. Delegate to the
            // collector's helper via a fresh scan of the declaration's attributes.
            return SerializedFieldAttributes.HasFormerlySerializedAs(field.Declaration, previousName);
        }

        private static string BuildKey(SerializedFieldInfo field)
        {
            return field.ContainingTypeKey + "::" + field.MatchKey + "::" + field.OrdinalInGroup;
        }
    }
}
