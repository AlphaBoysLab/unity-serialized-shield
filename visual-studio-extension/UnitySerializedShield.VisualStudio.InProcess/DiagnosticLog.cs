using System;
using System.IO;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Lightweight append-only diagnostics so behavior can be traced from a real
    /// Visual Studio session. Writes to
    /// <c>%LOCALAPPDATA%\UnitySerializedShield\InProcess.log</c>, rotating to
    /// <c>InProcess.old.log</c> when the file grows past a fixed bound so the log
    /// can never fill the disk. Logging must never throw into the editor.
    /// </summary>
    internal static class DiagnosticLog
    {
        private const long MaxLogSizeBytes = 2 * 1024 * 1024;

        private static readonly object Gate = new object();

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnitySerializedShield",
            "InProcess.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    RotateIfTooLarge();
                    File.AppendAllText(LogPath, $"{DateTimeOffset.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Diagnostics must never disrupt the editor.
            }
        }

        private static void RotateIfTooLarge()
        {
            var info = new FileInfo(LogPath);

            if (!info.Exists || info.Length < MaxLogSizeBytes)
            {
                return;
            }

            var oldPath = Path.ChangeExtension(LogPath, ".old.log");
            File.Delete(oldPath);
            File.Move(LogPath, oldPath);
        }
    }
}
