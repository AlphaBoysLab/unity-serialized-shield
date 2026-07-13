using System;
using System.Diagnostics;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// Process-wide, ONE-SHOT signal that a Rename Symbol (F2 / Ctrl+R,R) command
    /// was just invoked.
    ///
    /// The command observers arm this when the user starts a rename, optionally
    /// recording the identifier under the caret so the workspace watcher can
    /// verify that a detected rename is about THAT symbol. The watcher disarms
    /// the signal as soon as a migration is applied (one-shot), and the command
    /// observers disarm it on Escape/Undo/Redo — so manual typing after a rename
    /// can never keep re-triggering attribute insertion, and undoing a rename is
    /// never re-detected as a fresh rename.
    ///
    /// Time is measured with a monotonic <see cref="Stopwatch"/> clock
    /// (Environment.TickCount64 does not exist on net472), so wall-clock jumps
    /// cannot stretch or shrink the window.
    /// </summary>
    internal static class RenameSignal
    {
        private static readonly object Gate = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        // Both observers (MEF command handler and DTE command events) see the same
        // command; within this interval a null-identifier arm must not wipe an
        // identifier captured by the other observer moments earlier.
        private static readonly TimeSpan DoubleArmMergeWindow = TimeSpan.FromSeconds(2);

        private static bool armed;
        private static TimeSpan armedAt;
        private static string? armedIdentifier;

        /// <summary>
        /// Arms the signal. <paramref name="identifier"/> is the identifier under
        /// the caret when the Rename command started (the symbol being renamed),
        /// or null when the observer could not determine it.
        /// </summary>
        public static void Arm(string? identifier)
        {
            lock (Gate)
            {
                var now = Clock.Elapsed;

                if (identifier is null && armed && now - armedAt <= DoubleArmMergeWindow)
                {
                    // Second observer of the same command: refresh the window but
                    // keep the better (non-null) identifier already captured.
                    armedAt = now;
                    return;
                }

                armed = true;
                armedAt = now;
                armedIdentifier = identifier;
            }
        }

        /// <summary>
        /// Disarms the signal. Called after a migration is applied (one-shot
        /// consumption) and when Escape/Undo/Redo indicates the rename was
        /// cancelled or reverted.
        /// </summary>
        public static void Disarm()
        {
            lock (Gate)
            {
                armed = false;
                armedIdentifier = null;
            }
        }

        /// <summary>True if the signal was armed within <paramref name="window"/>.</summary>
        public static bool IsArmedWithin(TimeSpan window)
        {
            lock (Gate)
            {
                return armed && Clock.Elapsed - armedAt <= window;
            }
        }

        /// <summary>
        /// The identifier the rename was invoked on, or null when unknown.
        /// Only meaningful while the signal is armed.
        /// </summary>
        public static string? ArmedIdentifier
        {
            get
            {
                lock (Gate)
                {
                    return armed ? armedIdentifier : null;
                }
            }
        }
    }
}
