using System.Text.RegularExpressions;

namespace UnitySerializedShield.Core;

public static class FormerlySerializedAsBuilder
{
    private const string SerializationUsing = "using UnityEngine.Serialization;";
    private static readonly Regex SerializationUsingPattern = new(@"\b(?:global\s+)?using\s+UnityEngine\.Serialization\s*;");

    public static IReadOnlyList<TextInsertion> Build(string previousText, string currentText)
    {
        var renames = FindRenamedSerializedFields(previousText, currentText);
        var endOfLine = TextUtils.DetectLineEnding(currentText);
        var insertions = new List<TextInsertion>();

        foreach (var rename in renames)
        {
            if (rename.PreviousSerializedName == rename.CurrentSerializedName)
            {
                continue;
            }

            if (HasFormerlySerializedAs(rename.CurrentField.AttributesText, rename.PreviousSerializedName))
            {
                continue;
            }

            insertions.Add(new TextInsertion(
                rename.CurrentField.InsertOffset,
                $"{rename.CurrentField.Indent}[FormerlySerializedAs(\"{TextUtils.EscapeCSharpString(rename.PreviousSerializedName)}\")]{endOfLine}"));
        }

        if (insertions.Count > 0 && !SerializationUsingPattern.IsMatch(currentText))
        {
            insertions.Insert(0, new TextInsertion(
                FindUsingInsertOffset(currentText),
                $"{SerializationUsing}{endOfLine}"));
        }

        return insertions;
    }

    public static IReadOnlyList<SerializedFieldRename> FindRenamedSerializedFields(string previousText, string currentText)
    {
        var previousFields = FieldsByKey(SerializedFieldParser.Parse(previousText));
        var currentFields = FieldsByKey(SerializedFieldParser.Parse(currentText));
        var renames = new List<SerializedFieldRename>();

        foreach (var (key, previousGroup) in previousFields)
        {
            if (!currentFields.TryGetValue(key, out var currentGroup)
                || currentGroup.Count != previousGroup.Count)
            {
                continue;
            }

            for (var index = 0; index < previousGroup.Count; index++)
            {
                var previousField = previousGroup[index];
                var currentField = currentGroup[index];

                if (currentField.Name == previousField.Name
                    || currentField.SerializedName == previousField.SerializedName)
                {
                    continue;
                }

                renames.Add(new SerializedFieldRename(
                    previousField.Name,
                    previousField.SerializedName,
                    currentField.Name,
                    currentField.SerializedName,
                    currentField));
            }
        }

        return renames;
    }

    public static string ApplyInsertions(string text, IEnumerable<TextInsertion> insertions)
    {
        return insertions
            .OrderByDescending(insertion => insertion.Offset)
            .Aggregate(text, (updatedText, insertion) =>
                updatedText.Insert(insertion.Offset, insertion.Text));
    }

    private static Dictionary<string, List<SerializedField>> FieldsByKey(IEnumerable<SerializedField> fields)
    {
        return fields
            .GroupBy(field => field.Key)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private static bool HasFormerlySerializedAs(string attributesText, string previousName)
    {
        var escapedName = Regex.Escape(previousName);
        var pattern = new Regex(
            $@"\b(?:UnityEngine\.Serialization\.)?FormerlySerializedAs(?:Attribute)?\s*\(\s*""{escapedName}""\s*\)");

        return pattern.IsMatch(attributesText);
    }

    private static int FindUsingInsertOffset(string text)
    {
        var lines = TextUtils.SplitLines(text);
        var insertOffset = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        int? lastUsingEndOffset = null;

        foreach (var line in lines)
        {
            var trimmedLine = TextUtils.StripLineComment(line.Text).Trim();
            var lineEndOffset = line.Offset + line.Text.Length + line.EndOfLine.Length;

            if (trimmedLine == string.Empty || trimmedLine.StartsWith("//", StringComparison.Ordinal))
            {
                if (lastUsingEndOffset is null)
                {
                    insertOffset = lineEndOffset;
                }

                continue;
            }

            if (Regex.IsMatch(trimmedLine, @"^(?:global\s+)?using\s+"))
            {
                lastUsingEndOffset = lineEndOffset;
                continue;
            }

            break;
        }

        return lastUsingEndOffset ?? insertOffset;
    }
}

public sealed record SerializedFieldRename(
    string PreviousName,
    string PreviousSerializedName,
    string CurrentName,
    string CurrentSerializedName,
    SerializedField CurrentField);
