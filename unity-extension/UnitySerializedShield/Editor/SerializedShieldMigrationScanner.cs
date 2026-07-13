using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlphaBoysLab.SerializedShield.Editor
{
    public static class SerializedShieldMigrationScanner
    {
        // .controller (AnimatorController → StateMachineBehaviour) and .playable
        // (Timeline) hold MonoBehaviour documents with real m_Script anchors and
        // serialized fields, so they must be verified (and migrated) too (audit N1).
        private static readonly string[] VerificationExtensions = { ".unity", ".prefab", ".asset", ".anim", ".preset", ".controller", ".playable" };

        public static List<SerializedShieldScriptInfo> FindScriptsWithFormerlySerializedAs()
        {
            return FindScriptsWithFormerlySerializedAs(null);
        }

        /// <summary>
        /// Finds scripts containing FormerlySerializedAs attributes. The optional
        /// <paramref name="progress"/> callback receives (0..1, info) and returns true to
        /// cancel the scan.
        /// </summary>
        public static List<SerializedShieldScriptInfo> FindScriptsWithFormerlySerializedAs(Func<float, string, bool> progress)
        {
            List<SerializedShieldScriptInfo> scripts = new List<SerializedShieldScriptInfo>();
            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript");

            for (int guidIndex = 0; guidIndex < scriptGuids.Length; guidIndex++)
            {
                string scriptGuid = scriptGuids[guidIndex];
                string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);

                if (string.IsNullOrEmpty(scriptPath)
                    || !scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (progress != null
                    && progress((float)guidIndex / Math.Max(scriptGuids.Length, 1), scriptPath))
                {
                    break;
                }

                try
                {
                    string absolutePath = SerializedShieldPathUtility.ToPhysicalPath(scriptPath);

                    if (absolutePath == null || !File.Exists(absolutePath))
                    {
                        continue;
                    }

                    string text = File.ReadAllText(absolutePath);
                    int attributeCount = SerializedShieldScriptAnalyzer.CountFormerlySerializedAsAttributes(text);

                    if (attributeCount == 0)
                    {
                        continue;
                    }

                    // Field-migration analysis runs only for scripts that actually contain
                    // the attribute (audit U-M8).
                    List<string> warnings = new List<string>();
                    List<SerializedShieldFieldMigration> fieldMigrations =
                        SerializedShieldScriptAnalyzer.FindFieldMigrations(text, warnings);

                    SerializedShieldScriptInfo info = new SerializedShieldScriptInfo
                    {
                        ScriptPath = scriptPath,
                        ScriptGuid = scriptGuid,
                        AttributeCount = attributeCount
                    };
                    info.FieldMigrations.AddRange(fieldMigrations);
                    info.FormerNames.AddRange(SerializedShieldScriptAnalyzer.ExtractFormerlySerializedAsNames(text));
                    info.Warnings.AddRange(warnings);

                    scripts.Add(info);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(string.Format(
                        "[SerializedShield] Could not scan script '{0}': {1}",
                        scriptPath,
                        exception.Message));
                }
            }

            return scripts.OrderBy(script => script.ScriptPath).ToList();
        }

        public static List<string> FindSerializedAssetsReferencingScript(string scriptGuid, SerializedShieldMigrationOptions options)
        {
            return FindSerializedAssetsReferencingScript(scriptGuid, options, null).TargetAssetPaths;
        }

        /// <summary>
        /// Finds serialized assets referencing the script GUID. Enumerates
        /// AssetDatabase.GetAllAssetPaths so embedded and local package assets are
        /// covered (audit U-H2) and hidden folders are excluded automatically. Unreadable
        /// files are reported instead of silently skipped (audit U-H7).
        /// </summary>
        public static SerializedShieldAssetScanResult FindSerializedAssetsReferencingScript(
            string scriptGuid,
            SerializedShieldMigrationOptions options,
            Func<float, string, bool> progress)
        {
            SerializedShieldAssetScanResult result = new SerializedShieldAssetScanResult();
            List<string> candidateAssetPaths = new List<string>();

            foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
            {
                string extension = Path.GetExtension(assetPath).ToLowerInvariant();

                if (ShouldScanExtension(extension, options))
                {
                    candidateAssetPaths.Add(assetPath);
                }
            }

            for (int candidateIndex = 0; candidateIndex < candidateAssetPaths.Count; candidateIndex++)
            {
                string assetPath = candidateAssetPaths[candidateIndex];

                if (progress != null
                    && progress((float)candidateIndex / Math.Max(candidateAssetPaths.Count, 1), assetPath))
                {
                    result.Cancelled = true;
                    return result;
                }

                string absolutePath = SerializedShieldPathUtility.ToPhysicalPath(assetPath);

                if (absolutePath == null || Directory.Exists(absolutePath))
                {
                    continue;
                }

                if (!File.Exists(absolutePath))
                {
                    result.UnreadableAssetPaths.Add(assetPath);
                    continue;
                }

                try
                {
                    if (FileContainsText(absolutePath, scriptGuid))
                    {
                        result.TargetAssetPaths.Add(assetPath);
                    }
                }
                catch (Exception)
                {
                    result.UnreadableAssetPaths.Add(assetPath);
                }
            }

            result.TargetAssetPaths.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// Enumerates every asset path relevant to post-migration verification:
        /// scenes, prefabs, .asset files, animation clips, and presets, regardless of the
        /// migration include options.
        /// </summary>
        public static List<string> GetVerificationAssetPaths()
        {
            List<string> assetPaths = new List<string>();

            foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
            {
                string extension = Path.GetExtension(assetPath).ToLowerInvariant();

                if (Array.IndexOf(VerificationExtensions, extension) >= 0)
                {
                    assetPaths.Add(assetPath);
                }
            }

            return assetPaths;
        }

        public static string RemoveFormerlySerializedAsAttributes(string text)
        {
            return SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(text);
        }

        public static int CountFormerlySerializedAsAttributes(string text)
        {
            return SerializedShieldScriptAnalyzer.CountFormerlySerializedAsAttributes(text);
        }

        public static List<SerializedShieldFieldMigration> FindFieldMigrations(string text)
        {
            return SerializedShieldScriptAnalyzer.FindFieldMigrations(text);
        }

        private static bool FileContainsText(string absolutePath, string value)
        {
            // GUIDs never span lines, so a streaming line scan avoids loading large
            // .asset files into memory during the scan (audit U-M6).
            foreach (string line in File.ReadLines(absolutePath))
            {
                if (line.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
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

            if (extension == ".asset" || extension == ".controller" || extension == ".playable")
            {
                // .controller / .playable carry script-instance documents (audit N1);
                // treat them like other non-scene, non-prefab serialized assets.
                return options.IncludeAssetFiles;
            }

            return false;
        }
    }
}
