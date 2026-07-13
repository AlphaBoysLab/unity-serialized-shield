using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlphaBoysLab.SerializedShield.Editor
{
    public sealed class SerializedShieldMigrationWindow : EditorWindow
    {
        private const string PrefsPrefix = "AlphaBoysLab.SerializedShield.";
        private const int MaxBackupSessionsShown = 5;

        private readonly SerializedShieldMigrationOptions options = new SerializedShieldMigrationOptions();
        private readonly Dictionary<string, List<string>> previewsByScriptPath = new Dictionary<string, List<string>>();
        private List<SerializedShieldScriptInfo> scripts = new List<SerializedShieldScriptInfo>();
        private List<string> backupSessionFiles = new List<string>();
        private Vector2 scrollPosition;
        private bool showProjectScripts = true;
        private bool showPackageScripts = true;
        private string scriptSearchText = string.Empty;
        private string statusMessage = "Click Scan to find scripts with FormerlySerializedAs attributes.";

        [MenuItem("Tools/SerializedShield/Migration Window")]
        public static void Open()
        {
            GetWindow<SerializedShieldMigrationWindow>("SerializedShield Migration");
        }

        private void OnEnable()
        {
            LoadOptions();
            RefreshScriptList();
            RefreshBackupList();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("SerializedShield Migration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This tool reserializes Unity assets that reference scripts containing FormerlySerializedAs, then optionally removes those attributes from the scripts. Attributes are only removed when a verification pass confirms the old names no longer appear in any covered serialized file. Keep version control clean before running a migration.",
                MessageType.Info);

            DrawOptions();
            DrawScriptFilters();
            List<SerializedShieldScriptInfo> visibleScripts = GetVisibleScripts();
            DrawToolbar(visibleScripts);
            EditorGUILayout.LabelField(statusMessage, EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawScriptList(visibleScripts);
            GUILayout.Space(8);
            DrawBackups();
            EditorGUILayout.EndScrollView();
        }

        private void DrawOptions()
        {
            EditorGUILayout.LabelField("Migration Options", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            options.IncludePrefabs = EditorGUILayout.ToggleLeft("Include prefab assets", options.IncludePrefabs);
            options.IncludeScenes = EditorGUILayout.ToggleLeft("Include scene files", options.IncludeScenes);
            options.IncludeAssetFiles = EditorGUILayout.ToggleLeft("Include ScriptableObject / .asset files", options.IncludeAssetFiles);
            options.CreateBackup = EditorGUILayout.ToggleLeft("Create backup before migration", options.CreateBackup);
            options.RemoveAttributesAfterMigration = EditorGUILayout.ToggleLeft("Remove FormerlySerializedAs attributes after verified migration", options.RemoveAttributesAfterMigration);

            if (EditorGUI.EndChangeCheck())
            {
                SaveOptions();
            }

            if (options.RemoveAttributesAfterMigration
                && (!options.IncludePrefabs || !options.IncludeScenes || !options.IncludeAssetFiles))
            {
                EditorGUILayout.HelpBox(
                    "Attribute removal requires prefabs, scenes, and .asset files to all be included; otherwise removal is refused after migration.",
                    MessageType.Warning);
            }
        }

        private void DrawToolbar(List<SerializedShieldScriptInfo> visibleScripts)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Scan"))
            {
                RefreshScriptList();
                RefreshBackupList();
            }

            using (new EditorGUI.DisabledScope(visibleScripts.Count == 0))
            {
                if (GUILayout.Button("Migrate All Listed Scripts"))
                {
                    MigrateAllScripts(visibleScripts);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawScriptFilters()
        {
            EditorGUILayout.LabelField("Script Filters", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            showProjectScripts = EditorGUILayout.ToggleLeft("Project Scripts", showProjectScripts, GUILayout.Width(130));
            showPackageScripts = EditorGUILayout.ToggleLeft("Package Scripts", showPackageScripts, GUILayout.Width(130));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(50));
            scriptSearchText = EditorGUILayout.TextField(scriptSearchText);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(scriptSearchText)))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    scriptSearchText = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(string.Format("Showing {0} of {1}", GetVisibleScripts().Count, scripts.Count), EditorStyles.miniLabel);
        }

        private void DrawScriptList(List<SerializedShieldScriptInfo> visibleScripts)
        {
            if (visibleScripts.Count == 0)
            {
                string message = scripts.Count == 0
                    ? "No scripts with FormerlySerializedAs attributes were found."
                    : "No scripts match the current Project/Package filters or search text.";
                EditorGUILayout.HelpBox(message, MessageType.None);
                return;
            }

            foreach (SerializedShieldScriptInfo script in visibleScripts)
            {
                DrawScriptRow(script);
            }
        }

        private void DrawScriptRow(SerializedShieldScriptInfo script)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(Path.GetFileName(script.ScriptPath), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(script.ScriptPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("FormerlySerializedAs count", script.AttributeCount.ToString());
            EditorGUILayout.LabelField("Detected field migrations", script.FieldMigrations.Count.ToString());

            foreach (SerializedShieldFieldMigration migration in script.FieldMigrations)
            {
                EditorGUILayout.LabelField(
                    string.Format(
                        "  {0} -> {1}",
                        string.Join(", ", migration.FormerNames.Distinct().ToArray()),
                        migration.CurrentName),
                    EditorStyles.miniLabel);
            }

            List<string> unmappedNames = script.FormerNames
                .Distinct()
                .Where(name => !script.FieldMigrations.Any(migration => migration.FormerNames.Contains(name)))
                .ToList();

            if (unmappedNames.Count > 0)
            {
                EditorGUILayout.LabelField(
                    "  Unmapped old names (Unity reserialize still migrates them): " + string.Join(", ", unmappedNames.ToArray()),
                    EditorStyles.wordWrappedMiniLabel);
            }

            foreach (string warning in script.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Preview References"))
            {
                PreviewReferences(script);
            }

            if (GUILayout.Button("Dry Run"))
            {
                DryRunScript(script);
            }

            if (GUILayout.Button("Migrate / Serialize"))
            {
                MigrateScript(script);
            }

            EditorGUILayout.EndHorizontal();

            List<string> preview;

            if (previewsByScriptPath.TryGetValue(script.ScriptPath, out preview))
            {
                EditorGUILayout.LabelField(string.Format("Referenced serialized files: {0}", preview.Count));

                foreach (string assetPath in preview.Take(8))
                {
                    EditorGUILayout.LabelField("  " + assetPath, EditorStyles.miniLabel);
                }

                if (preview.Count > 8)
                {
                    EditorGUILayout.LabelField(string.Format("  ...and {0} more", preview.Count - 8), EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBackups()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Backups", EditorStyles.boldLabel);

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                RefreshBackupList();
            }

            EditorGUILayout.EndHorizontal();

            if (backupSessionFiles.Count == 0)
            {
                EditorGUILayout.LabelField("No backup sessions found.", EditorStyles.miniLabel);
                return;
            }

            foreach (string sessionFile in backupSessionFiles.Take(MaxBackupSessionsShown))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.SelectableLabel(sessionFile, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button("Restore", GUILayout.Width(70)))
                {
                    RestoreBackup(sessionFile);
                }

                EditorGUILayout.EndHorizontal();
            }

            if (backupSessionFiles.Count > MaxBackupSessionsShown)
            {
                EditorGUILayout.LabelField(
                    string.Format("  ...and {0} older session(s) in {1}", backupSessionFiles.Count - MaxBackupSessionsShown, SerializedShieldMigrationBackup.GetBackupRoot()),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                "Backups are never pruned automatically; delete old session folders manually and add the backup folder to your VCS ignore file.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void RefreshScriptList()
        {
            scripts = SerializedShieldMigrationScanner.FindScriptsWithFormerlySerializedAs();
            previewsByScriptPath.Clear();
            statusMessage = string.Format("Found {0} script(s) with FormerlySerializedAs attributes.", scripts.Count);
            Repaint();
        }

        private void RefreshBackupList()
        {
            // Cached so OnGUI never touches the disk per repaint (audit U-M7).
            backupSessionFiles = SerializedShieldMigrationBackup.GetSessionFiles();
        }

        private void PreviewReferences(SerializedShieldScriptInfo script)
        {
            List<string> targetPaths = SerializedShieldMigrationProcessor.PreviewTargets(script, options);
            previewsByScriptPath[script.ScriptPath] = targetPaths;
            statusMessage = string.Format("Found {0} serialized file(s) referencing {1}.", targetPaths.Count, script.ScriptPath);
        }

        private void DryRunScript(SerializedShieldScriptInfo script)
        {
            SerializedShieldDryRunResult dryRun = SerializedShieldMigrationProcessor.DryRun(script, options);

            if (dryRun.Cancelled)
            {
                statusMessage = "Dry run cancelled.";
                return;
            }

            foreach (string line in dryRun.Lines)
            {
                Debug.Log("[SerializedShield] Dry run: " + line);
            }

            statusMessage = string.Format(
                "Dry run for {0}: {1} key rename(s) across the project. See the Console for the per-file diff.",
                script.ScriptPath,
                dryRun.TotalRenameCount);
        }

        private void MigrateScript(SerializedShieldScriptInfo script)
        {
            if (!EditorUtility.DisplayDialog(
                "Migrate serialized data",
                "This will backup files, rewrite and reserialize referenced assets, verify the migration, and then apply the selected cleanup options.",
                "Migrate",
                "Cancel"))
            {
                return;
            }

            try
            {
                SerializedShieldMigrationResult result = SerializedShieldMigrationProcessor.MigrateScript(script, options);
                LogResultWarnings(result);
                RefreshScriptList();
                RefreshBackupList();
                statusMessage = DescribeResult(result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                statusMessage = "Migration failed. Check the Console for details.";
            }
        }

        private void MigrateAllScripts(List<SerializedShieldScriptInfo> scriptsToMigrate)
        {
            if (!EditorUtility.DisplayDialog(
                "Migrate all listed scripts",
                string.Format("This will run migration for {0} visible script(s). One shared backup session will be created if backup is enabled.", scriptsToMigrate.Count),
                "Migrate All",
                "Cancel"))
            {
                return;
            }

            SerializedShieldBackupSession sharedBackupSession = options.CreateBackup
                ? SerializedShieldMigrationBackup.CreateSession()
                : null;

            int totalReserialized = 0;
            int totalRemoved = 0;
            int totalTextMigratedAssets = 0;
            int totalTextMigratedFields = 0;
            List<string> failures = new List<string>();
            bool stopped = false;

            foreach (SerializedShieldScriptInfo script in scriptsToMigrate.ToArray())
            {
                try
                {
                    SerializedShieldMigrationResult result = SerializedShieldMigrationProcessor.MigrateScript(script, options, sharedBackupSession);
                    LogResultWarnings(result);

                    if (result.Aborted)
                    {
                        // Abort reasons (cancel, dirty scenes, prefab stage, binary
                        // serialization) apply to the whole batch: stop loudly instead of
                        // continuing into the same wall.
                        failures.Add(script.ScriptPath + ": aborted (" + result.AbortReason + ")");
                        stopped = true;
                        break;
                    }

                    totalReserialized += result.ReserializedAssetCount;
                    totalRemoved += result.RemovedAttributeCount;
                    totalTextMigratedAssets += result.TextMigratedAssetCount;
                    totalTextMigratedFields += result.TextMigratedFieldCount;
                }
                catch (Exception exception)
                {
                    // One failing script must not abort the rest of the batch (audit U-H10).
                    Debug.LogException(exception);
                    failures.Add(script.ScriptPath + ": " + exception.Message);
                }
            }

            RefreshScriptList();
            RefreshBackupList();
            statusMessage = string.Format(
                "{0} Text-migrated {1} field key(s) in {2} file(s), reserialized {3} file(s), removed {4} attribute(s).",
                stopped ? "Migration stopped." : "Migration complete.",
                totalTextMigratedFields,
                totalTextMigratedAssets,
                totalReserialized,
                totalRemoved);

            if (sharedBackupSession != null)
            {
                statusMessage += " Backup: " + sharedBackupSession.SessionFilePath;
            }

            if (failures.Count > 0)
            {
                statusMessage += string.Format(" {0} script(s) failed or aborted - see the Console.", failures.Count);

                foreach (string failure in failures)
                {
                    Debug.LogError("[SerializedShield] Migration issue: " + failure);
                }
            }
        }

        private void RestoreBackup(string sessionFilePath)
        {
            if (!EditorUtility.DisplayDialog(
                "Restore backup",
                "This will overwrite current files with the backup session:\n" + sessionFilePath,
                "Restore",
                "Cancel"))
            {
                return;
            }

            string message;
            SerializedShieldMigrationBackup.RestoreSession(sessionFilePath, out message);
            RefreshScriptList();
            RefreshBackupList();
            statusMessage = message;
        }

        private static string DescribeResult(SerializedShieldMigrationResult result)
        {
            if (result.Aborted)
            {
                return "Migration aborted: " + result.AbortReason;
            }

            string description = string.Format(
                "Migrated {0}. Text-migrated {1} field key(s) in {2} file(s), reserialized {3} file(s), removed {4} attribute(s).",
                result.ScriptPath,
                result.TextMigratedFieldCount,
                result.TextMigratedAssetCount,
                result.ReserializedAssetCount,
                result.RemovedAttributeCount);

            if (result.AttributeRemovalSkipped)
            {
                description += " Attribute removal skipped: " + result.AttributeRemovalSkipReason;
            }
            else if (result.KeptAttributeNames.Count > 0)
            {
                description += string.Format(" Kept {0} attribute(s) pending verification - see the Console.", result.KeptAttributeNames.Count);
            }

            if (!string.IsNullOrEmpty(result.BackupSessionPath))
            {
                description += " Backup: " + result.BackupSessionPath;
            }

            return description;
        }

        private static void LogResultWarnings(SerializedShieldMigrationResult result)
        {
            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning("[SerializedShield] " + warning);
            }
        }

        private void LoadOptions()
        {
            options.IncludePrefabs = EditorPrefs.GetBool(PrefsPrefix + "IncludePrefabs", true);
            options.IncludeScenes = EditorPrefs.GetBool(PrefsPrefix + "IncludeScenes", true);
            options.IncludeAssetFiles = EditorPrefs.GetBool(PrefsPrefix + "IncludeAssetFiles", true);
            options.CreateBackup = EditorPrefs.GetBool(PrefsPrefix + "CreateBackup", true);
            options.RemoveAttributesAfterMigration = EditorPrefs.GetBool(PrefsPrefix + "RemoveAttributesAfterMigration", true);
        }

        private void SaveOptions()
        {
            EditorPrefs.SetBool(PrefsPrefix + "IncludePrefabs", options.IncludePrefabs);
            EditorPrefs.SetBool(PrefsPrefix + "IncludeScenes", options.IncludeScenes);
            EditorPrefs.SetBool(PrefsPrefix + "IncludeAssetFiles", options.IncludeAssetFiles);
            EditorPrefs.SetBool(PrefsPrefix + "CreateBackup", options.CreateBackup);
            EditorPrefs.SetBool(PrefsPrefix + "RemoveAttributesAfterMigration", options.RemoveAttributesAfterMigration);
        }

        private List<SerializedShieldScriptInfo> GetVisibleScripts()
        {
            return scripts.Where(IsScriptVisible).ToList();
        }

        private bool IsScriptVisible(SerializedShieldScriptInfo script)
        {
            bool isPackageScript = IsPackageScript(script);

            if (isPackageScript && !showPackageScripts)
            {
                return false;
            }

            if (!isPackageScript && !showProjectScripts)
            {
                return false;
            }

            if (!MatchesSearch(script))
            {
                return false;
            }

            return true;
        }

        private bool MatchesSearch(SerializedShieldScriptInfo script)
        {
            if (string.IsNullOrWhiteSpace(scriptSearchText))
            {
                return true;
            }

            string query = scriptSearchText.Trim();

            if (script.ScriptPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string scriptFileName = Path.GetFileName(script.ScriptPath);

            if (!string.IsNullOrEmpty(scriptFileName) && scriptFileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return script.FormerNames != null
                && script.FormerNames.Any(name => !string.IsNullOrEmpty(name) && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsPackageScript(SerializedShieldScriptInfo script)
        {
            return script.ScriptPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
