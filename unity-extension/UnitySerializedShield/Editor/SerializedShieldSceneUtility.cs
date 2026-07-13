using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#if !UNITY_2021_2_OR_NEWER
using UnityEditor.Experimental.SceneManagement;
#endif

namespace AlphaBoysLab.SerializedShield.Editor
{
    /// <summary>
    /// Open-scene and Prefab Mode guards shared by migration and backup restore
    /// (audit U-C5 / U-M9). Editing serialized files on disk while a stale copy sits in
    /// memory lets a later Ctrl+S silently revert the migration, so callers must:
    /// 1. Refuse to run while Prefab Mode is open.
    /// 2. Refuse to run while any affected open scene is dirty (a "Don't Save" answer
    ///    leaves the scene dirty, which must abort, not proceed).
    /// 3. Reload affected open scenes from disk after files change.
    /// </summary>
    internal static class SerializedShieldSceneUtility
    {
        public static bool IsPrefabStageOpen()
        {
            return PrefabStageUtility.GetCurrentPrefabStage() != null;
        }

        public static List<string> GetLoadedScenePaths()
        {
            List<string> scenePaths = new List<string>();

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);

                if (!string.IsNullOrEmpty(scene.path))
                {
                    scenePaths.Add(scene.path);
                }
            }

            return scenePaths;
        }

        /// <summary>
        /// Returns the paths of loaded scenes that are BOTH dirty and part of
        /// <paramref name="affectedAssetPaths"/>. Any entry means the operation must abort.
        /// </summary>
        public static List<string> GetDirtyAffectedScenePaths(ICollection<string> affectedAssetPaths)
        {
            List<string> dirtyScenePaths = new List<string>();

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);

                if (!scene.isDirty)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path) || affectedAssetPaths.Contains(scene.path))
                {
                    // Untitled dirty scenes are included: they cannot be reloaded from disk
                    // safely, so they block the operation.
                    dirtyScenePaths.Add(string.IsNullOrEmpty(scene.path) ? "(untitled scene)" : scene.path);
                }
            }

            return dirtyScenePaths;
        }

        /// <summary>
        /// Reloads all open scenes from disk when any of them is in
        /// <paramref name="changedAssetPaths"/>. Returns false (with a message) when the
        /// reload could not be performed automatically.
        /// </summary>
        public static bool ReloadOpenScenesIfAffected(ICollection<string> changedAssetPaths, out string warning)
        {
            warning = null;

            bool anyAffected = GetLoadedScenePaths().Any(changedAssetPaths.Contains);

            if (!anyAffected)
            {
                return true;
            }

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            SceneSetup[] restorableSetup = setup.Where(entry => !string.IsNullOrEmpty(entry.path)).ToArray();

            if (restorableSetup.Length == 0)
            {
                warning = "Open scenes were changed on disk but could not be reloaded automatically. Reopen them before saving.";
                return false;
            }

            try
            {
                EditorSceneManager.RestoreSceneManagerSetup(restorableSetup);
            }
            catch (Exception exception)
            {
                warning = "Failed to reload open scenes from disk: " + exception.Message
                    + " Reopen the affected scenes before saving.";
                return false;
            }

            if (restorableSetup.Length != setup.Length)
            {
                warning = "An untitled scene was open and was closed while reloading migrated scenes from disk.";
                return false;
            }

            return true;
        }
    }
}
