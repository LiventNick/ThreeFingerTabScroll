using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ThreeFingerTabScroll;

/// <summary>Owns the tray icon, menu and settings, and wires touchpad input to tab switching.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings;
    private readonly Action<string>? _log;
    private readonly RawInputWindow _input;
    private readonly GestureDetector _detector;
    private readonly ForegroundBrowser _browsers;
    private readonly TabPreview _preview;

    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _modeItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly IntPtr _iconOnHandle, _iconOffHandle;
    private readonly Icon _iconOn, _iconOff;

    private readonly ToolStripMenuItem _updateItem;
    private readonly Updater _updater;
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly SynchronizationContext _ui;

    private SettingsForm? _settingsForm;
    private SwipeCalibrator? _calibrator;

    // Live mode without wrap-around: where we are in the tab strip, so steps stop at the ends.
    private int _livePosition = -1, _liveCount, _liveGeneration;
    private bool _liveStepped;

    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    public TrayApp(Settings settings, Action<string>? log, bool logFrames)
    {
        _settings = settings;
        _log = log;

        _browsers = new ForegroundBrowser(settings.BrowserProcesses);
        _preview = new TabPreview(_browsers, log); // creates hidden windows, which installs the UI sync context
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _updater = new Updater(log);

        _detector = new GestureDetector { Log = log };
        _detector.Started += () =>
        {
            if (_settings.Mode != SwitchMode.Live)
                _preview.Begin(hoverCardStyle: _settings.Mode == SwitchMode.HoverCard);
            else if (!_settings.WrapAround)
                BeginLiveTracking();
        };
        _detector.Locked += () => _preview.Lock();
        _detector.Step += OnStep;
        _detector.Ended += () =>
        {
            _preview.End();
            _livePosition = -1;
            _liveGeneration++;
        };

        _input = new RawInputWindow(log);
        _input.FrameReceived += frame =>
        {
            if (logFrames) _log?.Invoke(DebugFormat.Frame(frame));
            if (_calibrator != null) _calibrator.OnFrame(frame);
            else if (_settings.Enabled) _detector.OnFrame(frame);
        };

        (_iconOn, _iconOnHandle) = CreateIcon(enabled: true);
        (_iconOff, _iconOffHandle) = CreateIcon(enabled: false);

        _enabledItem = new ToolStripMenuItem("Enabled", null, (_, _) => SetEnabled(!_settings.Enabled));
        _modeItem = new ToolStripMenuItem("Mode");
        foreach (var mode in Enum.GetValues<SwitchMode>())
        {
            var m = mode;
            _modeItem.DropDownItems.Add(new ToolStripMenuItem(m.ShortLabel(), null, (_, _) => SetMode(m)) { Tag = m });
        }
        _autostartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => SetAutostart(!Autostart.IsEnabledSafe));
        _updateItem = new ToolStripMenuItem("Update", null, (_, _) => _ = InstallUpdateAsync()) { Visible = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"Three-Finger Tab Scroll {_updater.CurrentVersion}") { Enabled = false });
        menu.Items.Add(_updateItem);
        _updateItem.Font = new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_modeItem);
        menu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Edit settings file...", null, (_, _) => OpenSettingsFile()));
        menu.Items.Add(new ToolStripMenuItem("Reload settings file", null, (_, _) => ReloadSettings()));
        menu.Items.Add(new ToolStripMenuItem("Open settings and log folder", null, (_, _) => OpenDataFolder()));
        menu.Items.Add(new ToolStripMenuItem("Check for updates...", null, (_, _) => _ = CheckForUpdatesAsync(interactive: true)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();

        try { Autostart.RefreshPathIfEnabled(); } catch { /* registry unavailable; not fatal */ }

        ApplySettings();
        ListenForSecondLaunch();

        // Startup notice, a moment after the tray icon appears (Windows can drop notifications sent too early).
        var noticeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        noticeTimer.Tick += (_, _) =>
        {
            noticeTimer.Dispose();
            ShowStartupNotice();
        };
        noticeTimer.Start();

        // Background update checks: shortly after startup, then every few hours.
        _updateTimer.Interval = (int)FirstUpdateCheck.TotalMilliseconds;
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
            if (_settings.Updates != UpdateMode.Off) _ = CheckForUpdatesAsync(interactive: false);
        };
        if (_updater.CanUpdate) _updateTimer.Start();
    }

    // ---- Notifications ------------------------------------------------------------

    private Action? _balloonClick;

    /// <summary>Shows a Windows notification from the tray icon; <paramref name="onClick"/> runs if it's clicked.</summary>
    private void Notify(string title, string text, ToolTipIcon icon, Action? onClick = null, int timeoutMs = 10000)
    {
        _balloonClick = onClick;
        _tray.ShowBalloonTip(timeoutMs, title, text, icon);
    }

    /// <summary>At most one notice at startup, most important first.</summary>
    private void ShowStartupNotice()
    {
        if (!_input.HasTouchpad)
        {
            _log?.Invoke("notice: no precision touchpad");
            Notify("No precision touchpad found",
                "Three-Finger Tab Scroll needs a Precision Touchpad, and none was found. Click for help.",
                ToolTipIcon.Warning,
                () => OpenUrl("https://github.com/LiventNick/ThreeFingerTabScroll#troubleshooting"));
        }
        else if (_settings.WarnAboutWindowsSwipe && WindowsGestureCheck.IsWindowsSwipeOn())
        {
            _log?.Invoke("notice: Windows three-finger swipe is still on");
            Notify("Windows' three-finger swipe is on",
                "It will switch apps while you switch tabs. Click to open touchpad settings and set Swipes to \"Nothing\".",
                ToolTipIcon.Warning,
                WindowsGestureCheck.OpenTouchpadSettings);
        }
        else if (Updater.JustUpdatedTo is { } version)
        {
            Notify("Three-Finger Tab Scroll updated", $"You're now on version {version}.", ToolTipIcon.Info, timeoutMs: 5000);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no default browser */ }
    }

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Could not open folder: {ex.Message}");
        }
    }

    // ---- Second launch ------------------------------------------------------------

    /// <summary>Name of the event a second launch of the app signals to make this one open Settings.</summary>
    public const string ShowSettingsEventName = @"Local\ThreeFingerTabScroll.ShowSettings";

    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsWait;

    private void ListenForSecondLaunch()
    {
        try
        {
            _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
            _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(_showSettingsEvent,
                (_, _) => _ui.Post(_ => ShowSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Second-launch signal unavailable: {ex.Message}");
        }
    }

    // ---- Live mode: stop at the first/last tab -----------------------------------

    /// <summary>Reads the tab strip in the background so live steps can stop at the ends.</summary>
    private void BeginLiveTracking()
    {
        _livePosition = -1;
        _liveStepped = false;
        if (!_browsers.IsForeground(out _, out var hwnd)) return;

        int generation = ++_liveGeneration;
        Task.Run(() =>
        {
            var tabs = TabStripReader.Read(hwnd, _log);
            _ui.Post(_ =>
            {
                // If a step was already sent before the list arrived, the list may be stale: don't use it.
                if (generation != _liveGeneration || _liveStepped || tabs == null) return;
                _livePosition = tabs.ActiveIndex;
                _liveCount = tabs.Count;
            }, null);
        });
    }

    private void OnStep(int direction)
    {
        if (_settings.Mode != SwitchMode.Live)
        {
            _preview.Step(direction); // does nothing if the gesture didn't start over a browser
            return;
        }

        string label = direction > 0 ? "NEXT tab" : "PREV tab";
        if (!_browsers.IsForeground(out var process))
        {
            _log?.Invoke($"{label} skipped: foreground is {process ?? "(unknown)"}");
            return;
        }

        // Without wrap-around, stop at the ends (browsers themselves would wrap Ctrl+Tab).
        if (!_settings.WrapAround && _livePosition >= 0)
        {
            int next = _livePosition + direction;
            if (next < 0 || next >= _liveCount)
            {
                _log?.Invoke($"{label} skipped: already at the {(direction > 0 ? "last" : "first")} tab");
                return;
            }
            _livePosition = next;
        }
        _liveStepped = true;

        var result = TabSwitcher.Switch(forward: direction > 0, usePageKeys: ForegroundBrowser.UsesPageKeys(process));
        _log?.Invoke(result == TabSwitcher.Result.Sent ? $"{label} -> {process}" : $"{label} not sent: {result}");
    }

    // ---- Updates ------------------------------------------------------------------

    public Updater Updater => _updater;

    /// <summary>Checks for an update. Interactive checks always report the result in a message box.</summary>
    public async Task CheckForUpdatesAsync(bool interactive)
    {
        if (!_updater.CanUpdate)
        {
            if (interactive)
                MessageBox.Show(_updater.UnavailableReason, "Three-Finger Tab Scroll", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var update = await _updater.CheckAsync();
        RefreshMenu();
        _settingsForm?.RefreshUpdateStatus();

        if (update == null)
        {
            if (interactive)
                MessageBox.Show($"You're up to date (version {_updater.CurrentVersion}).", "Three-Finger Tab Scroll",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (interactive)
        {
            var answer = MessageBox.Show($"Version {_updater.AvailableVersion} is available (you have {_updater.CurrentVersion}).\n\n" +
                                         "Install it now? The app restarts in a few seconds.",
                "Three-Finger Tab Scroll", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes) await InstallUpdateAsync();
        }
        else if (_settings.Updates == UpdateMode.Automatic)
        {
            await InstallWhenIdleAsync();
        }
        else
        {
            Notify("Update available",
                $"Version {_updater.AvailableVersion} of Three-Finger Tab Scroll is ready. Click here to install it.",
                ToolTipIcon.Info, () => _ = InstallUpdateAsync());
        }
    }

    /// <summary>For automatic updates: wait until no gesture is in progress, then install.</summary>
    private async Task InstallWhenIdleAsync()
    {
        if (_waitingToInstall) return;
        _waitingToInstall = true;
        try
        {
            while (_detector.IsActive || _settingsForm != null)
                await Task.Delay(TimeSpan.FromSeconds(5));
            await InstallUpdateAsync(silent: true);
        }
        finally
        {
            _waitingToInstall = false;
        }
    }

    private bool _waitingToInstall;

    /// <summary>
    /// Downloads and installs the available update right now, then restarts the app.
    /// Shows a progress window unless <paramref name="silent"/> (used for automatic updates).
    /// </summary>
    public async Task InstallUpdateAsync(bool silent = false)
    {
        if (_updater.Available == null || _updater.IsBusy) return;

        UpdateProgressForm? progress = null;
        if (!silent)
        {
            _settingsForm?.Close();
            progress = new UpdateProgressForm(_updater.AvailableVersion ?? "");
            progress.Show();
        }

        var (ok, error) = await _updater.InstallAsync(
            beforeRestart: () =>
            {
                progress?.SetInstalling();
                PrepareForRestart();
            },
            progress: progress == null ? null : progress.SetProgress);

        progress?.Close();
        if (!ok && !silent)
            MessageBox.Show($"The update couldn't be installed:\n{error}", "Three-Finger Tab Scroll",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Cleans up right before the updater closes this process.</summary>
    private void PrepareForRestart()
    {
        _settingsForm?.Close();
        _detector.Reset();
        TabSwitcher.ReleaseModifiers();
        _tray.Visible = false; // otherwise a dead icon lingers in the tray until hovered
    }

    // ---- Settings ---------------------------------------------------------------

    private void ApplySettings()
    {
        _detector.StepFraction = _settings.StepFraction;
        _detector.Invert = _settings.InvertDirection;
        _preview.WrapAround = _settings.WrapAround;
        _browsers.SetProcesses(_settings.BrowserProcesses);
        if (!_settings.Enabled) _detector.Reset();

        _tray.Icon = _settings.Enabled ? _iconOn : _iconOff;
        _tray.Text = _settings.Enabled ? "Three-Finger Tab Scroll" : "Three-Finger Tab Scroll (disabled)";
        _log?.Invoke($"settings: enabled={_settings.Enabled} mode={_settings.Mode} tabs/swipe={_settings.TabsPerSwipe} " +
                     $"swipe width={_settings.SwipeWidthPercent}% -> step={_settings.StepFraction:P1} invert={_settings.InvertDirection} " +
                     $"browsers={string.Join(",", _settings.BrowserProcesses)}");
    }

    /// <summary>Applies the current settings and writes them to disk. Used by the settings window.</summary>
    public void ApplyAndSave()
    {
        ApplySettings();
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Could not save settings: {ex.Message}");
        }
    }

    /// <summary>While a calibrator is set, touch frames go to it and no tabs are switched.</summary>
    public void SetCalibrator(SwipeCalibrator? calibrator)
    {
        _calibrator = calibrator;
        _detector.Reset();
        _log?.Invoke(calibrator != null ? "measuring swipe width..." : "measuring finished");
    }

    public void SetAutostart(bool enabled)
    {
        try
        {
            Autostart.Set(enabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not change the startup setting:\n{ex.Message}", "Three-Finger Tab Scroll",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        ApplyAndSave();
    }

    private void SetMode(SwitchMode mode)
    {
        _settings.Mode = mode;
        ApplyAndSave();
        _settingsForm?.Close(); // it would show the old mode
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings, this);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OpenSettingsFile()
    {
        ApplyAndSave(); // make sure the file exists
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Settings.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Could not open settings file: {ex.Message}");
        }
    }

    private void ReloadSettings()
    {
        _settings.CopyFrom(Settings.Load(_log));
        ApplySettings();
        _settingsForm?.Close(); // it would show stale values
    }

    private void RefreshMenu()
    {
        _enabledItem.Checked = _settings.Enabled;
        foreach (ToolStripMenuItem item in _modeItem.DropDownItems)
            item.Checked = item.Tag is SwitchMode m && m == _settings.Mode;
        _updateItem.Visible = _updater.Available != null;
        _updateItem.Text = $"Install update (version {_updater.AvailableVersion})";
        _autostartItem.Checked = Autostart.IsEnabledSafe;
    }

    protected override void ExitThreadCore()
    {
        _showSettingsWait?.Unregister(null);
        _showSettingsEvent?.Dispose();
        _updateTimer.Dispose();
        _settingsForm?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _input.Dispose();
        _preview.Dispose();
        TabSwitcher.ReleaseModifiers();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconOn.Dispose();
            _iconOff.Dispose();
        }
        DestroyIcon(_iconOnHandle);
        DestroyIcon(_iconOffHandle);
        base.Dispose(disposing);
    }

    // ---- Icon -----------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Draws the tray icon: a rounded square with a left/right arrow. Gray when disabled.</summary>
    private static (Icon icon, IntPtr handle) CreateIcon(bool enabled)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float s = size;
            var background = enabled ? Color.FromArgb(26, 115, 232) : Color.FromArgb(128, 128, 128);
            using (var path = RoundedRect(new RectangleF(0.5f, 0.5f, s - 1, s - 1), s * 0.22f))
            using (var brush = new SolidBrush(background))
                g.FillPath(brush, path);

            // Double-headed horizontal arrow.
            float mid = s / 2f, left = s * 0.18f, right = s * 0.82f, head = s * 0.22f;
            using var pen = new Pen(Color.White, Math.Max(1.5f, s * 0.11f)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
            g.DrawLine(pen, left + head * 0.6f, mid, right - head * 0.6f, mid);
            using var white = new SolidBrush(Color.White);
            g.FillPolygon(white, new[] { new PointF(left, mid), new PointF(left + head, mid - head), new PointF(left + head, mid + head) });
            g.FillPolygon(white, new[] { new PointF(right, mid), new PointF(right - head, mid - head), new PointF(right - head, mid + head) });
        }

        IntPtr handle = bmp.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
