using System.Text.RegularExpressions;

namespace UnitySerializedShield.Core;

public static class FormerlySerializedAsBuilder
{
    private const string SerializationUsing = "using UnityEngine.Serialization;";
    private static readonly Regex SerializationUsingPattern = new(@"\b(?:global\s+)?using\s+UnityEngine\.Serialization\s*;");

    public static IReadOnlyList<TextInsertion> Build(string previousText, string currentText)
    {
        var previousFields = UniqueFieldsByKey(SerializedFieldParser.Parse(previousText));
        var currentFields = UniqueFieldsByKey(SerializedFieldParser.Parse(currentText));
        var endOfLine = TextUtils.DetectLineEnding(currentText);
        var insertions = new List<TextInsertion>();

        foreach (var (key, previousField) in previousFields)
        {
            if (!currentFields.TryGetValue(key, out var currentField) || currentField.Name == previousField.Name)
            {
                continue;
            }

            if (HasFormerlySerializedAs(currentField.AttributesText, previousField.SerializedName))
            {
                continue;
            }

            insertions.Add(new TextInsertion(
                currentField.InsertOffset,
                $"{currentField.Indent}[FormerlySerializedAs(\"{TextUtils.EscapeCSharpString(previousField.SerializedName)}\")]{endOfLine}"));
        }

        if (insertions.Count > 0 && !SerializationUsingPattern.IsMatch(currentText))
        {
            insertions.Insert(0, new TextInsertion(
                FindUsingInsertOffset(currentText),
                $"{SerializationUsing}{endOfLine}"));
        }

        return insertions;
    }

    public static string ApplyInsertions(string text, IEnumerable<TextInsertion> insertions)
    {
        return insertions
            .OrderByDescending(insertion => insertion.Offset)
            .Aggregate(text, (updatedText, insertion) =>
                updatedText.Insert(insertion.Offset, insertion.Text));
    }

    private static Dictionary<string, SerializedField> UniqueFieldsByKey(IEnumerable<SerializedField> fields)
    {
        var groupedFields = fields
            .GroupBy(field => field.Key)
            .Where(group => group.Count() == 1);

        return groupedFields.ToDictionary(group => group.Key, group => group.Single());
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
