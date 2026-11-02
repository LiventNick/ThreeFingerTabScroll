using static ThreeFingerTabScroll.Native;

namespace ThreeFingerTabScroll;

/// <summary>Checks whether the foreground window belongs to one of the configured browser processes.</summary>
internal sealed class ForegroundBrowser
{
    public static readonly string[] DefaultProcesses =
    {
        // Chromium-based
        "chrome.exe", "msedge.exe", "brave.exe", "vivaldi.exe", "opera.exe",
        // Firefox-based
        "firefox.exe", "librewolf.exe", "waterfox.exe", "floorp.exe", "zen.exe",
    };

    /// <summary>
    /// Firefox-based browsers: Ctrl+Tab there may follow "recently used" order (a Firefox option), so
    /// these get Ctrl+PageDown / Ctrl+PageUp instead, which always move through tabs in strip order.
    /// </summary>
    private static readonly HashSet<string> PageKeyBrowsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "firefox.exe", "librewolf.exe", "waterfox.exe", "floorp.exe", "zen.exe", "palemoon.exe", "basilisk.exe",
    };

    public static bool UsesPageKeys(string? processName) => processName != null && PageKeyBrowsers.Contains(processName);

    private HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly char[] _pathBuffer = new char[1024];

    // Cache the last lookup so repeated checks on the same window are cheap.
    private IntPtr _lastHwnd;
    private uint _lastPid;
    private string? _lastName;

    public ForegroundBrowser(IEnumerable<string> processNames) => SetProcesses(processNames);

    /// <summary>Accepts names with or without ".exe" (e.g. "chrome" or "chrome.exe").</summary>
    public void SetProcesses(IEnumerable<string> processNames)
    {
        _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in processNames)
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            _names.Add(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
        }
    }

    /// <summary>True if the foreground window is a configured browser. Always reports the process name it saw.</summary>
    public bool IsForeground(out string? processName) => IsForeground(out processName, out _);

    public bool IsForeground(out string? processName, out IntPtr hwnd)
    {
        hwnd = GetForegroundWindow();
        return IsBrowserWindow(hwnd, out processName);
    }

    public bool IsBrowserWindow(IntPtr hwnd, out string? processName)
    {
        processName = GetProcessName(hwnd);
        return processName != null && _names.Contains(processName);
    }

    private string? GetProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;
        if (hwnd == _lastHwnd && pid == _lastPid) return _lastName;

        string? name = null;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process != IntPtr.Zero)
        {
            try
            {
                uint size = (uint)_pathBuffer.Length;
                if (QueryFullProcessImageName(process, 0, _pathBuffer, ref size))
                    name = Path.GetFileName(new string(_pathBuffer, 0, (int)size));
            }
            finally
            {
                CloseHandle(process);
            }
        }

        _lastHwnd = hwnd;
        _lastPid = pid;
        _lastName = name;
        return name;
    }
}
