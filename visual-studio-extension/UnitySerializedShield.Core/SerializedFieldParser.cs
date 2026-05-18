using System.Text.RegularExpressions;

namespace UnitySerializedShield.Core;

public static class SerializedFieldParser
{
    private static readonly Regex SerializedFieldPattern = new(@"\b(?:UnityEngine\.)?SerializeField(?:Attribute)?\b");
    private static readonly Regex FieldPattern = new(
        @"^(?:(?<modifiers>(?:(?:public|private|protected|internal|static|readonly|const|volatile|new|unsafe)\s+)*))?(?<type>.+?)\s+(?<name>@?[A-Za-z_]\w*)\s*(?<tail>=.*)?;$");
    private static readonly Regex LeadingAttributesPattern = new(@"^\s*(?:\[[^\]\r\n]*\]\s*)+");
    private static readonly Regex FormerlySerializedAsAttributePattern = new(@"\[[^\]\r\n]*FormerlySerializedAs(?:Attribute)?[^\]\r\n]*\]");

    public static IReadOnlyList<SerializedField> Parse(string text)
    {
        var lines = TextUtils.SplitLines(text);
        var fields = new List<SerializedField>();

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var parsedField = ParseSerializedFieldAtLine(lines, lineIndex);

            if (parsedField is not null)
            {
                fields.Add(parsedField);
            }
        }

        return fields;
    }

    private static SerializedField? ParseSerializedFieldAtLine(IReadOnlyList<TextLine> lines, int lineIndex)
    {
        var fieldLine = TextUtils.StripLineComment(lines[lineIndex].Text).TrimEnd();
        var inlineAttributes = GetLeadingAttributes(fieldLine);
        var declaration = fieldLine[inlineAttributes.Length..].Trim();

        if (!declaration.EndsWith(';'))
        {
            return null;
        }

        var attributeStartLine = FindAttributeStartLine(lines, lineIndex);
        var attributesAbove = string.Join('\n', lines
            .Skip(attributeStartLine)
            .Take(lineIndex - attributeStartLine)
            .Select(line => line.Text));
        var attributesText = $"{attributesAbove}\n{inlineAttributes}";

        if (!SerializedFieldPattern.IsMatch(attributesText))
        {
            return null;
        }

        var declarationBeforeInitializer = declaration.Split('=')[0];

        if (declarationBeforeInitializer.Contains('(', StringComparison.Ordinal)
            || declarationBeforeInitializer.Contains('{', StringComparison.Ordinal)
            || declarationBeforeInitializer.Contains(',', StringComparison.Ordinal))
        {
            return null;
        }

        var fieldMatch = FieldPattern.Match(declaration);

        if (!fieldMatch.Success)
        {
            return null;
        }

        var modifiers = fieldMatch.Groups["modifiers"].Value;

        if (Regex.IsMatch(modifiers, @"\b(?:static|const)\b"))
        {
            return null;
        }

        var name = fieldMatch.Groups["name"].Value;
        var serializedName = name.StartsWith('@') ? name[1..] : name;
        var indentMatch = Regex.Match(lines[attributeStartLine].Text, @"^\s*");
        var tail = fieldMatch.Groups["tail"].Success ? fieldMatch.Groups["tail"].Value : string.Empty;

        return new SerializedField(
            name,
            serializedName,
            BuildFieldKey(attributesText, modifiers, fieldMatch.Groups["type"].Value, tail),
            lines[attributeStartLine].Offset,
            indentMatch.Value,
            attributesText);
    }

    private static string BuildFieldKey(string attributesText, string modifiers, string typeName, string tail)
    {
        var normalizedAttributes = TextUtils.NormalizeWhitespace(
            FormerlySerializedAsAttributePattern.Replace(attributesText, string.Empty));
        var normalizedModifiers = TextUtils.NormalizeWhitespace(modifiers);
        var normalizedType = TextUtils.NormalizeWhitespace(typeName);
        var normalizedTail = TextUtils.NormalizeWhitespace(tail);

        return $"{normalizedAttributes}|{normalizedModifiers}|{normalizedType}|{normalizedTail}";
    }

    private static int FindAttributeStartLine(IReadOnlyList<TextLine> lines, int fieldLineIndex)
    {
        var lineIndex = fieldLineIndex;

        while (lineIndex > 0 && IsAttributeOnlyLine(lines[lineIndex - 1].Text))
        {
            lineIndex--;
        }

        return lineIndex;
    }

    private static bool IsAttributeOnlyLine(string line)
    {
        var trimmedLine = TextUtils.StripLineComment(line).Trim();

        return trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']');
    }

    private static string GetLeadingAttributes(string line)
    {
        var match = LeadingAttributesPattern.Match(line);

        return match.Success ? match.Value : string.Empty;
    }
}
