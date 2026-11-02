namespace ThreeFingerTabScroll;

/// <summary>
/// Shown at startup while Windows' own three-finger swipe is still on (it clashes with this app).
/// Watches the setting and closes itself, with a thank-you, as soon as the user turns it off.
/// </summary>
internal sealed class SwipeReminderForm : Form
{
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
    private readonly CheckBox _dontRemind = new() { Text = "Don't remind me again", AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 1000 };

    /// <summary>True if the user ticked "Don't remind me again".</summary>
    public bool DontRemind => _dontRemind.Checked;

    public SwipeReminderForm()
    {
        Text = "Three-Finger Tab Scroll";
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
        ShowIcon = false;

        const int textWidth = 430;
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        flow.Controls.Add(new Label
        {
            Text = "Windows' three-finger swipe is still on",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold),
            Margin = new Padding(3, 0, 3, 8),
        });
        flow.Controls.Add(new Label
        {
            Text = "Windows uses the same three-finger swipe to switch apps, so while it's on, swiping will " +
                   "switch apps instead of (or as well as) switching tabs.\n\n" +
                   "To fix it, open touchpad settings and set:\n" +
                   "Three-finger gestures  >  Swipes  >  Nothing",
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
        });

        _status.Text = "This window closes by itself once the setting is changed.";
        _status.ForeColor = SystemColors.GrayText;
        _status.MaximumSize = new Size(textWidth, 0);
        flow.Controls.Add(_status);
        flow.Controls.Add(_dontRemind);

        var open = new Button { Text = "Open touchpad settings", AutoSize = true };
        open.Click += (_, _) => WindowsGestureCheck.OpenTouchpadSettings();
        var later = new Button { Text = "Remind me next time", AutoSize = true, DialogResult = DialogResult.Cancel };
        later.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(open);
        buttons.Controls.Add(later);
        flow.Controls.Add(buttons);

        AcceptButton = open;
        CancelButton = later;
        Controls.Add(flow);

        _watch.Tick += (_, _) =>
        {
            if (WindowsGestureCheck.IsWindowsSwipeOn()) return;
            _watch.Stop();
            _status.ForeColor = Color.FromArgb(16, 124, 16);
            _status.Text = "All set! Windows' swipe is off. You can switch tabs with three fingers now.";
            var closeSoon = new System.Windows.Forms.Timer { Interval = 2500 };
            closeSoon.Tick += (_, _) =>
            {
                closeSoon.Dispose();
                Close();
            };
            closeSoon.Start();
        };
        Shown += (_, _) => _watch.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _watch.Dispose();
        base.Dispose(disposing);
    }
}
