using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AlphaBoysLab.SerializedShield.Editor
{
    internal static class SerializedShieldPathUtility
    {
        public static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
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

        public static string ToAssetPath(string absolutePath)
        {
            string normalizedFullPath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string normalizedAssetsPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');

            if (!normalizedFullPath.StartsWith(normalizedAssetsPath))
            {
                return absolutePath;
            }

            return "Assets" + normalizedFullPath.Substring(normalizedAssetsPath.Length);
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
