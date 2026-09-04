using Velopack;
using Velopack.Sources;

namespace K162.App.Services;

/// <summary>
/// Auto-update via Velopack against this repo's GitHub Releases (CI publishes
/// Velopack packages on every v* tag). Checks on startup and every 6h; the status
/// bar shows an update chip, clicking it downloads (delta when possible) and restarts.
///
/// Only active when the app was installed through the Velopack Setup.exe —
/// dev runs and plain-zip runs are never touched.
/// </summary>
public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/apettey/eve-k162-tracker";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager _mgr = new(new GithubSource(RepoUrl, null, prerelease: false));
    private UpdateInfo? _pending;

    public string? AvailableVersion { get; private set; }
    public string Status { get; private set; } = ""; // "", "available", "downloading", "failed"
    public event Action? Changed;

    public string CurrentVersion => _mgr.IsInstalled ? _mgr.CurrentVersion?.ToString() ?? "?" : "dev";

    public async Task RunAsync(CancellationToken ct)
    {
        if (!_mgr.IsInstalled) return; // dev / zip run — nothing to update
        await Task.Delay(TimeSpan.FromSeconds(20), ct); // let startup traffic go first
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var info = await _mgr.CheckForUpdatesAsync();
                if (info is not null)
                {
                    _pending = info;
                    AvailableVersion = info.TargetFullRelease.Version.ToString();
                    Status = "available";
                    Changed?.Invoke();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Offline / rate limit — try again next interval.
            }
            await Task.Delay(CheckInterval, ct);
        }
    }

    /// <summary>Download the pending update and restart into it.</summary>
    public async Task ApplyAsync()
    {
        if (_pending is null || Status == "downloading") return;
        Status = "downloading";
        Changed?.Invoke();
        try
        {
            await _mgr.DownloadUpdatesAsync(_pending);
            _mgr.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception)
        {
            Status = "failed";
            Changed?.Invoke();
        }
    }
}
