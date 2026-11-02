using Microsoft.Win32;

namespace ThreeFingerTabScroll;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights needed).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ThreeFingerTabScroll";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static bool IsEnabledSafe
    {
        get
        {
            try { return IsEnabled; } catch { return false; }
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private const string HandoffKey = @"Software\ThreeFingerTabScroll";
    private const string HandoffValue = "InstallerStartWithWindows";

    /// <summary>
    /// The install wizard records its "Start with Windows" checkbox in the registry (Velopack's installer
    /// can't pass arguments through to the app). Returns that choice once, removing it, or null if none.
    /// </summary>
    public static bool? TakeInstallerChoice()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HandoffKey, writable: true);
            if (key?.GetValue(HandoffValue) is not int value) return null;
            key.DeleteValue(HandoffValue, throwOnMissingValue: false);
            return value != 0;
        }
        catch
        {
            return null;
        }
    }

    private const string DeferLaunchValue = "InstallerDeferLaunch";

    /// <summary>
    /// The install wizard sets this before running Velopack's installer, which always starts the app when
    /// it finishes. The wizard opens the app itself from its Finish page instead (if the user wants), so the
    /// copy Velopack starts should quietly exit. Returns true once if the flag was set, removing it.
    /// </summary>
    public static bool TakeInstallerDeferFlag()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(HandoffKey, writable: true);
            if (key?.GetValue(DeferLaunchValue) is not int value) return false;
            key.DeleteValue(DeferLaunchValue, throwOnMissingValue: false);
            return value != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>If autostart is on but points at an old location (the .exe was moved), update it.</summary>
    public static void RefreshPathIfEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current &&
            !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, Command);
    }
}
