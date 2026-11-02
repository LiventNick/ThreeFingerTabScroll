using System.Diagnostics;
using System.Text.RegularExpressions;
using UIA = Interop.UIAutomationClient;

namespace ThreeFingerTabScroll;

/// <summary>
/// One browser tab: title, memory use if the browser reports it (e.g. "110 MB"), and its on-screen
/// rectangle in physical pixels (empty if the tab is scrolled out of view).
/// </summary>
internal sealed record TabInfo(string Title, string? Memory, bool HighMemory, Rectangle Bounds);

/// <summary>
/// Tabs of one browser window in tab-strip order, which one is active, and the site shown in the
/// address bar (which belongs to the active tab; the browser doesn't expose other tabs' addresses).
/// </summary>
internal sealed record TabList(IReadOnlyList<TabInfo> Tabs, int ActiveIndex, string? ActiveSite)
{
    public int Count => Tabs.Count;
    public TabInfo Active => Tabs[ActiveIndex];
}

/// <summary>
/// Reads a browser window's tab strip with Windows UI Automation (the accessibility API).
/// Web page content is skipped, so this stays fast even with heavy pages open.
/// Call from a background thread: it makes cross-process calls that can take tens of milliseconds.
/// </summary>
internal static class TabStripReader
{
    private const int UIA_BoundingRectanglePropertyId = 30001;
    private const int UIA_IsOffscreenPropertyId = 30022;
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_ValueValuePropertyId = 30045;
    private const int UIA_SelectionItemIsSelectedPropertyId = 30079;
    private const int UIA_LegacyIAccessibleStatePropertyId = 30096;
    private const int UIA_ClassNamePropertyId = 30012;
    private const string FirefoxTabClass = "tabbrowser-tab";
    private const int STATE_SYSTEM_SELECTED = 0x2;
    private const int UIA_EditControlTypeId = 50004;
    private const int UIA_TabItemControlTypeId = 50019;
    private const int UIA_DocumentControlTypeId = 50030;

    private const int MaxDepth = 20;
    private const int MaxElements = 3000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly Lazy<UIA.CUIAutomation8> Automation = new(() => new UIA.CUIAutomation8());

    // Chromium appends hover-card details like " - Memory usage - 149 MB" to tab names.
    private static readonly Regex MemorySuffix = new(@"\s+-\s+(?<high>High\s+)?memory usage\s+-\s+(?<mem>[\d.,]+\s*[KMGT]?B)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static TabInfo ParseTab(string? name, Rectangle bounds)
    {
        if (string.IsNullOrWhiteSpace(name)) return new TabInfo("(untitled)", null, false, bounds);
        var m = MemorySuffix.Match(name);
        if (!m.Success) return new TabInfo(name.Trim(), null, false, bounds);
        string title = name[..m.Index].Trim();
        return new TabInfo(title.Length > 0 ? title : "(untitled)", m.Groups["mem"].Value, m.Groups["high"].Success, bounds);
    }

    /// <summary>Turns an address bar value like "linkedin.com/in/x" or "https://www.x.com/a" into "linkedin.com" / "x.com".</summary>
    private static string? SiteFromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        address = address.Trim();
        if (address.Contains(' ')) return null; // the user is typing a search, not a URL

        string scheme = "";
        int schemeEnd = address.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            scheme = address[..schemeEnd].ToLowerInvariant();
            address = address[(schemeEnd + 3)..];
        }

        int end = address.IndexOfAny(new[] { '/', '?', '#' });
        string host = end >= 0 ? address[..end] : address;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        if (host.Length == 0) return null;

        // Keep the scheme for browser pages like brave://settings, as the browser's own card does.
        return scheme is "" or "http" or "https" ? host : $"{scheme}://{host}";
    }

    /// <summary>Selected tab: UIA's selection state, or the older MSAA "selected" flag some browsers use instead.</summary>
    private static bool IsSelected(UIA.IUIAutomationElement element)
    {
        try
        {
            if (element.GetCachedPropertyValue(UIA_SelectionItemIsSelectedPropertyId) is bool selected)
                return selected;
            return element.GetCachedPropertyValue(UIA_LegacyIAccessibleStatePropertyId) is int state
                   && (state & STATE_SYSTEM_SELECTED) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Rectangle GetBounds(UIA.IUIAutomationElement element)
    {
        try
        {
            if (element.CachedIsOffscreen != 0) return Rectangle.Empty;
            var r = element.CachedBoundingRectangle;
            return r.right > r.left && r.bottom > r.top
                ? Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom)
                : Rectangle.Empty;
        }
        catch
        {
            return Rectangle.Empty;
        }
    }

    /// <summary>Returns the tabs, or null if the tab strip couldn't be read.</summary>
    public static TabList? Read(IntPtr hwnd, Action<string>? log = null)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var automation = Automation.Value;
            var root = automation.ElementFromHandle(hwnd);
            if (root == null) return null;

            var cache = automation.CreateCacheRequest();
            cache.AddProperty(UIA_ControlTypePropertyId);
            cache.AddProperty(UIA_NamePropertyId);
            cache.AddProperty(UIA_SelectionItemIsSelectedPropertyId);
            cache.AddProperty(UIA_BoundingRectanglePropertyId);
            cache.AddProperty(UIA_IsOffscreenPropertyId);
            cache.AddProperty(UIA_ValueValuePropertyId);
            cache.AddProperty(UIA_LegacyIAccessibleStatePropertyId);
            cache.AddProperty(UIA_ClassNamePropertyId);
            var walker = automation.ControlViewWalker;

            var found = new List<(TabInfo tab, bool selected, string className)>();
            string? site = null;
            int visited = 0;

            // Depth-first in tree order, so tabs come out in the same order as the tab strip.
            var stack = new Stack<(UIA.IUIAutomationElement element, int depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                if (timer.Elapsed > Timeout || visited > MaxElements) break;
                var (element, depth) = stack.Pop();

                var children = new List<UIA.IUIAutomationElement>();
                for (var child = walker.GetFirstChildElementBuildCache(element, cache);
                     child != null;
                     child = walker.GetNextSiblingElementBuildCache(child, cache))
                {
                    visited++;
                    int type = child.CachedControlType;
                    if (type == UIA_DocumentControlTypeId)
                        continue; // web page content: never contains the browser's own tabs

                    if (type == UIA_TabItemControlTypeId)
                    {
                        found.Add((ParseTab(child.CachedName, GetBounds(child)), IsSelected(child), child.CachedClassName ?? ""));
                        continue;
                    }

                    // The first address-looking text box outside the page is the address bar.
                    if (type == UIA_EditControlTypeId && site == null)
                        site = SiteFromAddress(child.GetCachedPropertyValue(UIA_ValueValuePropertyId) as string);

                    if (depth + 1 < MaxDepth)
                        children.Add(child);
                }

                for (int i = children.Count - 1; i >= 0; i--)
                    stack.Push((children[i], depth + 1));
            }

            // Firefox also presents its sidebar buttons (Bookmarks, History, AI chatbot...) as tab items.
            // Its real tabs are marked "tabbrowser-tab", so when that marker is present, keep only those.
            bool firefoxTabs = found.Any(f => f.className.Contains(FirefoxTabClass, StringComparison.Ordinal));
            var kept = firefoxTabs
                ? found.Where(f => f.className.Contains(FirefoxTabClass, StringComparison.Ordinal)).ToList()
                : found;
            var tabs = kept.Select(f => f.tab).ToList();
            int active = kept.FindIndex(f => f.selected);

            log?.Invoke($"tab strip: {tabs.Count} tab(s){(found.Count != kept.Count ? $" (ignored {found.Count - kept.Count} sidebar item(s))" : "")}, " +
                        $"active={active}, site={site ?? "?"}, {visited} elements in {timer.ElapsedMilliseconds} ms");
            return tabs.Count > 0 && active >= 0 ? new TabList(tabs, active, site) : null;
        }
        catch (Exception ex)
        {
            log?.Invoke($"tab strip read failed after {timer.ElapsedMilliseconds} ms: {ex.Message}");
            return null;
        }
    }
}
