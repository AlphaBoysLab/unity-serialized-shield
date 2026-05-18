namespace UnitySerializedShield.Core;

public sealed record SerializedField(
    string Name,
    string SerializedName,
    string Key,
    int InsertOffset,
    string Indent,
    string AttributesText);
