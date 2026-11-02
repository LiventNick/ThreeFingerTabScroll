using System.Diagnostics;
using Microsoft.Win32;

namespace ThreeFingerTabScroll;

/// <summary>Checks Windows' own three-finger swipe setting, which clashes with this app's gesture.</summary>
internal static class WindowsGestureCheck
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    /// <summary>
    /// True if Windows' three-finger swipe is (probably) still on. "Swipes: Nothing" stores
    /// ThreeFingerSlideEnabled = 0; any other value, or no value at all (Windows' default of switching
    /// apps), means Windows will also react to the swipe.
    /// </summary>
    public static bool IsWindowsSwipeOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue("ThreeFingerSlideEnabled") is not int value || value != 0;
        }
        catch
        {
            return false; // can't tell; don't nag
        }
    }

    public static void OpenTouchpadSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true }); }
        catch { /* settings app unavailable */ }
    }
}
