using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// A single Unity-serialized field declaration discovered in a syntax tree,
    /// together with the structural identity used to match it across an edit.
    /// </summary>
    internal sealed record SerializedFieldInfo(
        FieldDeclarationSyntax Declaration,
        VariableDeclaratorSyntax Variable,
        string Name,
        string ContainingTypeKey,
        string MatchKey,
        int OrdinalInGroup,
        bool HasFormerlySerializedAsForName);
}
