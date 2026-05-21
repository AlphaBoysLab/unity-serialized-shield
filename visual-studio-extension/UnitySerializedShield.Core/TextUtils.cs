using System.Text.RegularExpressions;

namespace UnitySerializedShield.Core;

internal sealed record TextLine(string Text, string EndOfLine, int Offset);

internal static class TextUtils
{
    public static IReadOnlyList<TextLine> SplitLines(string text)
    {
        var lines = new List<TextLine>();
        var start = 0;
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];

            if (character != '\r' && character != '\n')
            {
                index++;
                continue;
            }

            var endOfLine = character == '\r' && index + 1 < text.Length && text[index + 1] == '\n'
                ? "\r\n"
                : character.ToString();

            lines.Add(new TextLine(text[start..index], endOfLine, start));
            index += endOfLine.Length;
            start = index;
        }

        if (start < text.Length)
        {
            lines.Add(new TextLine(text[start..], string.Empty, start));
        }

        return lines;
    }

    public static string StripLineComment(string line)
    {
        var inString = false;
        var inVerbatimString = false;
        var inChar = false;
        var escaped = false;

        for (var index = 0; index < line.Length - 1; index++)
        {
            var character = line[index];
            var nextCharacter = line[index + 1];

            if (inVerbatimString)
            {
                // Verbatim strings use "" to escape a quote; everything else is literal.
                if (character == '"')
                {
                    if (nextCharacter == '"')
                    {
                        index++; // skip the escaped quote
                    }
                    else
                    {
                        inVerbatimString = false;
                    }
                }

                continue;
            }

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && (inString || inChar))
            {
                escaped = true;
                continue;
            }

            if (character == '"' && !inChar)
            {
                inString = !inString;
                continue;
            }

            if (character == '\'' && !inString)
            {
                inChar = !inChar;
                continue;
            }

            // Detect verbatim string start: @" outside of strings and chars.
            if (!inString && !inChar && character == '@' && nextCharacter == '"')
            {
                inVerbatimString = true;
                index++; // skip the opening quote
                continue;
            }

            if (!inString && !inChar && character == '/' && nextCharacter == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    public static string DetectLineEnding(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    public static string NormalizeWhitespace(string text)
    {
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public static string EscapeCSharpString(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
