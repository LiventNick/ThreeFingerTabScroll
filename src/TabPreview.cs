namespace ThreeFingerTabScroll;

/// <summary>
/// Preview modes: while sliding, show which tab you'll land on; lifting switches once.
///
/// Two styles:
///  - Tab list: a list of tabs centered over the browser with the target highlighted.
///  - Hover card: a card under the target tab, styled like the browser's own tab hover card.
///
/// Tabs are read in the background the moment three fingers touch down, so they're usually ready by
/// the time the slide starts. If they can't be read, a step counter is shown instead.
/// </summary>
internal sealed class TabPreview : IDisposable
{
    private const int MaxRememberedSites = 500;

    private readonly ForegroundBrowser _browsers;
    private readonly Action<string>? _log;
    private readonly TabOverlay _list = new();
    private readonly HoverCardOverlay _card = new();

    // The browser only reveals the address of the active tab, so sites are remembered by tab title
    // whenever a tab is seen active. The hover card shows the site for tabs it has learned.
    private readonly Dictionary<string, string> _sitesByTitle = new();

    /// <summary>Past the last tab, continue from the first (and vice versa) instead of stopping.</summary>
    public bool WrapAround { get; set; }

    private bool _active, _shown, _loaded, _cardStyle;
    private IntPtr _hwnd;
    private int _offset;
    private TabList? _tabs;
    private int _generation; // ignores tab lists that arrive after their gesture ended

    public TabPreview(ForegroundBrowser browsers, Action<string>? log)
    {
        _browsers = browsers;
        _log = log;
        _ = _list.Handle; // create the windows now (hidden) so background work can BeginInvoke onto them
        _ = _card.Handle;
    }

    /// <summary>Three fingers touched down. Starts reading the tabs if a browser is in front.</summary>
    public void Begin(bool hoverCardStyle)
    {
        if (!_browsers.IsForeground(out _, out _hwnd))
            return;

        _active = true;
        _shown = false;
        _loaded = false;
        _cardStyle = hoverCardStyle;
        _offset = 0;
        _tabs = null;
        int generation = ++_generation;
        ReadTabsAsync(_hwnd, TimeSpan.Zero, tabs => OnTabsLoaded(generation, tabs));
    }

    /// <summary>The gesture is horizontal: start showing the preview.</summary>
    public void Lock()
    {
        if (!_active) return;
        _shown = true;
        Render();
    }

    public void Step(int direction)
    {
        if (!_active) return;
        _offset += direction;
        Clamp();
        if (_shown) Render();
    }

    /// <summary>Fingers lifted: hide the preview and switch to the chosen tab.</summary>
    public void End()
    {
        if (!_active) return;
        _active = false;
        _shown = false;
        _generation++;
        _list.HideOverlay();
        _card.HideOverlay();

        int offset = MovesToTarget();
        if (offset == 0) return;

        if (!_browsers.IsForeground(out var process, out var hwnd) || hwnd != _hwnd)
        {
            _log?.Invoke($"preview: not switching, the browser window is no longer in front ({process ?? "unknown"})");
            return;
        }

        var result = TabSwitcher.Switch(forward: offset > 0, count: Math.Abs(offset),
            usePageKeys: ForegroundBrowser.UsesPageKeys(process));
        _log?.Invoke(result == TabSwitcher.Result.Sent
            ? $"preview: moved {offset:+0;-0} tab(s) in {process}"
            : $"preview: not sent ({result})");

        // Once the browser has switched, learn the new tab's site for future hover cards.
        if (result == TabSwitcher.Result.Sent)
            ReadTabsAsync(hwnd, TimeSpan.FromMilliseconds(600), RememberSite);
    }

    private void ReadTabsAsync(IntPtr hwnd, TimeSpan delay, Action<TabList?> onUiThread)
    {
        Task.Run(async () =>
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            var tabs = TabStripReader.Read(hwnd, _log);
            try
            {
                _list.BeginInvoke(() => onUiThread(tabs));
            }
            catch (InvalidOperationException)
            {
                // App is shutting down.
            }
        });
    }

    private void OnTabsLoaded(int generation, TabList? tabs)
    {
        RememberSite(tabs);
        if (generation != _generation || !_active) return;
        _tabs = tabs;
        _loaded = true;
        Clamp();
        if (_shown) Render();
    }

    private void RememberSite(TabList? tabs)
    {
        if (tabs?.ActiveSite is not { } site) return;
        if (_sitesByTitle.Count >= MaxRememberedSites) _sitesByTitle.Clear();
        _sitesByTitle[tabs.Active.Title] = site;
    }

    /// <summary>
    /// With a known tab list: without wrap-around, stop at the first and last tab; with it, keep the
    /// offset within one lap so it can't grow without limit.
    /// </summary>
    private void Clamp()
    {
        if (_tabs is not { } t) return;
        if (WrapAround)
            _offset %= t.Count;
        else
            _offset = Math.Clamp(_offset, -t.ActiveIndex, t.Count - 1 - t.ActiveIndex);
    }

    /// <summary>Index of the tab the gesture currently points at (only meaningful with a tab list).</summary>
    private int TargetIndex()
    {
        if (_tabs is not { } t) return 0;
        int n = t.Count;
        return ((t.ActiveIndex + _offset) % n + n) % n;
    }

    /// <summary>
    /// Signed number of Ctrl+Tab presses to reach the target. When wrapping, takes the shorter way round
    /// (browsers wrap Ctrl+Tab too), e.g. from the last tab "+1" is one press, not a trip back.
    /// </summary>
    private int MovesToTarget()
    {
        if (_tabs is not { } t || !WrapAround) return _offset;
        int n = t.Count;
        int forward = ((TargetIndex() - t.ActiveIndex) % n + n) % n;
        return forward <= n / 2 ? forward : forward - n;
    }

    private void Render()
    {
        // Wait for the tab list (usually under 0.1 s) rather than flashing the counter first.
        if (!_loaded) return;

        int target = TargetIndex();
        int moves = MovesToTarget();

        if (_cardStyle && _tabs is { } t)
        {
            var tab = t.Tabs[target];
            if (!tab.Bounds.IsEmpty)
            {
                string? site = target == t.ActiveIndex ? t.ActiveSite : null;
                site ??= _sitesByTitle.GetValueOrDefault(tab.Title);
                _list.HideOverlay();
                _card.ShowFor(_hwnd, tab, site);
                _log?.Invoke($"hover card: {moves:+0;-0} -> \"{tab.Title}\" ({site ?? "site unknown"}, {tab.Memory ?? "no memory info"})");
                return;
            }
            // Target tab is scrolled out of view in the tab strip: use the list for this step.
        }

        _card.HideOverlay();
        _list.ShowFor(_hwnd, _tabs, moves, target);
        if (_tabs is { } tabs)
            _log?.Invoke($"tab list: {moves:+0;-0} -> \"{tabs.Tabs[target].Title}\"");
        else
            _log?.Invoke($"tab list: {moves:+0;-0} (tab titles not available)");
    }

    public void Dispose()
    {
        _list.Dispose();
        _card.Dispose();
    }
}
