using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
