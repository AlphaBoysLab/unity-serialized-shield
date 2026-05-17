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
    }
}
