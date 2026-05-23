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
        private readonly SerializedShieldMigrationOptions options = new SerializedShieldMigrationOptions();
        private readonly Dictionary<string, List<string>> previewsByScriptPath = new Dictionary<string, List<string>>();
        private List<SerializedShieldScriptInfo> scripts = new List<SerializedShieldScriptInfo>();
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
            RefreshScriptList();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("SerializedShield Migration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This tool reserializes Unity assets that reference scripts containing FormerlySerializedAs, then optionally removes those attributes from the scripts. Keep version control clean before running a migration.",
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
            options.IncludePrefabs = EditorGUILayout.ToggleLeft("Include prefab assets", options.IncludePrefabs);
            options.IncludeScenes = EditorGUILayout.ToggleLeft("Include scene files", options.IncludeScenes);
            options.IncludeAssetFiles = EditorGUILayout.ToggleLeft("Include ScriptableObject / .asset files", options.IncludeAssetFiles);
            options.CreateBackup = EditorGUILayout.ToggleLeft("Create backup before migration", options.CreateBackup);
            options.RemoveAttributesAfterMigration = EditorGUILayout.ToggleLeft("Remove FormerlySerializedAs attributes after migration", options.RemoveAttributesAfterMigration);
        }

        private void DrawToolbar(List<SerializedShieldScriptInfo> visibleScripts)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Scan"))
            {
                RefreshScriptList();
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
            EditorGUILayout.LabelField("Old names: " + string.Join(", ", script.FormerNames.Distinct().ToArray()), EditorStyles.wordWrappedLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Preview References"))
            {
                PreviewReferences(script);
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
            List<string> sessionFiles = SerializedShieldMigrationBackup.GetSessionFiles();

            EditorGUILayout.LabelField("Backups", EditorStyles.boldLabel);

            if (sessionFiles.Count == 0)
            {
                EditorGUILayout.LabelField("No backup sessions found.", EditorStyles.miniLabel);
                return;
            }

            string latestSession = sessionFiles[0];
            EditorGUILayout.SelectableLabel(latestSession, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (GUILayout.Button("Restore Latest Backup"))
            {
                RestoreBackup(latestSession);
            }
        }

        private void RefreshScriptList()
        {
            scripts = SerializedShieldMigrationScanner.FindScriptsWithFormerlySerializedAs();
            previewsByScriptPath.Clear();
            statusMessage = string.Format("Found {0} script(s) with FormerlySerializedAs attributes.", scripts.Count);
            Repaint();
        }

        private void PreviewReferences(SerializedShieldScriptInfo script)
        {
            List<string> targetPaths = SerializedShieldMigrationProcessor.PreviewTargets(script, options);
            previewsByScriptPath[script.ScriptPath] = targetPaths;
            statusMessage = string.Format("Found {0} serialized file(s) referencing {1}.", targetPaths.Count, script.ScriptPath);
        }

        private void MigrateScript(SerializedShieldScriptInfo script)
        {
            if (!EditorUtility.DisplayDialog(
                "Migrate serialized data",
                "This will backup files, reserialize referenced assets, and then apply the selected cleanup options.",
                "Migrate",
                "Cancel"))
            {
                return;
            }

            try
            {
                SerializedShieldMigrationResult result = SerializedShieldMigrationProcessor.MigrateScript(script, options);
                RefreshScriptList();
                statusMessage = string.Format(
                    "Migrated {0}. Text-migrated {1} field(s) in {2} file(s), reserialized {3} file(s), removed {4} attribute(s).",
                    result.ScriptPath,
                    result.TextMigratedFieldCount,
                    result.TextMigratedAssetCount,
                    result.ReserializedAssetCount,
                    result.RemovedAttributeCount);
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
                string.Format("This will run migration for {0} visible script(s). A backup will be created for each migration if backup is enabled.", scriptsToMigrate.Count),
                "Migrate All",
                "Cancel"))
            {
                return;
            }

            int totalReserialized = 0;
            int totalRemoved = 0;
            int totalTextMigratedAssets = 0;
            int totalTextMigratedFields = 0;

            foreach (SerializedShieldScriptInfo script in scriptsToMigrate.ToArray())
            {
                SerializedShieldMigrationResult result = SerializedShieldMigrationProcessor.MigrateScript(script, options);
                totalReserialized += result.ReserializedAssetCount;
                totalRemoved += result.RemovedAttributeCount;
                totalTextMigratedAssets += result.TextMigratedAssetCount;
                totalTextMigratedFields += result.TextMigratedFieldCount;
            }

            RefreshScriptList();
            statusMessage = string.Format(
                "Migration complete. Text-migrated {0} field(s) in {1} file(s), reserialized {2} file(s), removed {3} attribute(s).",
                totalTextMigratedFields,
                totalTextMigratedAssets,
                totalReserialized,
                totalRemoved);
        }

        private void RestoreBackup(string sessionFilePath)
        {
            if (!EditorUtility.DisplayDialog(
                "Restore latest backup",
                "This will overwrite current files with the latest backup session.",
                "Restore",
                "Cancel"))
            {
                return;
            }

            string message;

            if (SerializedShieldMigrationBackup.RestoreSession(sessionFilePath, out message))
            {
                RefreshScriptList();
                statusMessage = message;
            }
            else
            {
                statusMessage = message;
            }
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
