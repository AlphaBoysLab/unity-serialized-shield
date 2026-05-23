using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace AlphaBoysLab.SerializedShield.Editor
{
    public static class SerializedShieldMigrationProcessor
    {
        public static List<string> PreviewTargets(SerializedShieldScriptInfo script, SerializedShieldMigrationOptions options)
        {
            return SerializedShieldMigrationScanner.FindSerializedAssetsReferencingScript(script.ScriptGuid, options);
        }

        public static SerializedShieldMigrationResult MigrateScript(SerializedShieldScriptInfo script, SerializedShieldMigrationOptions options)
        {
            List<string> targetAssetPaths = PreviewTargets(script, options);
            HashSet<string> filesToBackup = new HashSet<string>(targetAssetPaths);
            filesToBackup.Add(script.ScriptPath);

            SerializedShieldMigrationResult result = new SerializedShieldMigrationResult
            {
                ScriptPath = script.ScriptPath
            };
            result.TargetAssetPaths.AddRange(targetAssetPaths);

            if (options.CreateBackup)
            {
                SerializedShieldBackupSession backupSession = SerializedShieldMigrationBackup.CreateSession(filesToBackup);
                result.BackupSessionPath = backupSession.SessionFilePath;
            }

            if (targetAssetPaths.Any(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return result;
                }
            }

            if (targetAssetPaths.Count > 0)
            {
                ApplyTextFieldMigration(script, targetAssetPaths, result);
                if (result.TextMigratedAssetCount > 0)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }

                AssetDatabase.ForceReserializeAssets(targetAssetPaths);
                result.ReserializedAssetCount = targetAssetPaths.Count;
            }

            if (options.RemoveAttributesAfterMigration)
            {
                result.RemovedAttributeCount = RemoveFormerlySerializedAsFromScript(script.ScriptPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return result;
        }

        private static void ApplyTextFieldMigration(
            SerializedShieldScriptInfo script,
            List<string> targetAssetPaths,
            SerializedShieldMigrationResult result)
        {
            if (script.FieldMigrations == null || script.FieldMigrations.Count == 0)
            {
                return;
            }

            foreach (string assetPath in targetAssetPaths)
            {
                string absolutePath = SerializedShieldPathUtility.ToAbsolutePath(assetPath);

                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                string originalText;

                try
                {
                    originalText = File.ReadAllText(absolutePath);
                }
                catch (IOException)
                {
                    continue;
                }

                int migratedFieldCount;
                string migratedText = MigrateSerializedText(
                    originalText,
                    script.ScriptGuid,
                    script.FieldMigrations,
                    out migratedFieldCount);

                if (migratedFieldCount == 0)
                {
                    continue;
                }

                File.WriteAllText(absolutePath, migratedText);
                result.TextMigratedAssetCount++;
                result.TextMigratedFieldCount += migratedFieldCount;
            }
        }

        private static string MigrateSerializedText(
            string text,
            string scriptGuid,
            List<SerializedShieldFieldMigration> fieldMigrations,
            out int migratedFieldCount)
        {
            migratedFieldCount = 0;
            StringBuilder builder = new StringBuilder(text.Length);
            List<string> blockLines = new List<string>();

            foreach (string line in SplitLinesKeepingEndings(text))
            {
                if (line.StartsWith("--- ", StringComparison.Ordinal) && blockLines.Count > 0)
                {
                    AppendMigratedBlock(builder, blockLines, scriptGuid, fieldMigrations, ref migratedFieldCount);
                    blockLines.Clear();
                }

                blockLines.Add(line);
            }

            if (blockLines.Count > 0)
            {
                AppendMigratedBlock(builder, blockLines, scriptGuid, fieldMigrations, ref migratedFieldCount);
            }

            return builder.ToString();
        }

        private static void AppendMigratedBlock(
            StringBuilder builder,
            List<string> blockLines,
            string scriptGuid,
            List<SerializedShieldFieldMigration> fieldMigrations,
            ref int migratedFieldCount)
        {
            string blockText = string.Concat(blockLines);

            if (!blockText.Contains("guid: " + scriptGuid))
            {
                builder.Append(blockText);
                return;
            }

            foreach (SerializedShieldFieldMigration migration in fieldMigrations)
            {
                if (string.IsNullOrEmpty(migration.CurrentName)
                    || HasSerializedKey(blockText, migration.CurrentName))
                {
                    continue;
                }

                foreach (string formerName in migration.FormerNames.Distinct())
                {
                    if (string.IsNullOrEmpty(formerName) || formerName == migration.CurrentName)
                    {
                        continue;
                    }

                    string migratedBlockText = ReplaceSerializedKey(blockText, formerName, migration.CurrentName);

                    if (migratedBlockText != blockText)
                    {
                        blockText = migratedBlockText;
                        migratedFieldCount++;
                        break;
                    }
                }
            }

            builder.Append(blockText);
        }

        private static bool HasSerializedKey(string text, string key)
        {
            return Regex.IsMatch(text, @"(?m)^[ \t]*" + Regex.Escape(key) + @"\s*:");
        }

        private static string ReplaceSerializedKey(string text, string oldKey, string newKey)
        {
            return Regex.Replace(
                text,
                @"(?m)^(?<indent>[ \t]*)" + Regex.Escape(oldKey) + @"(?<suffix>\s*:)",
                "${indent}" + newKey + "${suffix}",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
        }

        private static IEnumerable<string> SplitLinesKeepingEndings(string text)
        {
            MatchCollection matches = Regex.Matches(text, @".*(?:\r\n|\n|\r)|.+\z");

            foreach (Match match in matches)
            {
                yield return match.Value;
            }
        }

        private static int RemoveFormerlySerializedAsFromScript(string scriptPath)
        {
            string absolutePath = SerializedShieldPathUtility.ToAbsolutePath(scriptPath);

            if (!File.Exists(absolutePath))
            {
                return 0;
            }

            string originalText = File.ReadAllText(absolutePath);
            int originalCount = SerializedShieldMigrationScanner.CountFormerlySerializedAsAttributes(originalText);

            if (originalCount == 0)
            {
                return 0;
            }

            string updatedText = SerializedShieldMigrationScanner.RemoveFormerlySerializedAsAttributes(originalText);
            File.WriteAllText(absolutePath, updatedText);
            AssetDatabase.ImportAsset(scriptPath);

            return originalCount;
        }
    }
}
