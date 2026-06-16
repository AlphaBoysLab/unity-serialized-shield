using System.Collections.Generic;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// Knowledge about how Unity decides which C# fields are serialized.
    /// Used to keep field-rename protection limited to fields Unity actually
    /// persists by name (and therefore can lose on a rename).
    /// </summary>
    internal static class UnitySerialization
    {
        /// <summary>Attribute simple names that force a field to be serialized.</summary>
        public static readonly HashSet<string> SerializeFieldAttributeNames = new(System.StringComparer.Ordinal)
        {
            "SerializeField",
            "SerializeFieldAttribute",
            "SerializeReference",
            "SerializeReferenceAttribute",
        };

        /// <summary>Attribute simple names that opt a field out of serialization.</summary>
        public static readonly HashSet<string> NonSerializedAttributeNames = new(System.StringComparer.Ordinal)
        {
            "NonSerialized",
            "NonSerializedAttribute",
            "HideInInspector", // does not stop serialization, but kept for future use
        };

        /// <summary>The migration attribute simple names.</summary>
        public static readonly HashSet<string> FormerlySerializedAsAttributeNames = new(System.StringComparer.Ordinal)
        {
            "FormerlySerializedAs",
            "FormerlySerializedAsAttribute",
        };

        /// <summary>
        /// Base type simple names that make a type's public/serialized instance
        /// fields participate in Unity serialization.
        /// </summary>
        public static readonly HashSet<string> SerializableUnityBaseTypeNames = new(System.StringComparer.Ordinal)
        {
            "MonoBehaviour",
            "ScriptableObject",
            "StateMachineBehaviour",
        };

        /// <summary>Fully qualified metadata names for the same bases (semantic check).</summary>
        public static readonly HashSet<string> SerializableUnityBaseMetadataNames = new(System.StringComparer.Ordinal)
        {
            "UnityEngine.MonoBehaviour",
            "UnityEngine.ScriptableObject",
            "UnityEngine.StateMachineBehaviour",
        };

        public const string FormerlySerializedAsAttributeShortName = "FormerlySerializedAs";
        public const string SerializationNamespace = "UnityEngine.Serialization";
        public const string SerializationUsingDirective = "using UnityEngine.Serialization;";
    }
}
