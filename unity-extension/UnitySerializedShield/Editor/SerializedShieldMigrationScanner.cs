using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AlphaBoysLab.SerializedShield.Editor
{
    public static class SerializedShieldMigrationScanner
    {
        private static readonly Regex FormerlySerializedAsRegex = new Regex(
            @"\[\s*(?:UnityEngine\.Serialization\.)?FormerlySerializedAs(?:Attribute)?\s*\(\s*""(?<name>(?:\\.|[^""\\])*)""\s*\)\s*\]",
            RegexOptions.Compiled);

        private static readonly Regex FormerlySerializedAsLineRegex = new Regex(
            @"(?m)^[ \t]*\[\s*(?:UnityEngine\.Serialization\.)?FormerlySerializedAs(?:Attribute)?\s*\(\s*""(?:\\.|[^""\\])*""\s*\)\s*\][ \t]*(?:\r\n|\n|\r)",
            RegexOptions.Compiled);

        private static readonly Regex FormerlySerializedAsInlineRegex = new Regex(
            @"[ \t]*\[\s*(?:UnityEngine\.Serialization\.)?FormerlySerializedAs(?:Attribute)?\s*\(\s*""(?:\\.|[^""\\])*""\s*\)\s*\][ \t]*",
            RegexOptions.Compiled);
        private static readonly Regex AttributeRegex = new Regex(
            @"\[\s*(?<name>(?:\w+\.)?\w+)(?:Attribute)?(?:\s*\((?<args>[^\]]*)\))?\s*\]",
            RegexOptions.Compiled);
        private static readonly Regex FieldNameRegex = new Regex(
            @"\b(?<name>@?[A-Za-z_]\w*)\s*(?:=[^;]*)?;",
            RegexOptions.Compiled);

        public static List<SerializedShieldScriptInfo> FindScriptsWithFormerlySerializedAs()
        {
            List<SerializedShieldScriptInfo> scripts = new List<SerializedShieldScriptInfo>();
            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript");

            foreach (string scriptGuid in scriptGuids)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);

                if (!scriptPath.EndsWith(".cs"))
                {
                    continue;
                }

                string absolutePath = SerializedShieldPathUtility.ToAbsolutePath(scriptPath);

                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                string text = File.ReadAllText(absolutePath);
                MatchCollection matches = FormerlySerializedAsRegex.Matches(text);
                List<SerializedShieldFieldMigration> fieldMigrations = FindFieldMigrations(text);

                if (matches.Count == 0)
                {
                    continue;
                }

                SerializedShieldScriptInfo info = new SerializedShieldScriptInfo
                {
                    ScriptPath = scriptPath,
                    ScriptGuid = scriptGuid,
                    AttributeCount = matches.Count
                };
                info.FieldMigrations.AddRange(fieldMigrations);

                foreach (Match match in matches)
                {
                    info.FormerNames.Add(UnescapeCSharpString(match.Groups["name"].Value));
                }

                scripts.Add(info);
            }

            return scripts.OrderBy(script => script.ScriptPath).ToList();
        }

        public static List<string> FindSerializedAssetsReferencingScript(string scriptGuid, SerializedShieldMigrationOptions options)
        {
            HashSet<string> targetAssetPaths = new HashSet<string>();

            foreach (string absolutePath in Directory.EnumerateFiles(Application.dataPath, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(absolutePath).ToLowerInvariant();

                if (!ShouldScanExtension(extension, options))
                {
                    continue;
                }

                string text;

                try
                {
                    text = File.ReadAllText(absolutePath);
                }
                catch (IOException)
                {
                    continue;
                }

                if (text.Contains(scriptGuid))
                {
                    targetAssetPaths.Add(SerializedShieldPathUtility.ToAssetPath(absolutePath));
                }
            }

            return targetAssetPaths.OrderBy(path => path).ToList();
        }

        public static string RemoveFormerlySerializedAsAttributes(string text)
        {
            string withoutAttributeLines = FormerlySerializedAsLineRegex.Replace(text, string.Empty);
            return FormerlySerializedAsInlineRegex.Replace(withoutAttributeLines, " ");
        }

        public static int CountFormerlySerializedAsAttributes(string text)
        {
            return FormerlySerializedAsRegex.Matches(text).Count;
        }

        public static List<SerializedShieldFieldMigration> FindFieldMigrations(string text)
        {
            List<SerializedShieldFieldMigration> migrations = new List<SerializedShieldFieldMigration>();
            List<string> pendingAttributeLines = new List<string>();

            foreach (string line in SplitLines(text))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("[") && !trimmed.Contains(";"))
                {
                    pendingAttributeLines.Add(line);
                    continue;
                }

                if (pendingAttributeLines.Count > 0 && line.Contains(";"))
                {
                    string attributesText = string.Join("\n", pendingAttributeLines);
                    List<string> formerNames = ExtractFormerlySerializedAsNames(attributesText);

                    if (formerNames.Count > 0)
                    {
                        string lineWithoutAttributes = AttributeRegex.Replace(line, string.Empty);
                        Match fieldMatch = FieldNameRegex.Match(lineWithoutAttributes);

                        if (fieldMatch.Success)
                        {
                            string currentName = NormalizeSerializedFieldName(fieldMatch.Groups["name"].Value);
                            SerializedShieldFieldMigration migration = new SerializedShieldFieldMigration
                            {
                                CurrentName = currentName
                            };
                            migration.FormerNames.AddRange(formerNames.Distinct());
                            migrations.Add(migration);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    pendingAttributeLines.Clear();
                }
            }

            return migrations;
        }

        private static bool ShouldScanExtension(string extension, SerializedShieldMigrationOptions options)
        {
            if (extension == ".prefab")
            {
                return options.IncludePrefabs;
            }

            if (extension == ".unity")
            {
                return options.IncludeScenes;
            }

            if (extension == ".asset")
            {
                return options.IncludeAssetFiles;
            }

            return false;
        }

        private static string UnescapeCSharpString(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static List<string> ExtractFormerlySerializedAsNames(string attributesText)
        {
            List<string> names = new List<string>();

            foreach (Match match in FormerlySerializedAsRegex.Matches(attributesText))
            {
                names.Add(UnescapeCSharpString(match.Groups["name"].Value));
            }

            return names;
        }

        private static string NormalizeSerializedFieldName(string name)
        {
            return name.StartsWith("@") ? name.Substring(1) : name;
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            using (StringReader reader = new StringReader(text))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    yield return line;
                }
            }
        }
    }
}
