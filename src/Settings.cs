using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreeFingerTabScroll;

internal enum SwitchMode
{
    /// <summary>Show our own tab list while sliding; switch once when the fingers lift.</summary>
    Preview,
    /// <summary>Show a browser-style hover card under the target tab; switch when the fingers lift.</summary>
    HoverCard,
    /// <summary>Switch tabs immediately while sliding.</summary>
    Live,
}

internal enum UpdateMode
{
    /// <summary>Check regularly and show a notification; install when the user clicks it.</summary>
    Ask,
    /// <summary>Check regularly and install right away (the app restarts in a couple of seconds).</summary>
    Automatic,
    /// <summary>Only check when the user clicks "Check for updates".</summary>
    Off,
}

internal static class SwitchModeNames
{
    public static string Label(this SwitchMode mode) => mode switch
    {
        SwitchMode.Preview => "Tab list - show a list of tabs, switch when fingers lift",
        SwitchMode.HoverCard => "Hover card - show a browser-style card under the tab, switch when fingers lift",
        _ => "Live - switch tabs immediately while sliding",
    };

    public static string ShortLabel(this SwitchMode mode) => mode switch
    {
        SwitchMode.Preview => "Tab list",
        SwitchMode.HoverCard => "Hover card",
        _ => "Live switching",
    };
}

/// <summary>User settings, stored as JSON in %AppData%\ThreeFingerTabScroll\settings.json.</summary>
internal sealed class Settings
{
    public const double MinTabsPerSwipe = 2, MaxTabsPerSwipe = 40;

    public bool Enabled { get; set; } = true;

    public SwitchMode Mode { get; set; } = SwitchMode.Preview;

    /// <summary>How many tabs one full swipe (one edge of the touchpad to the other) moves.</summary>
    public double TabsPerSwipe { get; set; } = 10;

    /// <summary>
    /// How far the center of three fingers travels in a full swipe, as a percent of touchpad width.
    /// It's less than 100 because the fingers take up space. Set by "Measure my swipe".
    /// </summary>
    public double SwipeWidthPercent { get; set; } = 90;

    /// <summary>Swap directions (slide right = previous tab).</summary>
    public bool InvertDirection { get; set; }

    /// <summary>Past the last tab, continue from the first (and the other way round) instead of stopping.</summary>
    public bool WrapAround { get; set; }

    public UpdateMode Updates { get; set; } = UpdateMode.Ask;

    /// <summary>Show a reminder at startup when Windows' own three-finger swipe is still turned on.</summary>
    public bool WarnAboutWindowsSwipe { get; set; } = true;

    /// <summary>Process names the gesture works in.</summary>
    public List<string> BrowserProcesses { get; set; } = new(ForegroundBrowser.DefaultProcesses);

    public bool FirstRunMessageShown { get; set; }

    /// <summary>
    /// Which version of the default browser list this file has seen. When new browsers are supported,
    /// bump <see cref="CurrentDefaultsVersion"/> and they're added once to existing lists.
    /// </summary>
    /// Defaults to 0 so files from before this field existed are recognized as old; fresh settings
    /// are created with the current version in <see cref="Load"/>.
    public int BrowserDefaultsVersion { get; set; }

    /// <summary>1 = Chromium browsers; 2 = added Firefox-based browsers.</summary>
    private const int CurrentDefaultsVersion = 2;

    /// <summary>Horizontal travel per tab, as a fraction of touchpad width.</summary>
    [JsonIgnore]
    public double StepFraction =>
        Math.Clamp(SwipeWidthPercent, 20, 100) / 100.0 / Math.Clamp(TabsPerSwipe, MinTabsPerSwipe, MaxTabsPerSwipe);

    // ---- Persistence ------------------------------------------------------

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThreeFingerTabScroll");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads settings, or returns defaults. A corrupt file is renamed to settings.json.bad.</summary>
    public static Settings Load(Action<string>? log = null)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded != null)
                {
                    loaded.BrowserProcesses ??= new(ForegroundBrowser.DefaultProcesses);
                    if (loaded.BrowserDefaultsVersion < CurrentDefaultsVersion)
                    {
                        // Files from older versions lack this field (reads as 0): add newly supported browsers once.
                        foreach (var name in ForegroundBrowser.DefaultProcesses)
                            if (!loaded.BrowserProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
                                loaded.BrowserProcesses.Add(name);
                        loaded.BrowserDefaultsVersion = CurrentDefaultsVersion;
                        try { loaded.Save(); } catch { /* applied in memory anyway */ }
                    }
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Could not read settings ({ex.Message}); using defaults.");
            try { File.Move(FilePath, FilePath + ".bad", overwrite: true); } catch { }
        }
        return new Settings { BrowserDefaultsVersion = CurrentDefaultsVersion };
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    public void CopyFrom(Settings other)
    {
        Enabled = other.Enabled;
        Mode = other.Mode;
        TabsPerSwipe = other.TabsPerSwipe;
        SwipeWidthPercent = other.SwipeWidthPercent;
        InvertDirection = other.InvertDirection;
        WrapAround = other.WrapAround;
        Updates = other.Updates;
        WarnAboutWindowsSwipe = other.WarnAboutWindowsSwipe;
        BrowserProcesses = other.BrowserProcesses;
    }
}
