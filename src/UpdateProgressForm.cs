namespace ThreeFingerTabScroll;

/// <summary>Small window shown while an update downloads and installs.</summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
    private readonly ProgressBar _bar = new() { Width = 340, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };

    public UpdateProgressForm(string version)
    {
        Text = "Updating Three-Finger Tab Scroll";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false; // the download can't be cancelled halfway
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        flow.Controls.Add(_status);
        flow.Controls.Add(_bar);
        Controls.Add(flow);

        _status.Text = $"Downloading version {version}...";
    }

    /// <summary>Thread-safe: Velopack reports download progress from a background thread.</summary>
    public void SetProgress(int percent)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => SetProgress(percent));
            return;
        }
        _bar.Value = Math.Clamp(percent, 0, 100);
        if (percent >= 100)
            _status.Text = "Installing. The app will restart in a moment...";
    }

    public void SetInstalling()
    {
        _bar.Style = ProgressBarStyle.Marquee;
        _status.Text = "Installing. The app will restart in a moment...";
        Refresh();
    }
}
