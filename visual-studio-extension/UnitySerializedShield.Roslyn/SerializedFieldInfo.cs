using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// A single Unity-serialized member discovered in a syntax tree, together
    /// with the structural identity used to match it across an edit.
    ///
    /// This is either a field declaration or a <c>[field: SerializeField]</c>
    /// auto-property, whose compiler-generated backing field Unity serializes as
    /// <c>&lt;Name&gt;k__BackingField</c>.
    /// </summary>
    internal sealed record SerializedFieldInfo(
        MemberDeclarationSyntax Declaration,
        string Name,
        string ContainingTypeKey,
        string MatchKey,
        int OrdinalInGroup,
        bool HasFormerlySerializedAsForName,
        bool IsAutoProperty)
    {
        /// <summary>
        /// The name Unity uses in serialized data for a member called
        /// <paramref name="memberName"/> — the backing-field form for
        /// auto-properties, the plain name for fields.
        /// </summary>
        public string GetSerializedName(string memberName)
        {
            return IsAutoProperty ? "<" + memberName + ">k__BackingField" : memberName;
        }
    }
}
