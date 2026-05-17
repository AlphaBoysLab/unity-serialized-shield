using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlphaBoysLab.SerializedShield.Editor
{
    public static class SerializedShieldMigrationBackup
    {
        private const string BackupFolderName = "SerializedShieldMigrationBackups";

        public static SerializedShieldBackupSession CreateSession(IEnumerable<string> assetPaths)
        {
            string sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string sessionFolder = Path.Combine(GetBackupRoot(), sessionId);
            Directory.CreateDirectory(sessionFolder);

            SerializedShieldBackupSession session = new SerializedShieldBackupSession
            {
                Id = sessionId,
                CreatedAt = DateTime.Now.ToString("u"),
                SessionFilePath = Path.Combine(sessionFolder, "session.json")
            };

            foreach (string assetPath in assetPaths.Where(path => !string.IsNullOrEmpty(path)).Distinct())
            {
                string absolutePath = SerializedShieldPathUtility.ToAbsolutePath(assetPath);

                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                string backupPath = Path.Combine(sessionFolder, SerializedShieldPathUtility.BuildSafeBackupFileName(assetPath));
                File.Copy(absolutePath, backupPath, true);

                session.Files.Add(new SerializedShieldBackupEntry
                {
                    AssetPath = assetPath,
                    BackupPath = backupPath
                });
            }

            File.WriteAllText(session.SessionFilePath, JsonUtility.ToJson(session, true));
            return session;
        }

        public static List<string> GetSessionFiles()
        {
            string backupRoot = GetBackupRoot();

            if (!Directory.Exists(backupRoot))
            {
                return new List<string>();
            }

            return Directory.GetFiles(backupRoot, "session.json", SearchOption.AllDirectories)
                .OrderByDescending(path => path)
                .ToList();
        }

        public static SerializedShieldBackupSession LoadSession(string sessionFilePath)
        {
            return JsonUtility.FromJson<SerializedShieldBackupSession>(File.ReadAllText(sessionFilePath));
        }

        public static bool RestoreSession(string sessionFilePath, out string message)
        {
            if (!File.Exists(sessionFilePath))
            {
                message = "Backup session file was not found.";
                return false;
            }

            SerializedShieldBackupSession session = LoadSession(sessionFilePath);
            int restoredCount = 0;

            foreach (SerializedShieldBackupEntry entry in session.Files)
            {
                if (!File.Exists(entry.BackupPath))
                {
                    continue;
                }

                string destinationPath = SerializedShieldPathUtility.ToAbsolutePath(entry.AssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(entry.BackupPath, destinationPath, true);
                restoredCount++;
            }

            AssetDatabase.Refresh();
            message = string.Format("Restored {0} file(s) from backup {1}.", restoredCount, session.Id);
            return true;
        }

        private static string GetBackupRoot()
        {
            return Path.Combine(SerializedShieldPathUtility.ProjectRoot, BackupFolderName);
        }
    }
}
