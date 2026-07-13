using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AlphaBoysLab.SerializedShield.Editor
{
    [Serializable]
    public sealed class SerializedShieldYamlKeyRename
    {
        /// <summary>1-based line number within the whole file.</summary>
        public int LineNumber;
        public string OldKey;
        public string NewKey;
    }

    [Serializable]
    public sealed class SerializedShieldYamlRewriteResult
    {
        public string Text;
        public bool Changed;
        public List<SerializedShieldYamlKeyRename> Renames = new List<SerializedShieldYamlKeyRename>();
        public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public sealed class SerializedShieldYamlKeyReference
    {
        public string Key;
        /// <summary>1-based line number within the whole file.</summary>
        public int LineNumber;
        public string Description;
    }

    /// <summary>
    /// Structure-aware rewriter for Unity text-serialized YAML files.
    ///
    /// This class is deliberately pure (no UnityEditor/UnityEngine dependencies) so the
    /// riskiest logic in the package can be unit tested outside Unity.
    ///
    /// Safety rules (see audit U-C1 / U-C2 / U-M1):
    /// - A component block is only treated as an instance of a script when its own
    ///   top-level "m_Script:" entry references fileID 11500000 with the script's GUID.
    ///   Blocks that merely reference the script GUID elsewhere (for example a MonoScript
    ///   object field) are never rewritten.
    /// - Keys are only renamed at the TOP indentation level of the component mapping
    ///   (exactly two spaces). Members of nested [Serializable] classes that happen to
    ///   share the field name are never touched.
    /// - The list-element first-key form ("  - someKey:") is deliberately NOT renamed:
    ///   a component's own serialized fields are mapping entries, never list elements.
    ///   List elements always belong to a nested array field, and nested data is migrated
    ///   by Unity's own reserialization (FormerlySerializedAs works at any depth). The
    ///   post-migration verification pass keeps the attribute if any old-name key remains.
    /// </summary>
    public static class SerializedShieldYamlRewriter
    {
        // Unity document markers look like "--- !u!114 &1234567890" (optionally "stripped").
        private static readonly Regex DocumentMarkerRegex = new Regex(
            @"^--- !u!\d+ &-?\d+",
            RegexOptions.Compiled);

        private static readonly Regex PropertyPathLineRegex = new Regex(
            @"^\s*(?:-\s*)?propertyPath:\s*(?<path>.+?)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex AnimationAttributeLineRegex = new Regex(
            @"^\s*(?:-\s*)?attribute:\s*(?<attr>.+?)\s*$",
            RegexOptions.Compiled);

        public static bool ContainsGuid(string text, string guid)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(guid))
            {
                return false;
            }

            return text.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Renames top-level serialized field keys inside every component block that is an
        /// actual instance of the script identified by <paramref name="scriptGuid"/>.
        /// Returns the rewritten text plus a per-line record of every rename performed.
        /// </summary>
        public static SerializedShieldYamlRewriteResult RenameComponentKeys(
            string text,
            string scriptGuid,
            IList<SerializedShieldFieldMigration> fieldMigrations)
        {
            SerializedShieldYamlRewriteResult result = new SerializedShieldYamlRewriteResult
            {
                Text = text,
                Changed = false
            };

            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(scriptGuid)
                || fieldMigrations == null || fieldMigrations.Count == 0)
            {
                return result;
            }

            List<string> lines = SplitLinesKeepingEndings(text);
            Regex scriptAnchorRegex = BuildScriptAnchorRegex(scriptGuid);

            foreach (KeyValuePair<int, int> documentRange in GetDocumentRanges(lines))
            {
                int start = documentRange.Key;
                int end = documentRange.Value;

                if (!IsScriptInstanceDocument(lines, start, end, scriptAnchorRegex))
                {
                    continue;
                }

                foreach (SerializedShieldFieldMigration migration in fieldMigrations)
                {
                    if (migration == null || string.IsNullOrEmpty(migration.CurrentName))
                    {
                        continue;
                    }

                    List<int> newKeyLines = FindTopLevelKeyLines(lines, start, end, migration.CurrentName);

                    if (newKeyLines.Count > 0)
                    {
                        // The block already stores data under the new name. Renaming the old
                        // key would create a duplicate mapping key, so skip and report when
                        // an old key is also present.
                        foreach (string formerName in migration.FormerNames.Distinct())
                        {
                            if (IsRenamableFormerName(formerName, migration.CurrentName)
                                && FindTopLevelKeyLines(lines, start, end, formerName).Count > 0)
                            {
                                result.Warnings.Add(string.Format(
                                    "Line {0}: block already contains '{1}' but also still contains old key '{2}'; not renamed.",
                                    newKeyLines[0] + 1,
                                    migration.CurrentName,
                                    formerName));
                            }
                        }

                        continue;
                    }

                    foreach (string formerName in migration.FormerNames.Distinct())
                    {
                        if (!IsRenamableFormerName(formerName, migration.CurrentName))
                        {
                            continue;
                        }

                        List<int> oldKeyLines = FindTopLevelKeyLines(lines, start, end, formerName);

                        if (oldKeyLines.Count == 0)
                        {
                            continue;
                        }

                        foreach (int lineIndex in oldKeyLines)
                        {
                            lines[lineIndex] = ReplaceTopLevelKey(lines[lineIndex], migration.CurrentName);
                            result.Renames.Add(new SerializedShieldYamlKeyRename
                            {
                                LineNumber = lineIndex + 1,
                                OldKey = formerName,
                                NewKey = migration.CurrentName
                            });
                        }

                        result.Changed = true;
                        break;
                    }
                }
            }

            if (result.Changed)
            {
                StringBuilder builder = new StringBuilder(text.Length);

                foreach (string line in lines)
                {
                    builder.Append(line);
                }

                result.Text = builder.ToString();
            }

            return result;
        }

        /// <summary>
        /// Returns every occurrence of <paramref name="keys"/> as a mapping key (at ANY
        /// indentation, including the "- key:" list-element form) inside blocks that are
        /// instances of the script. Used by the post-migration verification pass; any hit
        /// blocks attribute removal. This is intentionally conservative: a nested field
        /// that legitimately shares an old name keeps the attribute in place.
        /// </summary>
        public static List<SerializedShieldYamlKeyReference> FindKeysInScriptBlocks(
            string text,
            string scriptGuid,
            ICollection<string> keys)
        {
            List<SerializedShieldYamlKeyReference> references = new List<SerializedShieldYamlKeyReference>();

            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(scriptGuid) || keys == null || keys.Count == 0)
            {
                return references;
            }

            if (!ContainsGuid(text, scriptGuid))
            {
                return references;
            }

            List<string> lines = SplitLinesKeepingEndings(text);
            Regex scriptAnchorRegex = BuildScriptAnchorRegex(scriptGuid);

            foreach (KeyValuePair<int, int> documentRange in GetDocumentRanges(lines))
            {
                int start = documentRange.Key;
                int end = documentRange.Value;

                if (!IsScriptInstanceDocument(lines, start, end, scriptAnchorRegex))
                {
                    continue;
                }

                for (int lineIndex = start; lineIndex < end; lineIndex++)
                {
                    string key;

                    if (!TryGetAnyDepthKey(TrimLineEnding(lines[lineIndex]), out key))
                    {
                        continue;
                    }

                    if (keys.Contains(key))
                    {
                        references.Add(new SerializedShieldYamlKeyReference
                        {
                            Key = key,
                            LineNumber = lineIndex + 1,
                            Description = string.Format("serialized key '{0}' at line {1}", key, lineIndex + 1)
                        });
                    }
                }
            }

            return references;
        }

        /// <summary>
        /// Detects prefab-instance override entries (and preset properties) whose
        /// propertyPath root matches one of <paramref name="names"/>. These live in
        /// "PrefabInstance.m_Modifications" as "propertyPath: oldFieldName[...]" and
        /// reference the prefab GUID rather than the script GUID, so they cannot be
        /// migrated by this tool; detection blocks attribute removal (audit U-C3).
        /// </summary>
        public static List<SerializedShieldYamlKeyReference> FindPropertyPathReferences(
            string text,
            ICollection<string> names)
        {
            List<SerializedShieldYamlKeyReference> references = new List<SerializedShieldYamlKeyReference>();

            if (string.IsNullOrEmpty(text) || names == null || names.Count == 0)
            {
                return references;
            }

            List<string> lines = SplitLinesKeepingEndings(text);

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                Match match = PropertyPathLineRegex.Match(TrimLineEnding(lines[lineIndex]));

                if (!match.Success)
                {
                    continue;
                }

                string path = match.Groups["path"].Value;
                string root = GetPathRoot(path);

                if (names.Contains(root))
                {
                    references.Add(new SerializedShieldYamlKeyReference
                    {
                        Key = root,
                        LineNumber = lineIndex + 1,
                        Description = string.Format("propertyPath '{0}' at line {1}", path, lineIndex + 1)
                    });
                }
            }

            return references;
        }

        /// <summary>
        /// Detects animation curve bindings ("attribute: fieldName") that reference one of
        /// <paramref name="names"/> in a file that also references the script GUID.
        /// Animation bindings are not rewritten by this tool; detection blocks attribute
        /// removal (audit U-H9).
        /// </summary>
        public static List<SerializedShieldYamlKeyReference> FindAnimationBindingReferences(
            string text,
            string scriptGuid,
            ICollection<string> names)
        {
            List<SerializedShieldYamlKeyReference> references = new List<SerializedShieldYamlKeyReference>();

            if (string.IsNullOrEmpty(text) || names == null || names.Count == 0 || !ContainsGuid(text, scriptGuid))
            {
                return references;
            }

            List<string> lines = SplitLinesKeepingEndings(text);

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                Match match = AnimationAttributeLineRegex.Match(TrimLineEnding(lines[lineIndex]));

                if (!match.Success)
                {
                    continue;
                }

                string attribute = match.Groups["attr"].Value;
                string root = GetPathRoot(attribute);

                if (names.Contains(root))
                {
                    references.Add(new SerializedShieldYamlKeyReference
                    {
                        Key = root,
                        LineNumber = lineIndex + 1,
                        Description = string.Format("animation binding 'attribute: {0}' at line {1}", attribute, lineIndex + 1)
                    });
                }
            }

            return references;
        }

        /// <summary>
        /// Splits text into lines, each keeping its original line ending. Concatenating
        /// the returned lines always reproduces the input exactly.
        /// </summary>
        public static List<string> SplitLinesKeepingEndings(string text)
        {
            List<string> lines = new List<string>();

            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            int start = 0;
            int index = 0;

            while (index < text.Length)
            {
                char current = text[index];

                if (current == '\r')
                {
                    int end = (index + 1 < text.Length && text[index + 1] == '\n') ? index + 2 : index + 1;
                    lines.Add(text.Substring(start, end - start));
                    start = end;
                    index = end;
                }
                else if (current == '\n')
                {
                    lines.Add(text.Substring(start, index + 1 - start));
                    start = index + 1;
                    index++;
                }
                else
                {
                    index++;
                }
            }

            if (start < text.Length)
            {
                lines.Add(text.Substring(start));
            }

            return lines;
        }

        private static bool IsRenamableFormerName(string formerName, string currentName)
        {
            return !string.IsNullOrEmpty(formerName)
                && !string.Equals(formerName, currentName, StringComparison.Ordinal);
        }

        private static Regex BuildScriptAnchorRegex(string scriptGuid)
        {
            return new Regex(
                @"^  m_Script:\s*\{\s*fileID:\s*11500000\s*,\s*guid:\s*"
                + Regex.Escape(scriptGuid)
                + @"\s*[,}]",
                RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Returns [start, end) line ranges for every YAML document in the file. Lines
        /// before the first "--- !u!" marker (the %YAML/%TAG header) form a pseudo-range
        /// that can never match a script anchor.
        /// </summary>
        private static List<KeyValuePair<int, int>> GetDocumentRanges(List<string> lines)
        {
            List<KeyValuePair<int, int>> ranges = new List<KeyValuePair<int, int>>();
            int currentStart = 0;

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                if (DocumentMarkerRegex.IsMatch(TrimLineEnding(lines[lineIndex])))
                {
                    if (lineIndex > currentStart)
                    {
                        ranges.Add(new KeyValuePair<int, int>(currentStart, lineIndex));
                    }

                    currentStart = lineIndex;
                }
            }

            if (lines.Count > currentStart)
            {
                ranges.Add(new KeyValuePair<int, int>(currentStart, lines.Count));
            }

            return ranges;
        }

        private static bool IsScriptInstanceDocument(List<string> lines, int start, int end, Regex scriptAnchorRegex)
        {
            for (int lineIndex = start; lineIndex < end; lineIndex++)
            {
                if (scriptAnchorRegex.IsMatch(TrimLineEnding(lines[lineIndex])))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<int> FindTopLevelKeyLines(List<string> lines, int start, int end, string key)
        {
            List<int> matches = new List<int>();

            for (int lineIndex = start; lineIndex < end; lineIndex++)
            {
                string topLevelKey;

                if (TryGetTopLevelKey(TrimLineEnding(lines[lineIndex]), out topLevelKey)
                    && string.Equals(topLevelKey, key, StringComparison.Ordinal))
                {
                    matches.Add(lineIndex);
                }
            }

            return matches;
        }

        /// <summary>
        /// A top-level component key line is exactly two spaces of indentation followed by
        /// the key and a colon. Deeper indentation, document markers, class headers, and
        /// list-element lines ("  - key:") deliberately do not match.
        /// </summary>
        private static bool TryGetTopLevelKey(string line, out string key)
        {
            key = null;

            if (line.Length < 4 || line[0] != ' ' || line[1] != ' ')
            {
                return false;
            }

            char firstContentChar = line[2];

            if (firstContentChar == ' ' || firstContentChar == '-' || firstContentChar == ':')
            {
                return false;
            }

            int colonIndex = line.IndexOf(':');

            if (colonIndex <= 2)
            {
                return false;
            }

            string candidate = line.Substring(2, colonIndex - 2);

            // Serialized field keys never contain spaces or YAML flow characters.
            if (candidate.IndexOfAny(new[] { ' ', '\t', '{', '}', '[', ']', ',', '\'', '"' }) >= 0)
            {
                return false;
            }

            key = candidate;
            return true;
        }

        /// <summary>
        /// Matches a mapping key at any depth, including the list-element first-key form
        /// ("    - key: value"). Used only for verification, never for rewriting.
        /// </summary>
        private static bool TryGetAnyDepthKey(string line, out string key)
        {
            key = null;

            int index = 0;

            while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
            {
                index++;
            }

            if (index == 0 || index >= line.Length)
            {
                // Column-0 lines are class headers / document markers, not field keys.
                return false;
            }

            if (line[index] == '-')
            {
                index++;

                if (index >= line.Length || line[index] != ' ')
                {
                    return false;
                }

                while (index < line.Length && line[index] == ' ')
                {
                    index++;
                }
            }

            int keyStart = index;
            int colonIndex = line.IndexOf(':', keyStart);

            if (colonIndex <= keyStart)
            {
                return false;
            }

            string candidate = line.Substring(keyStart, colonIndex - keyStart);

            if (candidate.Length == 0
                || candidate.IndexOfAny(new[] { ' ', '\t', '{', '}', '[', ']', ',', '\'', '"' }) >= 0)
            {
                return false;
            }

            key = candidate;
            return true;
        }

        private static string ReplaceTopLevelKey(string line, string newKey)
        {
            int colonIndex = line.IndexOf(':');
            return "  " + newKey + line.Substring(colonIndex);
        }

        private static string GetPathRoot(string path)
        {
            int cutIndex = path.Length;

            for (int index = 0; index < path.Length; index++)
            {
                char current = path[index];

                if (current == '.' || current == '[')
                {
                    cutIndex = index;
                    break;
                }
            }

            return path.Substring(0, cutIndex);
        }

        private static string TrimLineEnding(string line)
        {
            return line.TrimEnd('\r', '\n');
        }
    }
}
