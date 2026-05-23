using System.Text.RegularExpressions;

namespace UnitySerializedShield.Core;

public static class FormerlySerializedAsBuilder
{
    private const string SerializationUsing = "using UnityEngine.Serialization;";
    private static readonly Regex SerializationUsingPattern = new(@"\b(?:global\s+)?using\s+UnityEngine\.Serialization\s*;");
    private static readonly Regex FieldLinePattern = new(
        @"^(?:(?:(?:public|private|protected|internal|static|readonly|const|volatile|new|unsafe)\s+)*)(?<type>.+?)\s+(?<name>@?[A-Za-z_]\w*)\s*(?:=.*)?;$");
    private static readonly Regex LeadingAttributesPattern = new(@"^\s*(?:\[[^\]\r\n]*\]\s*)+");

    public static IReadOnlyList<TextInsertion> Build(string previousText, string currentText)
    {
        var renames = FindRenamedSerializedFields(previousText, currentText);
        var endOfLine = TextUtils.DetectLineEnding(currentText);
        var insertions = new List<TextInsertion>();

        foreach (var rename in renames)
        {
            if (IsSelfMigration(rename))
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

    public static IReadOnlyList<TextRemoval> BuildSelfAttributeRemovals(string text)
    {
        var lines = TextUtils.SplitLines(text);
        var pendingAttributeLineIndexes = new List<int>();
        var removals = new List<TextRemoval>();

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var trimmedLine = TextUtils.StripLineComment(line.Text).Trim();

            if (IsAttributeOnlyLine(trimmedLine))
            {
                pendingAttributeLineIndexes.Add(lineIndex);
                continue;
            }

            if (pendingAttributeLineIndexes.Count == 0)
            {
                continue;
            }

            if (TryGetFieldSerializedName(trimmedLine, out var serializedName))
            {
                foreach (var attributeLineIndex in pendingAttributeLineIndexes)
                {
                    var attributeLine = lines[attributeLineIndex];

                    if (!IsFormerlySerializedAsLineFor(attributeLine.Text, serializedName))
                    {
                        continue;
                    }

                    removals.Add(new TextRemoval(
                        attributeLine.Offset,
                        attributeLine.Text.Length + attributeLine.EndOfLine.Length));
                }
            }

            pendingAttributeLineIndexes.Clear();
        }

        return removals;
    }

    public static string ApplyRemovals(string text, IEnumerable<TextRemoval> removals)
    {
        return removals
            .OrderByDescending(removal => removal.Offset)
            .Aggregate(text, (updatedText, removal) =>
                updatedText.Remove(removal.Offset, removal.Length));
    }

    public static string ApplyEdits(
        string text,
        IEnumerable<TextRemoval> removals,
        IEnumerable<TextInsertion> insertions)
    {
        var edits = removals
            .Select(removal => new TextEdit(removal.Offset, true, removal.Length, string.Empty))
            .Concat(insertions.Select(insertion => new TextEdit(insertion.Offset, false, 0, insertion.Text)))
            .OrderByDescending(edit => edit.Offset)
            .ThenByDescending(edit => edit.IsRemoval);

        return edits.Aggregate(text, (updatedText, edit) =>
            edit.IsRemoval
                ? updatedText.Remove(edit.Offset, edit.Length)
                : updatedText.Insert(edit.Offset, edit.Text));
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

    private static bool TryGetFieldSerializedName(string line, out string serializedName)
    {
        serializedName = string.Empty;
        var declaration = LeadingAttributesPattern.Replace(line, string.Empty).Trim();

        if (!declaration.EndsWith(';'))
        {
            return false;
        }

        var declarationBeforeInitializer = declaration.Split('=')[0];

        if (declarationBeforeInitializer.Contains('(', StringComparison.Ordinal)
            || declarationBeforeInitializer.Contains('{', StringComparison.Ordinal)
            || declarationBeforeInitializer.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        var match = FieldLinePattern.Match(declaration);

        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["name"].Value;
        serializedName = name.StartsWith('@') ? name[1..] : name;
        return true;
    }

    private static bool IsAttributeOnlyLine(string trimmedLine)
    {
        return trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']');
    }

    private static bool IsFormerlySerializedAsLineFor(string line, string serializedName)
    {
        var escapedName = Regex.Escape(serializedName);
        var pattern = new Regex(
            $@"^\s*\[\s*(?:UnityEngine\.Serialization\.)?FormerlySerializedAs(?:Attribute)?\s*\(\s*""{escapedName}""\s*\)\s*\]\s*$");

        return pattern.IsMatch(TextUtils.StripLineComment(line));
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

    private static bool IsSelfMigration(SerializedFieldRename rename)
    {
        return string.Equals(rename.PreviousSerializedName, rename.CurrentSerializedName, StringComparison.Ordinal)
            || string.Equals(rename.PreviousSerializedName, rename.CurrentField.SerializedName, StringComparison.Ordinal)
            || string.Equals(rename.PreviousName, rename.CurrentName, StringComparison.Ordinal);
    }

    private sealed record TextEdit(int Offset, bool IsRemoval, int Length, string Text);
}

public sealed record SerializedFieldRename(
    string PreviousName,
    string PreviousSerializedName,
    string CurrentName,
    string CurrentSerializedName,
    SerializedField CurrentField);
