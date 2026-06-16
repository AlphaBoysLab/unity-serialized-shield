using System;
using System.Threading;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Process-wide signal that a Rename (F2 / Ctrl+R) command was just invoked.
    ///
    /// The command handler arms this when the user starts a rename; the workspace
    /// watcher only migrates single-document edits while the signal is recent, so
    /// manual character-by-character editing of a field name never triggers a
    /// migration. Inline rename can take a while to commit, hence a generous window.
    /// </summary>
    internal static class RenameSignal
    {
        private static long lastInvokedTicks;

        public static void MarkInvoked()
        {
            Interlocked.Exchange(ref lastInvokedTicks, DateTime.UtcNow.Ticks);
        }

        public static bool WasInvokedWithin(TimeSpan window)
        {
            var ticks = Interlocked.Read(ref lastInvokedTicks);

            if (ticks == 0)
            {
                return false;
            }

            return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) <= window;
        }
    }
}
