using System.Drawing.Drawing2D;

namespace ThreeFingerTabScroll;

/// <summary>"Tab list" preview: a list of tabs centered over the browser with the target highlighted.</summary>
internal sealed class TabOverlay : OverlayWindow
{
    private const int MaxRows = 9;
    private const char ArrowLeft = (char)0x2190, ArrowRight = (char)0x2192;

    private static readonly Color Background = Color.FromArgb(32, 33, 36);
    private static readonly Color TextColor = Color.FromArgb(232, 234, 237);
    private static readonly Color Muted = Color.FromArgb(154, 160, 166);
    private static readonly Color Accent = Color.FromArgb(26, 115, 232);
    private static readonly Color CurrentRow = Color.FromArgb(60, 64, 67);

    private TabList? _tabs;
    private int _offset, _target;
    private Font? _titleFont, _smallFont, _bigFont;

    public TabOverlay()
    {
        BackColor = Background;
    }

    /// <summary>
    /// Shows or updates the overlay centered over the browser window. <paramref name="offset"/> is how
    /// many tabs lifting will move; <paramref name="target"/> is the highlighted tab (with a tab list).
    /// </summary>
    public void ShowFor(IntPtr browserHwnd, TabList? tabs, int offset, int target)
    {
        _tabs = tabs;
        _offset = offset;
        _target = target;
        if (UpdateDpiScale(browserHwnd) || _titleFont == null) CreateFonts();

        int width = S(480);
        int height = tabs != null
            ? S(12) + S(34) + Math.Min(tabs.Count, MaxRows) * S(34) + S(10)
            : S(96);

        var r = GetWindowBounds(browserHwnd);
        ShowAt(browserHwnd, r.Left + r.Width / 2 - width / 2, r.Top + r.Height / 3 - height / 2, width, height);
    }

    private void CreateFonts()
    {
        _titleFont?.Dispose();
        _smallFont?.Dispose();
        _bigFont?.Dispose();
        _titleFont = new Font("Segoe UI", 14f * DpiScale, GraphicsUnit.Pixel);
        _smallFont = new Font("Segoe UI", 12f * DpiScale, GraphicsUnit.Pixel);
        _bigFont = new Font("Segoe UI Semibold", 20f * DpiScale, GraphicsUnit.Pixel);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Background);
        if (_titleFont == null || _smallFont == null || _bigFont == null) return;

        if (_tabs == null)
        {
            PaintCounter(g);
            return;
        }

        var tabs = _tabs;
        int count = tabs.Count;
        int target = Math.Clamp(_target, 0, count - 1);
        int pad = S(12);

        // Header: position and what lifting will do.
        var header = new Rectangle(pad, pad, Width - pad * 2, S(28));
        TextRenderer.DrawText(g, $"Tab {target + 1} of {count}", _smallFont, header, Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        string hint = _offset == 0 ? "Lift to stay here" : $"Lift to switch ({(_offset > 0 ? "+" : "")}{_offset})";
        TextRenderer.DrawText(g, hint, _smallFont, header, Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        // A window of rows around the target, so it stays in view with many tabs.
        int rows = Math.Min(count, MaxRows);
        int first = Math.Clamp(target - rows / 2, 0, count - rows);
        int rowHeight = S(34);
        int top = pad + S(34);

        for (int i = first; i < first + rows; i++)
        {
            var row = new Rectangle(pad, top + (i - first) * rowHeight, Width - pad * 2, rowHeight - S(2));
            bool isTarget = i == target;
            bool isCurrent = i == tabs.ActiveIndex;

            if (isTarget || isCurrent)
            {
                using var path = RoundedRect(row, S(6));
                using var brush = new SolidBrush(isTarget ? Accent : CurrentRow);
                g.FillPath(brush, path);
            }

            var numberRect = new Rectangle(row.Left + S(8), row.Top, S(34), row.Height);
            TextRenderer.DrawText(g, (i + 1).ToString(), _smallFont, numberRect, isTarget ? TextColor : Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            int tagWidth = isCurrent ? S(64) : 0;
            var titleRect = new Rectangle(numberRect.Right, row.Top, row.Right - numberRect.Right - S(8) - tagWidth, row.Height);
            TextRenderer.DrawText(g, tabs.Tabs[i].Title, _titleFont, titleRect, TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            if (isCurrent)
            {
                var tagRect = new Rectangle(row.Right - S(8) - tagWidth, row.Top, tagWidth, row.Height);
                TextRenderer.DrawText(g, "current", _smallFont, tagRect, isTarget ? TextColor : Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>Fallback when the tab titles couldn't be read: just show how far the gesture will move.</summary>
    private void PaintCounter(Graphics g)
    {
        string main = _offset switch
        {
            0 => "Current tab",
            > 0 => $"{_offset} tab{(_offset == 1 ? "" : "s")} right  {ArrowRight}",
            _ => $"{ArrowLeft}  {-_offset} tab{(_offset == -1 ? "" : "s")} left",
        };
        var mainRect = new Rectangle(0, S(14), Width, S(44));
        TextRenderer.DrawText(g, main, _bigFont, mainRect, TextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var hintRect = new Rectangle(0, S(58), Width, S(24));
        TextRenderer.DrawText(g, _offset == 0 ? "Slide to choose a tab" : "Lift to switch", _smallFont, hintRect, Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont?.Dispose();
            _smallFont?.Dispose();
            _bigFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
