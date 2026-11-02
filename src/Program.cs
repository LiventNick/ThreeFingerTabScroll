using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Velopack;

namespace ThreeFingerTabScroll;

internal static class Program
{
    private const string AppName = "Three-Finger Tab Scroll";

    /// <summary>
    /// Tray app entry point.
    ///   --debug    open a console window that logs gestures and tab switches
    ///   --frames   (with --debug) also log every touchpad frame
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Must run first: handles install/update/uninstall events from the installer and exits for those.
        bool justInstalled = false;
        VelopackApp.Build()
            .OnFirstRun(_ => justInstalled = true)
            .OnRestarted(v => Updater.JustUpdatedTo = v.ToString())
            .OnBeforeUninstallFastCallback(_ => { try { Autostart.Set(false); } catch { } })
            .Run();

        // Started by Velopack's installer while the install wizard is still open: the wizard's
        // "Open app now" checkbox decides whether to open the app, so this copy exits quietly.
        bool deferredByInstaller = Autostart.TakeInstallerDeferFlag();
        if (deferredByInstaller && !args.Contains("--from-installer", StringComparer.OrdinalIgnoreCase))
            return;

        bool debug = args.Contains("--debug", StringComparer.OrdinalIgnoreCase);
        bool frames = args.Contains("--frames", StringComparer.OrdinalIgnoreCase);

        // The install wizard's "Start with Windows" checkbox, if the app was just installed with it.
        bool? installerAutostart = Autostart.TakeInstallerChoice();
        if (installerAutostart is { } autostart)
        {
            try { Autostart.Set(autostart); } catch { /* registry unavailable; not fatal */ }
        }

        if (args.Contains("--list-tabs", StringComparer.OrdinalIgnoreCase))
        {
            ListTabs();
            return;
        }

        // Two instances would each send Ctrl+Tab, doubling every step.
        using var mutex = new Mutex(true, @"Local\ThreeFingerTabScroll.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            // Opening the app again (e.g. from the Start menu) brings up the running copy's Settings.
            if (!debug && EventWaitHandle.TryOpenExisting(TrayApp.ShowSettingsEventName, out var showSettings))
            {
                AllowSetForegroundWindow(-1); // ASFW_ANY: let the running copy bring its window to the front
                showSettings.Set();
                showSettings.Dispose();
                return;
            }
            MessageBox.Show($"{AppName} is already running (see the system tray).\n\n" +
                            "To run with --debug, exit the tray instance first.",
                            AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Action<string>? log = null;
        if (debug)
        {
            AllocConsole();
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = $"{AppName} - debug";
            log = msg => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {msg}");
            log($"Debug mode. Settings file: {Settings.FilePath}");
            log("Close this window or use the tray icon > Exit to quit.");
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => OnError(e.Exception, log);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            TabSwitcher.ReleaseModifiers();
            if (e.ExceptionObject is Exception ex) WriteErrorLog(ex);
        };

        var settings = Settings.Load(log);
        if (!settings.FirstRunMessageShown || justInstalled || installerAutostart != null)
        {
            ShowWelcome(askAboutAutostart: installerAutostart == null);
            settings.FirstRunMessageShown = true;
            try { settings.Save(); } catch (Exception ex) { log?.Invoke($"Could not save settings: {ex.Message}"); }
        }

        try
        {
            using var app = new TrayApp(settings, log, debug && frames);
            Application.Run(app);
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
            MessageBox.Show($"{AppName} could not start:\n\n{ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            TabSwitcher.ReleaseModifiers();
        }
    }

    /// <summary>Welcome window on first run / right after installing; applies the "Start with Windows" choice.</summary>
    private static void ShowWelcome(bool askAboutAutostart)
    {
        using var welcome = new WelcomeForm(askAboutAutostart);
        welcome.ShowDialog();
        if (askAboutAutostart)
        {
            try { Autostart.Set(welcome.StartWithWindows); } catch { /* registry unavailable; not fatal */ }
        }
    }

    private static void OnError(Exception ex, Action<string>? log)
    {
        TabSwitcher.ReleaseModifiers();
        log?.Invoke($"Error: {ex}");
        WriteErrorLog(ex);
    }

    private static void WriteErrorLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Settings.Directory);
            File.AppendAllText(Path.Combine(Settings.Directory, "error.log"), $"[{DateTime.Now:u}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    /// <summary>Debug helper: prints the tabs of every open browser window (--list-tabs).</summary>
    private static void ListTabs()
    {
        if (!AttachConsole(-1)) AllocConsole();
        var browsers = new ForegroundBrowser(Settings.Load().BrowserProcesses.Concat(ForegroundBrowser.DefaultProcesses));
        int found = 0;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || !browsers.IsBrowserWindow(hwnd, out var process)) return true;
            var tabs = TabStripReader.Read(hwnd, msg => Console.WriteLine($"  {msg}"));
            if (tabs == null) return true;
            found++;
            Console.WriteLine($"{process} window 0x{hwnd:X} (active tab site: {tabs.ActiveSite ?? "unknown"}):");
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs.Tabs[i];
                Console.WriteLine($"  {(i == tabs.ActiveIndex ? ">" : " ")} {i + 1,3}. {t.Title}" +
                                  (t.Memory != null ? $"  ({(t.HighMemory ? "high " : "")}{t.Memory})" : "") +
                                  (t.Bounds.IsEmpty ? "  [off screen]" : $"  [{t.Bounds.X},{t.Bounds.Y} {t.Bounds.Width}x{t.Bounds.Height}]"));
            }
            return true;
        }, IntPtr.Zero);
        if (found == 0) Console.WriteLine("No browser windows with readable tabs found.");
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}

/// <summary>Formats a touch frame for the debug console.</summary>
internal static class DebugFormat
{
    public static string Frame(TouchFrame frame)
    {
        var sb = new StringBuilder();
        sb.Append($"contacts={frame.Contacts.Count} down={frame.DownCount} reports={frame.ReportCount} |");

        double sumX = 0;
        int down = 0;
        foreach (var c in frame.Contacts)
        {
            sb.Append($" [id{c.Id} {(c.Tip ? "TIP" : "up ")}{(c.Confident ? "" : " palm")} x={c.X,5} y={c.Y,5}]");
            if (c.IsDown) { sumX += c.X; down++; }
        }

        if (down > 0)
            sb.Append($" | avgX={frame.Device.NormalizeX(sumX / down):F3}");
        return sb.ToString();
    }
}
