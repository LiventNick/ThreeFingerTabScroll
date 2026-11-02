namespace ThreeFingerTabScroll;

/// <summary>Settings window: switching mode, tabs per swipe, swipe measurement, direction, autostart.</summary>
internal sealed class SettingsForm : Form
{
    private const double MinMeasuredWidth = 0.25; // anything shorter is probably not a full swipe

    private readonly Settings _settings;
    private readonly TrayApp _app;

    private readonly Dictionary<SwitchMode, RadioButton> _modes = Enum.GetValues<SwitchMode>()
        .ToDictionary(m => m, m => new RadioButton { Text = m.Label(), AutoSize = true, Tag = m });
    private readonly TrackBar _tabs = new()
    {
        Minimum = (int)Settings.MinTabsPerSwipe,
        Maximum = (int)Settings.MaxTabsPerSwipe,
        TickFrequency = 2,
        LargeChange = 2,
        Width = 300,
    };
    private readonly Label _tabsValue = new() { AutoSize = true, Margin = new Padding(6, 10, 0, 0) };
    private readonly Label _widthLabel = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    private readonly Button _measure = new() { Text = "Measure my swipe", AutoSize = true };
    private readonly Label _measureStatus = new() { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText };
    private readonly CheckBox _invert = new() { Text = "Reverse direction (slide right = previous tab)", AutoSize = true };
    private readonly CheckBox _wrap = new() { Text = "Wrap around (past the last tab, continue from the first)", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _warnSwipe = new() { Text = "Remind me if Windows' own three-finger swipe is turned on", AutoSize = true };
    private readonly Label _swipeWarning = new()
    {
        Text = "Windows' three-finger swipe is still on, so it will switch apps while you switch tabs.",
        AutoSize = true,
        MaximumSize = new Size(300, 0),
        ForeColor = Color.FromArgb(196, 43, 28),
        Margin = new Padding(3, 7, 3, 3),
    };
    private readonly Button _fixSwipe = new() { Text = "Open touchpad settings", AutoSize = true };
    private FlowLayoutPanel? _swipeWarningRow;

    private readonly Label _versionLabel = new() { AutoSize = true, Margin = new Padding(3, 3, 3, 6) };
    private readonly ComboBox _updateMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly Button _checkUpdates = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button _installUpdate = new() { Text = "Install update", AutoSize = true, Visible = false };

    private static readonly (UpdateMode mode, string text)[] UpdateModeChoices =
    {
        (UpdateMode.Ask, "Ask me before installing"),
        (UpdateMode.Automatic, "Install automatically"),
        (UpdateMode.Off, "Don't check automatically"),
    };

    private static readonly (string text, string url)[] Links =
    {
        ("liventnick.xyz", "https://liventnick.xyz"),
        ("GitHub", "https://github.com/LiventNick/ThreeFingerTabScroll"),
        ("Buy me a coffee", "https://ko-fi.com/liventnick"),
    };

    private SwipeCalibrator? _calibrator;

    public SettingsForm(Settings settings, TrayApp app)
    {
        _settings = settings;
        _app = app;

        Text = "Three-Finger Tab Scroll - Settings";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        root.Controls.Add(Group("When you slide three fingers", _modes.Values.ToArray<Control>()));

        var tabsRow = Row(_tabs, _tabsValue);
        var widthRow = Row(_widthLabel, _measure);
        root.Controls.Add(Group("Speed",
            new Label { Text = "Tabs per full swipe (one edge of the touchpad to the other):", AutoSize = true },
            tabsRow,
            widthRow,
            _measureStatus));

        _swipeWarningRow = Row(_swipeWarning, _fixSwipe);
        root.Controls.Add(Group("Other", _wrap, _invert, _autostart, _warnSwipe, _swipeWarningRow));

        foreach (var (_, text) in UpdateModeChoices) _updateMode.Items.Add(text);
        root.Controls.Add(Group("Updates",
            _versionLabel,
            Row(new Label { Text = "When a new version is out:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, _updateMode),
            Row(_checkUpdates, _installUpdate)));

        // Bottom bar: links on the left, Close on the right.
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        for (int i = 0; i < Links.Length; i++)
        {
            if (i > 0) links.Controls.Add(new Label { Text = "·", AutoSize = true, Margin = new Padding(0, 3, 0, 0), ForeColor = SystemColors.GrayText });
            var (text, url) = Links[i];
            var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 0) };
            link.LinkClicked += (_, _) => OpenUrl(url);
            links.Controls.Add(link);
        }
        var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();
        CancelButton = close;

        var bottom = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(links, 0, 0);
        bottom.Controls.Add(close, 1, 0);
        root.Controls.Add(bottom);
        Controls.Add(root);

        // Give all sections (and the bottom bar) the width of the widest one.
        Load += (_, _) =>
        {
            var groups = root.Controls.OfType<GroupBox>().ToList();
            int width = groups.Max(g => g.Width);
            foreach (var g in groups) g.MinimumSize = new Size(width, 0);
            bottom.MinimumSize = new Size(width, 0);
        };

        LoadValues();

        foreach (var (mode, radio) in _modes)
            radio.CheckedChanged += (_, _) => { if (radio.Checked) Change(() => _settings.Mode = mode); };
        _tabs.ValueChanged += (_, _) =>
        {
            Change(() => _settings.TabsPerSwipe = _tabs.Value);
            UpdateLabels();
        };
        _invert.CheckedChanged += (_, _) => Change(() => _settings.InvertDirection = _invert.Checked);
        _wrap.CheckedChanged += (_, _) => Change(() => _settings.WrapAround = _wrap.Checked);
        _autostart.CheckedChanged += (_, _) => _app.SetAutostart(_autostart.Checked);
        _warnSwipe.CheckedChanged += (_, _) => Change(() => _settings.WarnAboutWindowsSwipe = _warnSwipe.Checked);
        _fixSwipe.Click += (_, _) => WindowsGestureCheck.OpenTouchpadSettings();
        Activated += (_, _) => RefreshSwipeWarning(); // re-check when coming back from Windows Settings
        _measure.Click += (_, _) => ToggleMeasuring();
        _updateMode.SelectedIndexChanged += (_, _) =>
            Change(() => _settings.Updates = UpdateModeChoices[Math.Max(0, _updateMode.SelectedIndex)].mode);
        _checkUpdates.Click += async (_, _) =>
        {
            _checkUpdates.Enabled = false;
            try { await _app.CheckForUpdatesAsync(interactive: true); }
            finally { if (!IsDisposed) { _checkUpdates.Enabled = true; RefreshUpdateStatus(); } }
        };
        _installUpdate.Click += async (_, _) => await _app.InstallUpdateAsync();
    }

    private void RefreshSwipeWarning()
    {
        if (_swipeWarningRow != null)
            _swipeWarningRow.Visible = WindowsGestureCheck.IsWindowsSwipeOn();
    }

    /// <summary>Updates the version line and the Install button (called after update checks).</summary>
    public void RefreshUpdateStatus()
    {
        if (IsDisposed) return;
        var updater = _app.Updater;
        string status = updater.UnavailableReason
                        ?? (updater.AvailableVersion is { } v ? $"Version {v} is available." : "");
        _versionLabel.Text = $"You have version {updater.CurrentVersion}. {status}".Trim();
        _installUpdate.Visible = updater.Available != null;
        _installUpdate.Text = $"Install version {updater.AvailableVersion}";
        _checkUpdates.Enabled = updater.CanUpdate;
        _updateMode.Enabled = updater.CanUpdate;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No default browser configured; nothing useful to do.
        }
    }

    private void LoadValues()
    {
        foreach (var (mode, radio) in _modes)
            radio.Checked = _settings.Mode == mode;
        _tabs.Value = (int)Math.Round(Math.Clamp(_settings.TabsPerSwipe, _tabs.Minimum, _tabs.Maximum));
        _invert.Checked = _settings.InvertDirection;
        _wrap.Checked = _settings.WrapAround;
        _warnSwipe.Checked = _settings.WarnAboutWindowsSwipe;
        RefreshSwipeWarning();
        _autostart.Checked = Autostart.IsEnabledSafe;
        _updateMode.SelectedIndex = Math.Max(0, Array.FindIndex(UpdateModeChoices, c => c.mode == _settings.Updates));
        RefreshUpdateStatus();
        _measureStatus.Text = "If the tab count feels off, measure your swipe so it matches your touchpad and hand.";
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        _tabsValue.Text = $"{_tabs.Value} tabs";
        _widthLabel.Text = $"A full swipe moves your fingers {_settings.SwipeWidthPercent:0}% of the touchpad width.";
    }

    private void Change(Action apply)
    {
        apply();
        _app.ApplyAndSave();
    }

    private void ToggleMeasuring()
    {
        if (_calibrator != null)
        {
            StopMeasuring();
            return;
        }

        _calibrator = new SwipeCalibrator();
        _calibrator.Measured += OnMeasured;
        _app.SetCalibrator(_calibrator);
        _measure.Text = "Cancel";
        _measureStatus.ForeColor = SystemColors.ControlText;
        _measureStatus.Text = "Put three fingers at the far left of the touchpad and slide slowly all the way to the far right " +
                              "(the way you'd normally swipe), then lift. Tabs won't switch while measuring.";
    }

    private void OnMeasured(double width)
    {
        if (width < MinMeasuredWidth)
        {
            _measureStatus.Text = $"That swipe only covered {width:P0} of the touchpad. Try again, from one edge all the way to the other.";
            return;
        }

        _settings.SwipeWidthPercent = Math.Round(Math.Min(width, 1.0) * 100);
        _app.ApplyAndSave();
        UpdateLabels();
        _measure.Text = "Done";
        _measureStatus.Text = $"Measured {_settings.SwipeWidthPercent:0}%. A full swipe now moves {_tabs.Value} tabs. " +
                              "Swipe again to re-measure, or click Done.";
    }

    private void StopMeasuring()
    {
        if (_calibrator == null) return;
        _calibrator.Measured -= OnMeasured;
        _calibrator = null;
        _app.SetCalibrator(null);
        _measure.Text = "Measure my swipe";
        _measureStatus.ForeColor = SystemColors.GrayText;
        _measureStatus.Text = $"Swipe width: {_settings.SwipeWidthPercent:0}% of the touchpad.";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopMeasuring();
        base.OnFormClosed(e);
    }

    private static GroupBox Group(string title, params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
        };
        flow.Controls.AddRange(controls);
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(440, 0),
            Padding = new Padding(8),
        };
        group.Controls.Add(flow);
        return group;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        row.Controls.AddRange(controls);
        return row;
    }
}
