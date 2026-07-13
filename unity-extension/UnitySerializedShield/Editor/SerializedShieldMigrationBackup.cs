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

        /// <summary>
        /// Creates an empty backup session. Session ids combine a timestamp with a random
        /// suffix so sessions created within the same second never collide and never
        /// overwrite each other's files (audit U-C6). Batch migrations create ONE session
        /// and add every script's files to it, so a file is only ever backed up in its
        /// pre-migration state.
        /// </summary>
        public static SerializedShieldBackupSession CreateSession()
        {
            string sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss")
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 8);
            string sessionFolder = Path.Combine(GetBackupRoot(), sessionId);
            Directory.CreateDirectory(sessionFolder);

            SerializedShieldBackupSession session = new SerializedShieldBackupSession
            {
                Id = sessionId,
                CreatedAt = DateTime.Now.ToString("u"),
                SessionFilePath = Path.Combine(sessionFolder, "session.json")
            };

            SaveSession(session);
            return session;
        }

        public static SerializedShieldBackupSession CreateSession(IEnumerable<string> assetPaths)
        {
            SerializedShieldBackupSession session = CreateSession();
            AddFilesToSession(session, assetPaths);
            return session;
        }

        /// <summary>
        /// Copies the given assets into the session folder. Files already present in the
        /// session are NOT copied again, so a shared batch session always keeps the
        /// original pre-migration content.
        /// </summary>
        public static void AddFilesToSession(SerializedShieldBackupSession session, IEnumerable<string> assetPaths)
        {
            string sessionFolder = Path.GetDirectoryName(session.SessionFilePath);
            HashSet<string> alreadyBackedUp = new HashSet<string>(
                session.Files.Select(entry => entry.AssetPath),
                StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            foreach (string assetPath in assetPaths.Where(path => !string.IsNullOrEmpty(path)).Distinct())
            {
                if (alreadyBackedUp.Contains(assetPath))
                {
                    continue;
                }

                string absolutePath = SerializedShieldPathUtility.ToPhysicalPath(assetPath);

                if (absolutePath == null || !File.Exists(absolutePath))
                {
                    continue;
                }

                string backupFileName = SerializedShieldPathUtility.BuildSafeBackupFileName(assetPath);
                File.Copy(absolutePath, Path.Combine(sessionFolder, backupFileName), true);

                session.Files.Add(new SerializedShieldBackupEntry
                {
                    AssetPath = assetPath,
                    BackupPath = backupFileName
                });
                alreadyBackedUp.Add(assetPath);
                changed = true;
            }

            if (changed)
            {
                SaveSession(session);
            }
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
            string sessionFolder = Path.GetDirectoryName(sessionFilePath);

            // The restore has the same open-scene hazard as migration (audit U-M9): a
            // stale in-memory scene saved after the restore would overwrite the restored
            // file. Refuse instead of proceeding silently.
            if (SerializedShieldSceneUtility.IsPrefabStageOpen())
            {
                message = "A prefab is open in Prefab Mode. Exit Prefab Mode before restoring a backup.";
                return false;
            }

            HashSet<string> restoredAssetPaths = new HashSet<string>(
                session.Files.Select(entry => entry.AssetPath),
                StringComparer.OrdinalIgnoreCase);
            List<string> dirtyScenes = SerializedShieldSceneUtility.GetDirtyAffectedScenePaths(restoredAssetPaths);

            if (dirtyScenes.Count > 0)
            {
                message = "Restore refused: open scene(s) with unsaved changes would overwrite the restored files when saved: "
                    + string.Join(", ", dirtyScenes.ToArray())
                    + ". Save or discard those changes first.";
                return false;
            }

            int restoredCount = 0;
            List<string> failures = new List<string>();

            foreach (SerializedShieldBackupEntry entry in session.Files)
            {
                string backupFilePath = ResolveBackupPath(sessionFolder, entry.BackupPath);

                if (backupFilePath == null || !File.Exists(backupFilePath))
                {
                    failures.Add(entry.AssetPath + " (backup file missing)");
                    continue;
                }

                try
                {
                    string destinationPath = SerializedShieldPathUtility.ToPhysicalPath(entry.AssetPath);

                    if (destinationPath == null)
                    {
                        failures.Add(entry.AssetPath + " (could not resolve destination path)");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.Copy(backupFilePath, destinationPath, true);
                    restoredCount++;
                }
                catch (Exception exception)
                {
                    failures.Add(entry.AssetPath + " (" + exception.Message + ")");
                }
            }

            string reloadWarning;
            SerializedShieldSceneUtility.ReloadOpenScenesIfAffected(restoredAssetPaths, out reloadWarning);

            AssetDatabase.Refresh();
            message = string.Format("Restored {0} file(s) from backup {1}.", restoredCount, session.Id);

            if (failures.Count > 0)
            {
                message += " FAILED to restore " + failures.Count + " file(s): " + string.Join("; ", failures.ToArray());
            }

            if (!string.IsNullOrEmpty(reloadWarning))
            {
                message += " " + reloadWarning;
            }

            return failures.Count == 0;
        }

        public static string GetBackupRoot()
        {
            return Path.Combine(SerializedShieldPathUtility.ProjectRoot, BackupFolderName);
        }

        private static void SaveSession(SerializedShieldBackupSession session)
        {
            File.WriteAllText(session.SessionFilePath, JsonUtility.ToJson(session, true));
        }

        /// <summary>
        /// Sessions created since 2.0.0 store a file name relative to the session folder
        /// (portable across machines); older sessions stored an absolute path.
        /// </summary>
        private static string ResolveBackupPath(string sessionFolder, string backupPath)
        {
            if (string.IsNullOrEmpty(backupPath))
            {
                return null;
            }

            if (Path.IsPathRooted(backupPath))
            {
                if (File.Exists(backupPath))
                {
                    return backupPath;
                }

                // The project may have moved; fall back to the file name inside the
                // session folder.
                return Path.Combine(sessionFolder, Path.GetFileName(backupPath));
            }

            return Path.Combine(sessionFolder, backupPath);
        }
    }
}
