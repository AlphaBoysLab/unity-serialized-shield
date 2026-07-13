using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AlphaBoysLab.SerializedShield.Editor
{
    /// <summary>
    /// Pure C# source-text analyzer for [FormerlySerializedAs] attributes.
    ///
    /// This class is deliberately free of UnityEditor/UnityEngine dependencies so it can
    /// be unit tested outside Unity.
    ///
    /// Heuristics (documented limitations):
    /// - Comment and string awareness is a character-level state machine that understands
    ///   line comments, block comments, regular strings, verbatim strings, interpolated
    ///   strings (treated entirely as string content), and char literals. Attributes that
    ///   only appear inside comments or strings are never counted, never treated as
    ///   migrations, and never removed (audit U-M4).
    /// - "#if" preprocessor branches are NOT evaluated; an attribute in an inactive branch
    ///   is treated like ordinary code. This is a known, documented limitation.
    /// - Multi-declarator fields ("[FormerlySerializedAs] int a, b;") are skipped with a
    ///   warning because the old name cannot be mapped to a single declarator safely
    ///   (audit U-M5). The attribute is left in place and the verification pass keeps it.
    /// </summary>
    public static class SerializedShieldScriptAnalyzer
    {
        private const byte RegionCode = 0;
        private const byte RegionComment = 1;
        private const byte RegionString = 2;

        // Matches one FormerlySerializedAs attribute ELEMENT (not the surrounding
        // brackets), so combined attribute lists like
        // "[SerializeField, FormerlySerializedAs("old")]" are recognized (audit U-M3).
        // Callers additionally verify that the preceding non-whitespace character is
        // '[' or ',' in code, so identifiers such as "MyFormerlySerializedAs(...)" or
        // method calls never match.
        private static readonly Regex FormerlySerializedAsElementRegex = new Regex(
            @"(?:global::\s*)?(?:UnityEngine\s*\.\s*Serialization\s*\.\s*)?FormerlySerializedAs(?:Attribute)?\s*\(\s*(?:@""(?<verbatimName>(?:""""|[^""])*)""|""(?<name>(?:\\.|[^""\\])*)"")\s*\)",
            RegexOptions.Compiled);

        // First character supports non-ASCII identifiers (audit U-M2); \w already covers
        // Unicode letters/digits/connector characters in .NET for the remainder.
        private static readonly Regex FieldNameRegex = new Regex(
            @"\b(?<name>@?[\p{L}\p{Nl}_]\w*)\s*(?:=[^;]*)?;",
            RegexOptions.Compiled);

        private sealed class AttributeOccurrence
        {
            public int Index;
            public int Length;
            public int PrefixIndex;
            public string Name;
        }

        public static int CountFormerlySerializedAsAttributes(string text)
        {
            return FindAttributeOccurrences(text).Count;
        }

        public static List<string> ExtractFormerlySerializedAsNames(string text)
        {
            return FindAttributeOccurrences(text).Select(occurrence => occurrence.Name).ToList();
        }

        public static string RemoveFormerlySerializedAsAttributes(string text)
        {
            int removedCount;
            return RemoveFormerlySerializedAsAttributes(text, null, out removedCount);
        }

        /// <summary>
        /// Removes FormerlySerializedAs attributes whose old name is in
        /// <paramref name="namesToRemove"/> (or all of them when it is null). Handles the
        /// standalone attribute-line form, the inline form, and elements inside combined
        /// attribute lists. Occurrences inside comments or strings are never touched.
        /// </summary>
        public static string RemoveFormerlySerializedAsAttributes(
            string text,
            ICollection<string> namesToRemove,
            out int removedCount)
        {
            removedCount = 0;

            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            List<AttributeOccurrence> occurrences = FindAttributeOccurrences(text);
            string current = text;

            // Process from the end of the file so earlier occurrence indices stay valid;
            // every edit for an occurrence happens at indices >= that occurrence's span.
            for (int occurrenceIndex = occurrences.Count - 1; occurrenceIndex >= 0; occurrenceIndex--)
            {
                AttributeOccurrence occurrence = occurrences[occurrenceIndex];

                if (namesToRemove != null && !namesToRemove.Contains(occurrence.Name))
                {
                    continue;
                }

                current = RemoveOccurrence(current, occurrence);
                removedCount++;
            }

            return current;
        }

        /// <summary>
        /// Finds field migrations: the current field name plus the old names declared by
        /// FormerlySerializedAs attributes attached to that field. Handles attributes on
        /// preceding lines, inline attributes on the declaration line (audit U-H4),
        /// combined attribute lists (U-M3), and comment lines between the attribute and
        /// the field (U-H5). Multi-declarator fields are skipped with a warning (U-M5).
        /// </summary>
        public static List<SerializedShieldFieldMigration> FindFieldMigrations(string text)
        {
            return FindFieldMigrations(text, new List<string>());
        }

        public static List<SerializedShieldFieldMigration> FindFieldMigrations(string text, List<string> warnings)
        {
            List<SerializedShieldFieldMigration> migrations = new List<SerializedShieldFieldMigration>();

            if (string.IsNullOrEmpty(text))
            {
                return migrations;
            }

            byte[] regions = ClassifyRegions(text);
            List<AttributeOccurrence> occurrences = FindAttributeOccurrences(text, regions);
            List<KeyValuePair<int, int>> lineSpans = GetLineSpans(text);
            List<string> pendingNames = new List<string>();

            for (int lineIndex = 0; lineIndex < lineSpans.Count; lineIndex++)
            {
                int lineStart = lineSpans[lineIndex].Key;
                int lineLength = lineSpans[lineIndex].Value;
                int lineEnd = lineStart + lineLength;

                List<string> lineNames = occurrences
                    .Where(occurrence => occurrence.Index >= lineStart && occurrence.Index < lineEnd)
                    .Select(occurrence => occurrence.Name)
                    .ToList();

                int firstCodeIndex = -1;
                bool hasCodeSemicolon = false;
                bool hasAnyCode = false;

                for (int charIndex = lineStart; charIndex < lineEnd; charIndex++)
                {
                    char current = text[charIndex];

                    if (char.IsWhiteSpace(current))
                    {
                        continue;
                    }

                    if (regions[charIndex] == RegionCode)
                    {
                        hasAnyCode = true;

                        if (firstCodeIndex < 0)
                        {
                            firstCodeIndex = charIndex;
                        }

                        if (current == ';')
                        {
                            hasCodeSemicolon = true;
                        }
                    }
                    else if (regions[charIndex] == RegionString && firstCodeIndex < 0)
                    {
                        // Continuation of a multi-line string/attribute argument; treat
                        // like an ignorable line so pending attributes survive.
                        firstCodeIndex = -2;
                    }
                }

                // Blank lines and lines that are entirely comment (or string continuation)
                // do NOT break the attribute-to-field association (audit U-H5).
                if (!hasAnyCode)
                {
                    continue;
                }

                bool startsWithBracket = firstCodeIndex >= 0 && text[firstCodeIndex] == '[';

                if (startsWithBracket && !hasCodeSemicolon)
                {
                    pendingNames.AddRange(lineNames);
                    continue;
                }

                if (hasCodeSemicolon)
                {
                    List<string> combinedNames = new List<string>(pendingNames);
                    combinedNames.AddRange(lineNames);
                    pendingNames.Clear();

                    if (combinedNames.Count == 0)
                    {
                        continue;
                    }

                    string codeOnlyLine = ExtractCodeOnly(text, regions, lineStart, lineLength);
                    string declaration = StripLeadingAttributeGroups(codeOnlyLine);

                    if (HasTopLevelComma(declaration))
                    {
                        if (warnings != null)
                        {
                            warnings.Add(string.Format(
                                "Line {0}: multi-declarator field skipped; FormerlySerializedAs old names cannot be mapped to a single field safely.",
                                lineIndex + 1));
                        }

                        continue;
                    }

                    Match fieldMatch = FieldNameRegex.Match(declaration);

                    if (!fieldMatch.Success)
                    {
                        if (warnings != null)
                        {
                            warnings.Add(string.Format(
                                "Line {0}: could not resolve the field name next to a FormerlySerializedAs attribute.",
                                lineIndex + 1));
                        }

                        continue;
                    }

                    SerializedShieldFieldMigration migration = new SerializedShieldFieldMigration
                    {
                        CurrentName = NormalizeIdentifier(fieldMatch.Groups["name"].Value)
                    };
                    migration.FormerNames.AddRange(combinedNames.Distinct());
                    migrations.Add(migration);
                    continue;
                }

                // A code line that is neither an attribute line nor a declaration breaks
                // the association.
                pendingNames.Clear();
            }

            return migrations;
        }

        private static List<AttributeOccurrence> FindAttributeOccurrences(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new List<AttributeOccurrence>();
            }

            return FindAttributeOccurrences(text, ClassifyRegions(text));
        }

        private static List<AttributeOccurrence> FindAttributeOccurrences(string text, byte[] regions)
        {
            List<AttributeOccurrence> occurrences = new List<AttributeOccurrence>();

            foreach (Match match in FormerlySerializedAsElementRegex.Matches(text))
            {
                if (regions[match.Index] != RegionCode)
                {
                    continue;
                }

                int prefixIndex = match.Index - 1;

                while (prefixIndex >= 0 && char.IsWhiteSpace(text[prefixIndex]))
                {
                    prefixIndex--;
                }

                if (prefixIndex < 0)
                {
                    continue;
                }

                char prefixChar = text[prefixIndex];

                if ((prefixChar != '[' && prefixChar != ',') || regions[prefixIndex] != RegionCode)
                {
                    continue;
                }

                string name = match.Groups["verbatimName"].Success
                    ? match.Groups["verbatimName"].Value.Replace("\"\"", "\"")
                    : UnescapeCSharpString(match.Groups["name"].Value);

                occurrences.Add(new AttributeOccurrence
                {
                    Index = match.Index,
                    Length = match.Length,
                    PrefixIndex = prefixIndex,
                    Name = name
                });
            }

            return occurrences;
        }

        private static string RemoveOccurrence(string current, AttributeOccurrence occurrence)
        {
            int elementStart = occurrence.Index;
            int elementEnd = occurrence.Index + occurrence.Length;
            int prefixIndex = occurrence.PrefixIndex;
            char prefixChar = current[prefixIndex];

            int suffixIndex = elementEnd;

            while (suffixIndex < current.Length && char.IsWhiteSpace(current[suffixIndex]))
            {
                suffixIndex++;
            }

            char suffixChar = suffixIndex < current.Length ? current[suffixIndex] : '\0';

            if (prefixChar == '[' && suffixChar == ']')
            {
                // Sole element: remove the whole bracket group; if the group is alone on
                // its line, remove the line including its line ending.
                int groupStart = prefixIndex;
                int groupEnd = suffixIndex + 1;
                int lineStart = current.LastIndexOf('\n', Math.Max(groupStart - 1, 0));
                lineStart = lineStart < 0 ? 0 : lineStart + 1;

                int lineBreak = current.IndexOf('\n', groupEnd);
                int lineContentEnd = lineBreak < 0 ? current.Length : lineBreak;

                bool aloneOnLine = IsWhitespaceRange(current, lineStart, groupStart)
                    && IsWhitespaceRange(current, groupEnd, lineContentEnd);

                if (aloneOnLine)
                {
                    int removalEnd = lineBreak < 0 ? current.Length : lineBreak + 1;
                    return current.Remove(lineStart, removalEnd - lineStart);
                }

                int inlineEnd = groupEnd;

                while (inlineEnd < current.Length && (current[inlineEnd] == ' ' || current[inlineEnd] == '\t'))
                {
                    inlineEnd++;
                }

                return current.Remove(groupStart, inlineEnd - groupStart);
            }

            if (suffixChar == ',')
            {
                // Element followed by a sibling: remove element, the comma, and spacing.
                int removalEnd = suffixIndex + 1;

                while (removalEnd < current.Length && (current[removalEnd] == ' ' || current[removalEnd] == '\t'))
                {
                    removalEnd++;
                }

                return current.Remove(elementStart, removalEnd - elementStart);
            }

            if (prefixChar == ',')
            {
                // Last element of a list: remove the preceding comma and the element.
                return current.Remove(prefixIndex, elementEnd - prefixIndex);
            }

            return current.Remove(elementStart, elementEnd - elementStart);
        }

        /// <summary>
        /// Classifies every character of the text as code, comment, or string content.
        /// </summary>
        private static byte[] ClassifyRegions(string text)
        {
            byte[] regions = new byte[text.Length];
            int index = 0;
            int length = text.Length;

            while (index < length)
            {
                char current = text[index];

                if (current == '/' && index + 1 < length && text[index + 1] == '/')
                {
                    while (index < length && text[index] != '\n' && text[index] != '\r')
                    {
                        regions[index] = RegionComment;
                        index++;
                    }

                    continue;
                }

                if (current == '/' && index + 1 < length && text[index + 1] == '*')
                {
                    regions[index] = RegionComment;
                    regions[index + 1] = RegionComment;
                    index += 2;

                    while (index < length)
                    {
                        regions[index] = RegionComment;

                        if (text[index] == '/' && text[index - 1] == '*')
                        {
                            index++;
                            break;
                        }

                        index++;
                    }

                    continue;
                }

                bool isVerbatim = false;
                bool isString = false;
                int prefixLength = 0;

                if (current == '"')
                {
                    isString = true;
                    prefixLength = 1;
                }
                else if ((current == '@' || current == '$') && index + 1 < length && text[index + 1] == '"')
                {
                    isString = true;
                    isVerbatim = current == '@';
                    prefixLength = 2;
                }
                else if ((current == '@' || current == '$') && index + 2 < length
                    && (text[index + 1] == '@' || text[index + 1] == '$') && text[index + 2] == '"')
                {
                    isString = true;
                    isVerbatim = true;
                    prefixLength = 3;
                }

                if (isString)
                {
                    for (int prefixIndex = 0; prefixIndex < prefixLength; prefixIndex++)
                    {
                        regions[index + prefixIndex] = RegionString;
                    }

                    index += prefixLength;

                    while (index < length)
                    {
                        char stringChar = text[index];
                        regions[index] = RegionString;

                        if (isVerbatim)
                        {
                            if (stringChar == '"')
                            {
                                if (index + 1 < length && text[index + 1] == '"')
                                {
                                    regions[index + 1] = RegionString;
                                    index += 2;
                                    continue;
                                }

                                index++;
                                break;
                            }

                            index++;
                        }
                        else
                        {
                            if (stringChar == '\\' && index + 1 < length)
                            {
                                regions[index + 1] = RegionString;
                                index += 2;
                                continue;
                            }

                            if (stringChar == '"')
                            {
                                index++;
                                break;
                            }

                            if (stringChar == '\n' || stringChar == '\r')
                            {
                                // Unterminated string literal; stop at the line break.
                                regions[index] = RegionCode;
                                break;
                            }

                            index++;
                        }
                    }

                    continue;
                }

                if (current == '\'')
                {
                    regions[index] = RegionString;
                    index++;

                    while (index < length)
                    {
                        char literalChar = text[index];
                        regions[index] = RegionString;

                        if (literalChar == '\\' && index + 1 < length)
                        {
                            regions[index + 1] = RegionString;
                            index += 2;
                            continue;
                        }

                        index++;

                        if (literalChar == '\'' || literalChar == '\n' || literalChar == '\r')
                        {
                            break;
                        }
                    }

                    continue;
                }

                regions[index] = RegionCode;
                index++;
            }

            return regions;
        }

        private static List<KeyValuePair<int, int>> GetLineSpans(string text)
        {
            List<KeyValuePair<int, int>> spans = new List<KeyValuePair<int, int>>();
            int start = 0;

            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    spans.Add(new KeyValuePair<int, int>(start, index + 1 - start));
                    start = index + 1;
                }
            }

            if (start < text.Length)
            {
                spans.Add(new KeyValuePair<int, int>(start, text.Length - start));
            }

            return spans;
        }

        private static string ExtractCodeOnly(string text, byte[] regions, int lineStart, int lineLength)
        {
            char[] buffer = new char[lineLength];

            for (int offset = 0; offset < lineLength; offset++)
            {
                int charIndex = lineStart + offset;
                buffer[offset] = regions[charIndex] == RegionCode ? text[charIndex] : ' ';
            }

            return new string(buffer);
        }

        private static string StripLeadingAttributeGroups(string codeOnlyLine)
        {
            int index = 0;

            while (true)
            {
                while (index < codeOnlyLine.Length && char.IsWhiteSpace(codeOnlyLine[index]))
                {
                    index++;
                }

                if (index >= codeOnlyLine.Length || codeOnlyLine[index] != '[')
                {
                    break;
                }

                int depth = 0;
                int scanIndex = index;
                int closeIndex = -1;

                while (scanIndex < codeOnlyLine.Length)
                {
                    char current = codeOnlyLine[scanIndex];

                    if (current == '[')
                    {
                        depth++;
                    }
                    else if (current == ']')
                    {
                        depth--;

                        if (depth == 0)
                        {
                            closeIndex = scanIndex;
                            break;
                        }
                    }

                    scanIndex++;
                }

                if (closeIndex < 0)
                {
                    break;
                }

                index = closeIndex + 1;
            }

            return codeOnlyLine.Substring(Math.Min(index, codeOnlyLine.Length));
        }

        /// <summary>
        /// Detects a comma at declarator level (outside any parentheses, brackets, braces,
        /// or generic angle brackets) before the terminating semicolon. Angle-bracket
        /// counting is a heuristic that is correct for field declarations such as
        /// "Dictionary&lt;string, int&gt; map;".
        /// </summary>
        private static bool HasTopLevelComma(string declaration)
        {
            int depth = 0;

            for (int index = 0; index < declaration.Length; index++)
            {
                char current = declaration[index];

                if (current == '(' || current == '[' || current == '{' || current == '<')
                {
                    depth++;
                }
                else if (current == ')' || current == ']' || current == '}' || current == '>')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                }
                else if (current == ';')
                {
                    break;
                }
                else if (current == ',' && depth == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWhitespaceRange(string text, int startInclusive, int endExclusive)
        {
            for (int index = startInclusive; index < endExclusive; index++)
            {
                if (!char.IsWhiteSpace(text[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeIdentifier(string name)
        {
            return name.StartsWith("@", StringComparison.Ordinal) ? name.Substring(1) : name;
        }

        private static string UnescapeCSharpString(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
