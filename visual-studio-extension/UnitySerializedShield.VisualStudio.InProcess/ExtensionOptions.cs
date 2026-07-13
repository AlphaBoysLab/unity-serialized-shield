using System;
using Microsoft.Win32;

namespace UnitySerializedShield.VisualStudio.InProcess
{
    /// <summary>
    /// User-facing kill switch for the extension, read from the registry:
    /// <c>HKCU\Software\UnitySerializedShield</c>, DWORD value <c>Enabled</c>
    /// (missing key/value means enabled). The value is cached briefly so the
    /// high-frequency workspace-changed path never hammers the registry, while a
    /// user toggling the switch still takes effect without restarting VS.
    /// </summary>
    internal static class ExtensionOptions
    {
        private const string RegistryPath = @"Software\UnitySerializedShield";
        private const string EnabledValueName = "Enabled";

        private static readonly object Gate = new object();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        private static bool cachedEnabled = true;
        private static System.Diagnostics.Stopwatch? cacheAge;

        public static bool IsEnabled
        {
            get
            {
                lock (Gate)
                {
                    if (cacheAge is null || cacheAge.Elapsed > CacheDuration)
                    {
                        cachedEnabled = ReadEnabledFromRegistry();
                        cacheAge = System.Diagnostics.Stopwatch.StartNew();
                    }

                    return cachedEnabled;
                }
            }
        }

        private static bool ReadEnabledFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key?.GetValue(EnabledValueName) is int enabled)
                    {
                        return enabled != 0;
                    }
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write($"Reading the Enabled option failed; defaulting to enabled: {exception.Message}");
            }

            return true;
        }
    }
}
