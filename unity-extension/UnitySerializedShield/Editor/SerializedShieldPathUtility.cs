using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlphaBoysLab.SerializedShield.Editor
{
    internal static class SerializedShieldPathUtility
    {
        public static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        /// <summary>
        /// Resolves a Unity asset path ("Assets/..." or "Packages/...") to a physical
        /// file path. Uses FileUtil.GetPhysicalPath when available so registry/cached
        /// package paths resolve correctly (audit U-H3); otherwise falls back to a
        /// project-root join which covers Assets/ and embedded packages.
        /// </summary>
        public static string ToPhysicalPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

#if UNITY_2021_2_OR_NEWER
            try
            {
                string physicalPath = FileUtil.GetPhysicalPath(assetPath);

                if (!string.IsNullOrEmpty(physicalPath))
                {
                    return Path.GetFullPath(physicalPath);
                }
            }
            catch (Exception)
            {
                // Fall through to the manual join below.
            }
#endif
            return ToAbsolutePath(assetPath);
        }

        public static string ToAbsolutePath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return Path.GetFullPath(assetPath);
            }

            string normalizedAssetPath = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(ProjectRoot, normalizedAssetPath));
        }

        /// <summary>
        /// Converts a physical path back to a Unity asset path. Returns null when the
        /// path cannot be mapped, so callers must handle the failure loudly instead of
        /// passing an absolute path to AssetDatabase APIs that silently no-op on it
        /// (audit U-M11).
        /// </summary>
        public static string ToAssetPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            string normalizedFullPath = Path.GetFullPath(absolutePath).Replace('\\', '/');

#if UNITY_2021_2_OR_NEWER
            try
            {
                string logicalPath = FileUtil.GetLogicalPath(normalizedFullPath);

                if (!string.IsNullOrEmpty(logicalPath) && !Path.IsPathRooted(logicalPath))
                {
                    return logicalPath;
                }
            }
            catch (Exception)
            {
                // Fall through to the manual mapping below.
            }
#endif
            string normalizedProjectRoot = Path.GetFullPath(ProjectRoot).Replace('\\', '/').TrimEnd('/');

            // Asset paths are compared case-insensitively: Windows and default macOS
            // volumes are case-insensitive, and a drive-letter case mismatch previously
            // leaked absolute paths into AssetDatabase calls.
            if (normalizedFullPath.StartsWith(normalizedProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = normalizedFullPath.Substring(normalizedProjectRoot.Length + 1);

                if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    || relativePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    return relativePath;
                }
            }

            return null;
        }

        public static string BuildSafeBackupFileName(string assetPath)
        {
            string extension = Path.GetExtension(assetPath);
            string safeName = assetPath.Replace('\\', '_').Replace('/', '_').Replace(':', '_');

            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalidCharacter, '_');
            }

            if (safeName.Length > 90)
            {
                safeName = Path.GetFileNameWithoutExtension(assetPath);
            }

            return string.Format("{0}__{1}{2}", safeName, GetSha1(assetPath), extension);
        }

        private static string GetSha1(string text)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder builder = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
