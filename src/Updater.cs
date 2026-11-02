using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace ThreeFingerTabScroll;

/// <summary>
/// Checks GitHub Releases for new versions and installs them, using Velopack.
/// Works when the app was installed with the installer (or run from the portable zip). For local builds
/// (no repository URL baked in, or not installed) updates are simply unavailable.
/// </summary>
internal sealed class Updater
{
    private readonly UpdateManager? _manager;
    private readonly Action<string>? _log;

    /// <summary>Set at startup when Velopack restarted us after installing an update.</summary>
    public static string? JustUpdatedTo { get; set; }

    /// <summary>The GitHub repository releases come from, baked in at build time (see the .csproj).</summary>
    public static string? RepositoryUrl =>
        typeof(Updater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value is { Length: > 0 } url ? url : null;

    public static string AppVersion =>
        typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] // drop the source-control hash the SDK appends
        ?? typeof(Updater).Assembly.GetName().Version?.ToString(3)
        ?? "?";

    public Updater(Action<string>? log)
    {
        _log = log;
        if (RepositoryUrl is not { } repo) return;
        try
        {
            _manager = new UpdateManager(new GithubSource(repo, null, false));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"updates unavailable: {ex.Message}");
        }
    }

    /// <summary>True if this copy can update itself (installed, and built with a repository URL).</summary>
    public bool CanUpdate => _manager?.IsInstalled == true;

    /// <summary>Why updates are unavailable, for display; null when they work.</summary>
    public string? UnavailableReason =>
        RepositoryUrl == null ? "This is a local build, so it can't update itself."
        : !CanUpdate ? "Updates work when the app is installed with the installer from GitHub."
        : null;

    public string CurrentVersion => _manager?.IsInstalled == true ? _manager.CurrentVersion?.ToString() ?? AppVersion : AppVersion;

    /// <summary>A newer version found by the last check, if any.</summary>
    public UpdateInfo? Available { get; private set; }

    public string? AvailableVersion => Available?.TargetFullRelease.Version.ToString();

    public bool IsBusy { get; private set; }

    /// <summary>Checks GitHub for a newer version. Returns it, or null if up to date / unavailable / offline.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!CanUpdate || _manager == null || IsBusy) return Available;
        try
        {
            IsBusy = true;
            Available = await _manager.CheckForUpdatesAsync();
            _log?.Invoke(Available != null ? $"update available: {AvailableVersion}" : "no update available");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"update check failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
        return Available;
    }

    /// <summary>
    /// Downloads the available update (reporting 0-100 to <paramref name="progress"/>, from a background
    /// thread), then calls <paramref name="beforeRestart"/> to clean up and restarts into the new version
    /// straight away. Returns false (with the reason) if it couldn't.
    /// </summary>
    public async Task<(bool ok, string? error)> InstallAsync(Action beforeRestart, Action<int>? progress = null)
    {
        if (!CanUpdate || _manager == null || Available is not { } update) return (false, "No update is available.");
        if (IsBusy) return (false, "An update check is already running.");
        try
        {
            IsBusy = true;
            await _manager.DownloadUpdatesAsync(update, progress);
            _log?.Invoke($"downloaded {AvailableVersion}, restarting to install");
            beforeRestart();
            _manager.ApplyUpdatesAndRestart(update); // exits this process
            return (true, null);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"update failed: {ex.Message}");
            return (false, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
