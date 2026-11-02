using System.Diagnostics;

namespace ThreeFingerTabScroll;

/// <summary>
/// Shown the first time the app runs (including right after installing): explains the gesture,
/// helps turn off Windows' own three-finger swipe, and asks whether to start with Windows.
/// </summary>
internal sealed class WelcomeForm : Form
{
    private readonly CheckBox _autostart = new()
    {
        Text = "Start Three-Finger Tab Scroll when Windows starts",
        AutoSize = true,
        Checked = true,
        Margin = new Padding(3, 12, 3, 3),
    };

    /// <summary>The user's choice for starting with Windows.</summary>
    public bool StartWithWindows => _autostart.Checked;

    /// <param name="askAboutAutostart">False when the installer already asked about starting with Windows.</param>
    public WelcomeForm(bool askAboutAutostart = true)
    {
        _autostart.Visible = askAboutAutostart;
        Text = "Welcome to Three-Finger Tab Scroll";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        const int textWidth = 440;
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        flow.Controls.Add(new Label
        {
            Text = "You're all set!",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.4f, FontStyle.Bold),
            Margin = new Padding(3, 0, 3, 8),
        });
        flow.Controls.Add(new Label
        {
            Text = "Put three fingers on your touchpad and slide left or right to move between tabs in Chrome, " +
                   "Edge, Firefox, Brave, Vivaldi, Opera and other Chromium- and Firefox-based browsers.\n\n" +
                   "The app lives in the system tray (the blue arrow icon). Double-click it for settings.",
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
        });
        flow.Controls.Add(new Label
        {
            Text = "One more step",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 14, 3, 4),
        });
        flow.Controls.Add(new Label
        {
            Text = "Windows uses the same gesture to switch apps. Turn it off so the two don't clash:\n" +
                   "Touchpad settings > Three-finger gestures > Swipes > Nothing",
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
        });
        var openSettings = new Button { Text = "Open touchpad settings", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        openSettings.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true }); }
            catch { /* settings app unavailable; the text says where to go */ }
        };
        flow.Controls.Add(openSettings);
        flow.Controls.Add(_autostart);

        var ok = new Button { Text = "Get started", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        flow.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;
        Controls.Add(flow);
    }
}
