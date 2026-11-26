using Bloom.Core.Settings;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Bloom.App.Services;

/// <summary>Updates via Velopack from GitHub Releases (Blueprint 10.5). Only works in an installed copy.</summary>
public sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/simsonimson/bloom";
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService(AppSettings settings, ILogger<UpdateService> logger)
    {
        _logger = logger;
        var channel = settings.UpdateChannel;
        _manager = new UpdateManager(
            new GithubSource(RepositoryUrl, null, prerelease: channel != "stable"),
            new UpdateOptions { ExplicitChannel = channel });
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString()
        ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>Returns the new version, or null when bloom is up to date.</summary>
    public async Task<string?> CheckAsync()
    {
        if (!IsInstalled) return null;
        try
        {
            _pending = await _manager.CheckForUpdatesAsync();
            return _pending?.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            throw;
        }
    }

    public async Task DownloadAndRestartAsync(Action<int>? progress = null)
    {
        if (_pending is null) return;
        await _manager.DownloadUpdatesAsync(_pending, progress);
        _logger.LogInformation("Applying update {Version}", _pending.TargetFullRelease.Version);
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
