using System.Drawing.Drawing2D;

namespace ThreeFingerTabScroll;

/// <summary>
/// "Hover card" preview: a card drawn just below the target tab, styled like the one Chromium shows
/// when you rest the mouse on a tab (title, site, memory use). The mouse is never touched.
/// </summary>
internal sealed class HoverCardOverlay : OverlayWindow
{
    private readonly record struct Palette(Color Background, Color Border, Color Title, Color Secondary, Color Divider);

    private static readonly Palette Dark = new(
        Color.FromArgb(40, 40, 40), Color.FromArgb(78, 78, 78), Color.FromArgb(240, 240, 240),
        Color.FromArgb(200, 200, 200), Color.FromArgb(64, 64, 64));

    private static readonly Palette Light = new(
        Color.White, Color.FromArgb(218, 220, 224), Color.FromArgb(32, 33, 36),
        Color.FromArgb(95, 99, 104), Color.FromArgb(218, 220, 224));

    private const float CardWidth = 250, PadX = 14, PadTop = 12, PadBottom = 13, IconSize = 15;
    private const float SiteGap = 5, DividerGapAbove = 12, DividerGapBelow = 11;

    private readonly TabHighlight _highlight = new();
    private TabInfo? _tab;
    private string? _site;
    private Palette _palette = Dark;
    private Font? _titleFont, _textFont;

    /// <summary>Shows the card for <paramref name="tab"/> under its position in the tab strip.</summary>
    public void ShowFor(IntPtr browserHwnd, TabInfo tab, string? site)
    {
        _tab = tab;
        _site = site;
        bool light = SystemUsesLightTheme();
        _palette = light ? Light : Dark;
        BackColor = _palette.Background;
        if (UpdateDpiScale(browserHwnd) || _titleFont == null) CreateFonts();

        int width = S(CardWidth);
        int height = LayoutCard(width, draw: null);

        var t = tab.Bounds;
        _highlight.ShowFor(browserHwnd, t, light);
        ShowAt(browserHwnd, t.Left, t.Bottom + S(4), width, height);
        SetBorderColor(_palette.Border);
    }

    public override void HideOverlay()
    {
        _highlight.HideOverlay();
        base.HideOverlay();
    }

    private void CreateFonts()
    {
        _titleFont?.Dispose();
        _textFont?.Dispose();
        _titleFont = new Font("Segoe UI Semibold", 14f * DpiScale, GraphicsUnit.Pixel);
        _textFont = new Font("Segoe UI", 12.5f * DpiScale, GraphicsUnit.Pixel);
    }

    private const TextFormatFlags TitleFlags =
        TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
    private const TextFormatFlags LineFlags =
        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

    /// <summary>Lays out the card and returns its height; also draws it when <paramref name="draw"/> is given.</summary>
    private int LayoutCard(int width, Graphics? draw)
    {
        var g = draw;
        if (_tab == null || _titleFont == null || _textFont == null) return S(40);
        int padX = S(PadX), inner = width - padX * 2;
        int y = S(PadTop);

        // Title: up to two lines, like the browser's card.
        int lineHeight = _titleFont.Height;
        var titleSize = TextRenderer.MeasureText(_tab.Title, _titleFont, new Size(inner, lineHeight * 2), TitleFlags);
        int titleHeight = Math.Clamp(titleSize.Height, lineHeight, lineHeight * 2);
        if (g != null)
            TextRenderer.DrawText(g, _tab.Title, _titleFont, new Rectangle(padX, y, inner, titleHeight), _palette.Title, TitleFlags);
        y += titleHeight;

        if (_site != null)
        {
            y += S(SiteGap);
            int h = _textFont.Height;
            if (g != null)
                TextRenderer.DrawText(g, _site, _textFont, new Rectangle(padX, y, inner, h), _palette.Secondary, LineFlags);
            y += h;
        }

        if (_tab.Memory != null)
        {
            y += S(DividerGapAbove);
            if (g != null)
            {
                using var pen = new Pen(_palette.Divider, Math.Max(1, S(1)));
                g.DrawLine(pen, 0, y, width, y);
            }
            y += S(DividerGapBelow);

            int h = Math.Max(_textFont.Height, S(IconSize));
            if (g != null)
            {
                DrawGauge(g, new Rectangle(padX, y + (h - S(IconSize)) / 2, S(IconSize), S(IconSize)), _palette.Secondary);
                string text = $"{(_tab.HighMemory ? "High memory usage" : "Memory usage")}: {_tab.Memory}";
                var textRect = new Rectangle(padX + S(IconSize) + S(8), y, inner - S(IconSize) - S(8), h);
                TextRenderer.DrawText(g, text, _textFont, textRect, _palette.Secondary, LineFlags);
            }
            y += h;
        }

        return y + S(PadBottom);
    }

    /// <summary>Small speedometer icon, like the one next to "Memory usage" in Chromium.</summary>
    private void DrawGauge(Graphics g, Rectangle r, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float stroke = Math.Max(1.2f, 1.4f * DpiScale);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var arc = RectangleF.Inflate(r, -stroke / 2, -stroke / 2);
        g.DrawArc(pen, arc, 150, 240);
        var center = new PointF(arc.Left + arc.Width / 2, arc.Top + arc.Height / 2);
        double angle = -45 * Math.PI / 180;
        float len = arc.Width * 0.38f;
        g.DrawLine(pen, center, new PointF(center.X + (float)Math.Cos(angle) * len, center.Y + (float)Math.Sin(angle) * len));
        g.SmoothingMode = SmoothingMode.None;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_palette.Background);
        LayoutCard(Width, e.Graphics);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _highlight.Dispose();
            _titleFont?.Dispose();
            _textFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Faint see-through box over the target tab, like the browser's own hover highlight.</summary>
    private sealed class TabHighlight : OverlayWindow
    {
        public TabHighlight() : base(opacity: 0.12, corners: Corners.RoundSmall) { }

        public void ShowFor(IntPtr browserHwnd, Rectangle tab, bool light)
        {
            BackColor = light ? Color.Black : Color.White;
            ShowAt(browserHwnd, tab.Left, tab.Top, tab.Width, tab.Height);
        }

        protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(BackColor);
    }
}
