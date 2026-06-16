namespace UnitySerializedShield.Roslyn
{
    /// <summary>
    /// A detected rename of a Unity-serialized field. <see cref="CurrentField"/>
    /// refers to the declaration in the new (post-edit) syntax tree, which is
    /// where the <c>[FormerlySerializedAs(PreviousName)]</c> attribute belongs.
    /// </summary>
    internal sealed record SerializedFieldRename(string PreviousName, string CurrentName, SerializedFieldInfo CurrentField);
}
