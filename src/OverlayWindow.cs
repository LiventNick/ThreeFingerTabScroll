using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ThreeFingerTabScroll;

/// <summary>
/// Base for the click-through, never-focused popups drawn over the browser.
/// They must never take focus: the browser has to stay in front to receive the final Ctrl+Tab.
/// </summary>
internal class OverlayWindow : Form
{
    protected float DpiScale { get; private set; } = 1f;

    /// <summary>Windows 11 corner styles (DWM_WINDOW_CORNER_PREFERENCE).</summary>
    public enum Corners { Square = 1, Round = 2, RoundSmall = 3 }

    public OverlayWindow(double opacity = 0.97, Corners corners = Corners.Round)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        TopMost = true;    // set before the handle exists, so it only affects the window style
        Opacity = opacity; // must stay below 1: a layered window plus WS_EX_TRANSPARENT is what makes it click-through
        _corners = corners;
    }

    private readonly Corners _corners;

    /// <summary>Sets the thin window border Windows 11 draws around rounded windows.</summary>
    protected void SetBorderColor(Color color)
    {
        if (!IsHandleCreated) return;
        int colorRef = color.R | (color.G << 8) | (color.B << 16);
        DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref colorRef, sizeof(int));
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11 rounded corners (ignored on older versions).
        int corners = (int)_corners;
        DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corners, sizeof(int));
    }

    /// <summary>Picks up the DPI of the monitor the browser is on. Returns true if it changed.</summary>
    protected bool UpdateDpiScale(IntPtr browserHwnd)
    {
        float scale = Math.Max(1f, GetDpiForWindow(browserHwnd) / 96f);
        if (scale == DpiScale) return false;
        DpiScale = scale;
        return true;
    }

    protected int S(float v) => (int)Math.Round(v * DpiScale);

    /// <summary>Places the window, keeping it inside the working area of the browser's monitor, and shows it.</summary>
    protected void ShowAt(IntPtr browserHwnd, int x, int y, int width, int height)
    {
        var area = Screen.FromHandle(browserHwnd).WorkingArea;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
        Bounds = new Rectangle(x, y, width, height);
        if (!Visible) Show();
        Invalidate();
    }

    public virtual void HideOverlay()
    {
        if (Visible) Hide();
    }

    protected static Rectangle GetWindowBounds(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>True if Windows apps are set to light mode (browsers follow this by default).</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    protected static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
