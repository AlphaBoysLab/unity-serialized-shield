using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.VersionControl;

namespace AlphaBoysLab.SerializedShield.Editor
{
    /// <summary>
    /// Performs serialized-data migrations. Every failure mode aborts or reports loudly:
    /// - Requires Force Text asset serialization (audit U-H1, feature gap 3).
    /// - Refuses to run in Prefab Mode and aborts when affected open scenes stay dirty
    ///   after the save prompt; reloads affected open scenes from disk afterwards (U-C5).
    /// - Backs up AFTER the scene-save prompt so a restore never discards saved work (U-H8).
    /// - Stages all YAML rewrites before writing anything; read/write failures are
    ///   collected per file, never swallowed (U-H7), and block attribute removal.
    /// - Preserves each file's encoding and BOM (U-H6).
    /// - Attribute removal is gated on a post-migration verification pass: an attribute is
    ///   removed only when its old name is verified absent from every covered serialized
    ///   file AND the run covered all asset kinds with zero failures (U-C4, feature gap 2).
    /// </summary>
    public static class SerializedShieldMigrationProcessor
    {
        private const string ProgressTitle = "SerializedShield Migration";

        private sealed class StagedWrite
        {
            public string AssetPath;
            public string AbsolutePath;
            public SerializedShieldTextFileContent OriginalContent;
            public SerializedShieldYamlRewriteResult Rewrite;
        }

        public static List<string> PreviewTargets(SerializedShieldScriptInfo script, SerializedShieldMigrationOptions options)
        {
            try
            {
                return SerializedShieldMigrationScanner
                    .FindSerializedAssetsReferencingScript(script.ScriptGuid, options, CreateProgress("Scanning serialized files"))
                    .TargetAssetPaths;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Dry run (feature gap 1): reports every key rename the migration would perform,
        /// per file with line numbers, without writing anything.
        /// </summary>
        public static SerializedShieldDryRunResult DryRun(SerializedShieldScriptInfo script, SerializedShieldMigrationOptions options)
        {
            SerializedShieldDryRunResult dryRun = new SerializedShieldDryRunResult();

            try
            {
                SerializedShieldAssetScanResult scan = SerializedShieldMigrationScanner
                    .FindSerializedAssetsReferencingScript(script.ScriptGuid, options, CreateProgress("Scanning serialized files"));

                if (scan.Cancelled)
                {
                    dryRun.Cancelled = true;
                    return dryRun;
                }

                foreach (string assetPath in scan.UnreadableAssetPaths)
                {
                    dryRun.Lines.Add(assetPath + ": UNREADABLE (would block attribute removal)");
                }

                for (int targetIndex = 0; targetIndex < scan.TargetAssetPaths.Count; targetIndex++)
                {
                    string assetPath = scan.TargetAssetPaths[targetIndex];

                    if (EditorUtility.DisplayCancelableProgressBar(
                        ProgressTitle,
                        "Previewing " + assetPath,
                        (float)targetIndex / Math.Max(scan.TargetAssetPaths.Count, 1)))
                    {
                        dryRun.Cancelled = true;
                        return dryRun;
                    }

                    string absolutePath = SerializedShieldPathUtility.ToPhysicalPath(assetPath);

                    if (absolutePath == null || !File.Exists(absolutePath))
                    {
                        dryRun.Lines.Add(assetPath + ": file not found");
                        continue;
                    }

                    SerializedShieldTextFileContent content;

                    try
                    {
                        content = SerializedShieldTextFileUtility.Read(absolutePath);
                    }
                    catch (Exception exception)
                    {
                        dryRun.Lines.Add(assetPath + ": could not read (" + exception.Message + ")");
                        continue;
                    }

                    SerializedShieldYamlRewriteResult rewrite = SerializedShieldYamlRewriter.RenameComponentKeys(
                        content.Text,
                        script.ScriptGuid,
                        script.FieldMigrations);

                    foreach (SerializedShieldYamlKeyRename rename in rewrite.Renames)
                    {
                        dryRun.Lines.Add(string.Format(
                            "{0}: line {1}: {2} -> {3}",
                            assetPath,
                            rename.LineNumber,
                            rename.OldKey,
                            rename.NewKey));
                        dryRun.TotalRenameCount++;
                    }

                    foreach (string warning in rewrite.Warnings)
                    {
                        dryRun.Lines.Add(assetPath + ": " + warning);
                    }
                }

                return dryRun;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static SerializedShieldMigrationResult MigrateScript(SerializedShieldScriptInfo script, SerializedShieldMigrationOptions options)
        {
            return MigrateScript(script, options, null);
        }

        /// <summary>
        /// Migrates one script. Pass <paramref name="sharedBackupSession"/> to make several
        /// migrations share ONE backup session (batch mode, audit U-C6); when null and
        /// backups are enabled, a fresh uniquely-named session is created.
        /// </summary>
        public static SerializedShieldMigrationResult MigrateScript(
            SerializedShieldScriptInfo script,
            SerializedShieldMigrationOptions options,
            SerializedShieldBackupSession sharedBackupSession)
        {
            SerializedShieldMigrationResult result = new SerializedShieldMigrationResult
            {
                ScriptPath = script.ScriptPath
            };

            try
            {
                // --- Pre-flight guards (nothing on disk is touched before these pass) ---

                if (EditorSettings.serializationMode != SerializationMode.ForceText)
                {
                    return Abort(result,
                        "Asset Serialization Mode is not 'Force Text' (Project Settings > Editor). "
                        + "Binary or mixed serialized assets cannot be scanned or migrated safely.");
                }

                if (SerializedShieldSceneUtility.IsPrefabStageOpen())
                {
                    return Abort(result, "A prefab is open in Prefab Mode. Save and exit Prefab Mode before migrating.");
                }

                SerializedShieldAssetScanResult scan = SerializedShieldMigrationScanner.FindSerializedAssetsReferencingScript(
                    script.ScriptGuid,
                    options,
                    CreateProgress("Scanning serialized files"));

                if (scan.Cancelled)
                {
                    return Abort(result, "Migration cancelled during the serialized file scan.");
                }

                List<string> targetAssetPaths = scan.TargetAssetPaths;
                result.TargetAssetPaths.AddRange(targetAssetPaths);

                foreach (string unreadablePath in scan.UnreadableAssetPaths)
                {
                    result.FailedAssetPaths.Add(unreadablePath);
                    result.Warnings.Add("Could not read serialized file: " + unreadablePath);
                }

                // --- Open scene handling BEFORE the backup (U-C5, U-H8) ---

                HashSet<string> targetPathSet = new HashSet<string>(targetAssetPaths, StringComparer.OrdinalIgnoreCase);
                bool anySceneTargets = targetAssetPaths.Any(
                    path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));

                if (anySceneTargets)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        return Abort(result, "Migration cancelled at the scene save prompt.");
                    }

                    List<string> dirtyScenePaths = SerializedShieldSceneUtility.GetDirtyAffectedScenePaths(targetPathSet);

                    if (dirtyScenePaths.Count > 0)
                    {
                        return Abort(result,
                            "Open scene(s) still have unsaved changes (\"Don't Save\" was chosen or saving failed): "
                            + string.Join(", ", dirtyScenePaths.ToArray())
                            + ". Save or discard those changes, then run the migration again.");
                    }
                }

                // --- Backup (after the guards so a restore never loses saved work) ---

                if (options.CreateBackup)
                {
                    SerializedShieldBackupSession backupSession = sharedBackupSession
                        ?? SerializedShieldMigrationBackup.CreateSession();
                    HashSet<string> filesToBackup = new HashSet<string>(targetAssetPaths);
                    filesToBackup.Add(script.ScriptPath);
                    SerializedShieldMigrationBackup.AddFilesToSession(backupSession, filesToBackup);
                    result.BackupSessionPath = backupSession.SessionFilePath;
                }

                // --- Version control checkout (feature gap 6) ---

                List<string> allTouchedPaths = new List<string>(targetAssetPaths);
                allTouchedPaths.Add(script.ScriptPath);
                TryCheckout(allTouchedPaths, result);

                // --- Staged text migration (U-H7): read + rewrite everything first ---

                if (targetAssetPaths.Count > 0)
                {
                    bool stagingCancelled;
                    List<StagedWrite> stagedWrites = StageTextMigration(script, targetAssetPaths, result, out stagingCancelled);

                    if (stagingCancelled)
                    {
                        return Abort(result, "Migration cancelled while preparing serialized file rewrites. No files were changed.");
                    }

                    ApplyStagedWrites(stagedWrites, result);

                    if (result.TextMigratedAssetCount > 0)
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }

                    List<string> reserializeTargets = targetAssetPaths
                        .Where(path => !result.FailedAssetPaths.Contains(path))
                        .ToList();

                    if (reserializeTargets.Count > 0)
                    {
                        AssetDatabase.ForceReserializeAssets(reserializeTargets);
                        result.ReserializedAssetCount = reserializeTargets.Count;
                    }

                    string reloadWarning;

                    if (!SerializedShieldSceneUtility.ReloadOpenScenesIfAffected(targetPathSet, out reloadWarning)
                        && !string.IsNullOrEmpty(reloadWarning))
                    {
                        result.Warnings.Add(reloadWarning);
                    }
                }

                // --- Attribute removal, gated on verification (U-C4, feature gap 2) ---

                if (options.RemoveAttributesAfterMigration)
                {
                    RemoveAttributesWithVerification(script, options, result);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                WriteMigrationLog(result);
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static List<StagedWrite> StageTextMigration(
            SerializedShieldScriptInfo script,
            List<string> targetAssetPaths,
            SerializedShieldMigrationResult result,
            out bool cancelled)
        {
            cancelled = false;
            List<StagedWrite> stagedWrites = new List<StagedWrite>();

            if (script.FieldMigrations == null || script.FieldMigrations.Count == 0)
            {
                return stagedWrites;
            }

            for (int targetIndex = 0; targetIndex < targetAssetPaths.Count; targetIndex++)
            {
                string assetPath = targetAssetPaths[targetIndex];

                if (EditorUtility.DisplayCancelableProgressBar(
                    ProgressTitle,
                    "Rewriting serialized keys in " + assetPath,
                    (float)targetIndex / Math.Max(targetAssetPaths.Count, 1)))
                {
                    cancelled = true;
                    return stagedWrites;
                }

                string absolutePath = SerializedShieldPathUtility.ToPhysicalPath(assetPath);

                if (absolutePath == null || !File.Exists(absolutePath))
                {
                    result.FailedAssetPaths.Add(assetPath);
                    result.Warnings.Add("Serialized file not found on disk: " + assetPath);
                    continue;
                }

                SerializedShieldTextFileContent content;

                try
                {
                    content = SerializedShieldTextFileUtility.Read(absolutePath);
                }
                catch (Exception exception)
                {
                    result.FailedAssetPaths.Add(assetPath);
                    result.Warnings.Add(string.Format(
                        "Could not read '{0}' ({1}). The file was skipped and attribute removal will be refused.",
                        assetPath,
                        exception.Message));
                    continue;
                }

                SerializedShieldYamlRewriteResult rewrite = SerializedShieldYamlRewriter.RenameComponentKeys(
                    content.Text,
                    script.ScriptGuid,
                    script.FieldMigrations);

                foreach (string warning in rewrite.Warnings)
                {
                    result.Warnings.Add(assetPath + ": " + warning);
                }

                if (!rewrite.Changed)
                {
                    continue;
                }

                stagedWrites.Add(new StagedWrite
                {
                    AssetPath = assetPath,
                    AbsolutePath = absolutePath,
                    OriginalContent = content,
                    Rewrite = rewrite
                });
            }

            return stagedWrites;
        }

        private static void ApplyStagedWrites(List<StagedWrite> stagedWrites, SerializedShieldMigrationResult result)
        {
            foreach (StagedWrite stagedWrite in stagedWrites)
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(stagedWrite.AbsolutePath);

                    if (fileInfo.IsReadOnly)
                    {
                        result.FailedAssetPaths.Add(stagedWrite.AssetPath);
                        result.Warnings.Add(string.Format(
                            "'{0}' is read-only (version control?). It was NOT migrated; check it out and re-run.",
                            stagedWrite.AssetPath));
                        continue;
                    }

                    SerializedShieldTextFileUtility.Write(
                        stagedWrite.AbsolutePath,
                        stagedWrite.OriginalContent,
                        stagedWrite.Rewrite.Text);
                    result.TextMigratedAssetCount++;
                    result.TextMigratedFieldCount += stagedWrite.Rewrite.Renames.Count;
                }
                catch (Exception exception)
                {
                    result.FailedAssetPaths.Add(stagedWrite.AssetPath);
                    result.Warnings.Add(string.Format(
                        "Could not write '{0}' ({1}). Attribute removal will be refused.",
                        stagedWrite.AssetPath,
                        exception.Message));
                }
            }
        }

        /// <summary>
        /// Post-migration verification pass (feature gap 2, audit U-C4/U-C3/U-H9): re-scans
        /// every scene, prefab, .asset, .anim and .preset file. An old name is verified
        /// only when no serialized key in any instance block of the script, no
        /// prefab-instance override propertyPath, and no animation binding still uses it.
        /// Only verified names have their attributes removed; everything else is kept and
        /// reported. Removal is refused entirely when coverage was incomplete.
        /// </summary>
        private static void RemoveAttributesWithVerification(
            SerializedShieldScriptInfo script,
            SerializedShieldMigrationOptions options,
            SerializedShieldMigrationResult result)
        {
            List<string> formerNames = script.FormerNames
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToList();

            if (formerNames.Count == 0)
            {
                return;
            }

            if (!options.IncludePrefabs || !options.IncludeScenes || !options.IncludeAssetFiles)
            {
                SkipRemoval(result,
                    "not every asset kind was included in the migration (enable prefabs, scenes, and .asset files, then re-run).");
                return;
            }

            if (result.FailedAssetPaths.Count > 0)
            {
                SkipRemoval(result, string.Format(
                    "{0} serialized file(s) could not be read or written, so migration coverage is incomplete.",
                    result.FailedAssetPaths.Distinct().Count()));
                return;
            }

            bool verificationCancelled;
            Dictionary<string, List<string>> blockingReferences = VerifyFormerNames(
                script,
                formerNames,
                out verificationCancelled);

            if (verificationCancelled)
            {
                SkipRemoval(result, "the verification pass was cancelled.");
                return;
            }

            List<string> verifiedNames = new List<string>();

            foreach (string formerName in formerNames)
            {
                List<string> blockers = blockingReferences[formerName];

                if (blockers.Count == 0)
                {
                    verifiedNames.Add(formerName);
                }
                else
                {
                    result.KeptAttributeNames.Add(formerName);
                    result.Warnings.Add(string.Format(
                        "Kept [FormerlySerializedAs(\"{0}\")]: still referenced by {1}{2}",
                        formerName,
                        string.Join("; ", blockers.Take(3).ToArray()),
                        blockers.Count > 3 ? string.Format(" (and {0} more)", blockers.Count - 3) : string.Empty));
                }
            }

            if (verifiedNames.Count == 0)
            {
                SkipRemoval(result, "no former name could be verified as fully migrated; all attributes were kept.");
                return;
            }

            int removedCount = RemoveFormerlySerializedAsFromScript(script.ScriptPath, verifiedNames, result);

            if (removedCount > 0)
            {
                result.RemovedAttributeCount = removedCount;
                result.RemovedAttributeNames.AddRange(verifiedNames);
            }
        }

        private static Dictionary<string, List<string>> VerifyFormerNames(
            SerializedShieldScriptInfo script,
            List<string> formerNames,
            out bool cancelled)
        {
            cancelled = false;
            Dictionary<string, List<string>> blockingReferences = new Dictionary<string, List<string>>();

            foreach (string formerName in formerNames)
            {
                blockingReferences[formerName] = new List<string>();
            }

            HashSet<string> nameSet = new HashSet<string>(formerNames, StringComparer.Ordinal);
            List<string> verificationAssetPaths = SerializedShieldMigrationScanner.GetVerificationAssetPaths();

            for (int assetIndex = 0; assetIndex < verificationAssetPaths.Count; assetIndex++)
            {
                string assetPath = verificationAssetPaths[assetIndex];

                if (EditorUtility.DisplayCancelableProgressBar(
                    ProgressTitle,
                    "Verifying migration: " + assetPath,
                    (float)assetIndex / Math.Max(verificationAssetPaths.Count, 1)))
                {
                    cancelled = true;
                    return blockingReferences;
                }

                string absolutePath = SerializedShieldPathUtility.ToPhysicalPath(assetPath);

                if (absolutePath == null || Directory.Exists(absolutePath))
                {
                    continue;
                }

                string text;

                try
                {
                    text = File.ReadAllText(absolutePath);
                }
                catch (Exception)
                {
                    // An unreadable file means unverifiable coverage: block every name.
                    foreach (string formerName in formerNames)
                    {
                        blockingReferences[formerName].Add(assetPath + " (unreadable during verification)");
                    }

                    continue;
                }

                string extension = Path.GetExtension(assetPath).ToLowerInvariant();

                if (extension == ".unity" || extension == ".prefab" || extension == ".asset")
                {
                    foreach (SerializedShieldYamlKeyReference reference in
                        SerializedShieldYamlRewriter.FindKeysInScriptBlocks(text, script.ScriptGuid, nameSet))
                    {
                        blockingReferences[reference.Key].Add(assetPath + " (" + reference.Description + ")");
                    }

                    if (extension != ".asset")
                    {
                        foreach (SerializedShieldYamlKeyReference reference in
                            SerializedShieldYamlRewriter.FindPropertyPathReferences(text, nameSet))
                        {
                            blockingReferences[reference.Key].Add(
                                assetPath + " (prefab override " + reference.Description + ")");
                        }
                    }
                }
                else if (extension == ".anim")
                {
                    foreach (SerializedShieldYamlKeyReference reference in
                        SerializedShieldYamlRewriter.FindAnimationBindingReferences(text, script.ScriptGuid, nameSet))
                    {
                        blockingReferences[reference.Key].Add(assetPath + " (" + reference.Description + ")");
                    }
                }
                else if (extension == ".preset")
                {
                    if (SerializedShieldYamlRewriter.ContainsGuid(text, script.ScriptGuid))
                    {
                        foreach (SerializedShieldYamlKeyReference reference in
                            SerializedShieldYamlRewriter.FindPropertyPathReferences(text, nameSet))
                        {
                            blockingReferences[reference.Key].Add(assetPath + " (preset " + reference.Description + ")");
                        }
                    }
                }
            }

            return blockingReferences;
        }

        private static int RemoveFormerlySerializedAsFromScript(
            string scriptPath,
            ICollection<string> namesToRemove,
            SerializedShieldMigrationResult result)
        {
            try
            {
                string absolutePath = SerializedShieldPathUtility.ToPhysicalPath(scriptPath);

                if (absolutePath == null || !File.Exists(absolutePath))
                {
                    result.Warnings.Add("Script file not found for attribute removal: " + scriptPath);
                    return 0;
                }

                SerializedShieldTextFileContent content = SerializedShieldTextFileUtility.Read(absolutePath);
                int removedCount;
                string updatedText = SerializedShieldScriptAnalyzer.RemoveFormerlySerializedAsAttributes(
                    content.Text,
                    namesToRemove,
                    out removedCount);

                if (removedCount == 0)
                {
                    return 0;
                }

                SerializedShieldTextFileUtility.Write(absolutePath, content, updatedText);
                AssetDatabase.ImportAsset(scriptPath);
                return removedCount;
            }
            catch (Exception exception)
            {
                result.Warnings.Add(string.Format(
                    "Could not remove attributes from '{0}': {1}",
                    scriptPath,
                    exception.Message));
                return 0;
            }
        }

        private static void TryCheckout(List<string> assetPaths, SerializedShieldMigrationResult result)
        {
            try
            {
                if (!Provider.enabled || !Provider.isActive)
                {
                    return;
                }

                Task checkoutTask = Provider.Checkout(assetPaths.ToArray(), CheckoutMode.Asset);
                checkoutTask.Wait();
            }
            catch (Exception exception)
            {
                result.Warnings.Add("Version control checkout failed: " + exception.Message);
            }
        }

        private static SerializedShieldMigrationResult Abort(SerializedShieldMigrationResult result, string reason)
        {
            result.Aborted = true;
            result.AbortReason = reason;
            WriteMigrationLog(result);
            return result;
        }

        private static void SkipRemoval(SerializedShieldMigrationResult result, string reason)
        {
            result.AttributeRemovalSkipped = true;
            result.AttributeRemovalSkipReason = reason;
            result.Warnings.Add("FormerlySerializedAs attributes were NOT removed because " + reason);
        }

        /// <summary>
        /// Structured per-run log written next to the backup session (feature gap 9).
        /// </summary>
        private static void WriteMigrationLog(SerializedShieldMigrationResult result)
        {
            if (string.IsNullOrEmpty(result.BackupSessionPath))
            {
                return;
            }

            try
            {
                string sessionFolder = Path.GetDirectoryName(result.BackupSessionPath);

                if (sessionFolder == null || !Directory.Exists(sessionFolder))
                {
                    return;
                }

                StringBuilder log = new StringBuilder();
                log.AppendLine("---");
                log.AppendLine("time: " + DateTime.Now.ToString("u"));
                log.AppendLine("script: " + result.ScriptPath);
                log.AppendLine("aborted: " + result.Aborted + (result.Aborted ? " (" + result.AbortReason + ")" : string.Empty));
                log.AppendLine("targets: " + result.TargetAssetPaths.Count);
                log.AppendLine("textMigratedFiles: " + result.TextMigratedAssetCount);
                log.AppendLine("textMigratedKeys: " + result.TextMigratedFieldCount);
                log.AppendLine("reserialized: " + result.ReserializedAssetCount);
                log.AppendLine("attributesRemoved: " + result.RemovedAttributeCount
                    + (result.RemovedAttributeNames.Count > 0
                        ? " (" + string.Join(", ", result.RemovedAttributeNames.ToArray()) + ")"
                        : string.Empty));

                if (result.AttributeRemovalSkipped)
                {
                    log.AppendLine("attributeRemovalSkipped: " + result.AttributeRemovalSkipReason);
                }

                foreach (string keptName in result.KeptAttributeNames)
                {
                    log.AppendLine("keptAttribute: " + keptName);
                }

                foreach (string failedPath in result.FailedAssetPaths)
                {
                    log.AppendLine("failed: " + failedPath);
                }

                foreach (string warning in result.Warnings)
                {
                    log.AppendLine("warning: " + warning);
                }

                File.AppendAllText(Path.Combine(sessionFolder, "migration-log.txt"), log.ToString());
            }
            catch (Exception)
            {
                // Logging must never break the migration itself.
            }
        }

        private static Func<float, string, bool> CreateProgress(string stage)
        {
            return (progressValue, info) => EditorUtility.DisplayCancelableProgressBar(
                ProgressTitle,
                stage + ": " + info,
                progressValue);
        }
    }
}
