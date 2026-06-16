using System;
using System.IO;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Lightweight append-only diagnostics so behavior can be traced from a real
    /// Visual Studio session. Writes to
    /// <c>%LOCALAPPDATA%\UnitySerializedShield\InProcess.log</c>. Logging must
    /// never throw into the editor.
    /// </summary>
    internal static class DiagnosticLog
    {
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
                    File.AppendAllText(LogPath, $"{DateTimeOffset.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Diagnostics must never disrupt the editor.
            }
        }
    }
}
