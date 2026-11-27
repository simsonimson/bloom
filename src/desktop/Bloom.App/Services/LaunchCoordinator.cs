using System.Diagnostics;
using Bloom.Core.Events;
using Bloom.Core.Settings;
using Bloom.Launcher.Bootstrapper;
using Bloom.Launcher.FastFlags;
using Bloom.Launcher.Installation;
using Bloom.Launcher.Links;
using Microsoft.Extensions.Logging;

namespace Bloom.App.Services;

public enum LaunchPhase { Idle, Checking, Downloading, Extracting, Starting, Fallback, Failed }

/// <summary>Progress of the current launch, for the client page and the tray.</summary>
public sealed record LaunchProgressChanged(LaunchPhase Phase, double Fraction, string? Message = null);

/// <summary>Raised when the bootstrapper paused itself after repeated failures.</summary>
public sealed record BootstrapperPaused(string Reason);

/// <summary>
/// Starts Roblox: through the bloom bootstrapper when enabled, otherwise (or on any bootstrapper
/// error) through the official installation (Blueprint 6.11).
/// </summary>
public sealed class LaunchCoordinator
{
    private const int MaxFailures = 3;
    private static readonly TimeSpan EarlyExitWindow = TimeSpan.FromSeconds(10);

    private readonly AppSettings _settings;
    private readonly RobloxBootstrapper _bootstrapper;
    private readonly FastFlagService _fastFlags;
    private readonly IEventBus _events;
    private readonly ILogger<LaunchCoordinator> _logger;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly LaunchPreparation _preparation;

    public LaunchCoordinator(AppSettings settings, RobloxBootstrapper bootstrapper, FastFlagService fastFlags,
        LaunchPreparation preparation, IEventBus events, ILogger<LaunchCoordinator> logger)
    {
        _preparation = preparation;
        _settings = settings;
        _bootstrapper = bootstrapper;
        _fastFlags = fastFlags;
        _events = events;
        _logger = logger;
    }

    public static string BloomExecutable =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "bloom.exe");

    public bool IsBusy => _busy.CurrentCount == 0;

    public async Task LaunchAsync(LaunchRequest request)
    {
        if (!await _busy.WaitAsync(0))
        {
            _logger.LogInformation("Launch ignored, another launch is running");
            return;
        }
        try
        {
            _logger.LogInformation("Launch requested: {Request}", request.ToLogString());
            if (_settings.BootstrapperEnabled && await TryBootstrapperAsync(request)) return;
            await LaunchOfficialAsync(request);
        }
        finally
        {
            _busy.Release();
            _events.Publish(new LaunchProgressChanged(LaunchPhase.Idle, 0));
        }
    }

    /// <summary>Downloads the current client if needed, without starting it.</summary>
    public async Task<InstalledClient?> UpdateClientAsync()
    {
        if (!await _busy.WaitAsync(0)) return null;
        try
        {
            return await _bootstrapper.EnsureCurrentAsync(Progress());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Client update failed");
            _events.Publish(new LaunchProgressChanged(LaunchPhase.Failed, 0, ex.Message));
            return null;
        }
        finally
        {
            _busy.Release();
            _events.Publish(new LaunchProgressChanged(LaunchPhase.Idle, 0));
        }
    }

    private async Task<bool> TryBootstrapperAsync(LaunchRequest request)
    {
        try
        {
            var client = await _bootstrapper.EnsureCurrentAsync(Progress());
            await _preparation.PrepareAsync(client.Directory, client.PlayerExe, request.PlaceId);

            _events.Publish(new LaunchProgressChanged(LaunchPhase.Starting, 1));
            using var process = _bootstrapper.Launch(client, request);
            var exited = await Task.Run(() => process.WaitForExit(EarlyExitWindow));
            if (exited && process.ExitCode != 0)
                throw new BootstrapperException(BootstrapperFailure.LaunchFailed, $"Roblox exited immediately with code {process.ExitCode}");

            if (_settings.BootstrapperFailures != 0) _settings.BootstrapperFailures = 0;
            _bootstrapper.CleanupExcept(client);
            return true;
        }
        catch (Exception ex)
        {
            var failures = _settings.BootstrapperFailures + 1;
            _settings.BootstrapperFailures = failures;
            _logger.LogWarning(ex, "Bootstrapper failed ({Failures}/{Max}), falling back to the official installation", failures, MaxFailures);
            _events.Publish(new LaunchProgressChanged(LaunchPhase.Fallback, 0, ex.Message));

            if (failures >= MaxFailures)
            {
                SetBootstrapperEnabled(false);
                _events.Publish(new BootstrapperPaused(ex.Message));
            }
            return false;
        }
    }

    private async Task LaunchOfficialAsync(LaunchRequest request)
    {
        var backup = _settings.ProtocolBackup;
        var official = RobloxInstallation.FindOfficial(BloomExecutable, backup.GetValueOrDefault("roblox-player"));
        if (official.VersionDirectory is not null)
            await _preparation.PrepareAsync(official.VersionDirectory, official.PlayerExe, request.PlaceId);

        var argument = request.OpensAppOnly ? null : request.ToDeepLink();
        try
        {
            var scheme = argument?.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) == true ? "roblox-player" : "roblox";
            var handler = backup.GetValueOrDefault(scheme);
            var bloomOwnsProtocol = ProtocolRegistration.IsRegisteredTo(BloomExecutable);

            if (argument is not null && !bloomOwnsProtocol)
            {
                // The official handler is registered: let Windows route the link.
                Process.Start(new ProcessStartInfo(argument) { UseShellExecute = true })?.Dispose();
            }
            else if (argument is not null && handler is not null && RobloxInstallation.ExecutableFromCommand(handler) is { } exe && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe, RobloxInstallation.ArgumentsFromCommand(handler, argument)) { UseShellExecute = false })?.Dispose();
            }
            else if (official.LauncherPath is not null)
            {
                var info = new ProcessStartInfo(official.LauncherPath) { UseShellExecute = false };
                if (argument is not null) info.ArgumentList.Add(argument);
                Process.Start(info)?.Dispose();
            }
            else if (official.PlayerExe is { } player && File.Exists(player))
            {
                var info = new ProcessStartInfo(player) { UseShellExecute = false, WorkingDirectory = official.VersionDirectory };
                info.ArgumentList.Add(argument ?? "--app");
                Process.Start(info)?.Dispose();
            }
            else
            {
                _logger.LogWarning("No official Roblox installation found");
                _events.Publish(new LaunchProgressChanged(LaunchPhase.Failed, 0, Strings.Get("LaunchNoRoblox")));
                Process.Start(new ProcessStartInfo("https://www.roblox.com/download") { UseShellExecute = true })?.Dispose();
                return;
            }
            _events.Publish(new LaunchProgressChanged(LaunchPhase.Starting, 1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Official launch failed");
            _events.Publish(new LaunchProgressChanged(LaunchPhase.Failed, 0, ex.Message));
        }
    }

    /// <summary>Switches the bootstrapper and the protocol registration together.</summary>
    public void SetBootstrapperEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var previous = ProtocolRegistration.Register(BloomExecutable);
                var backup = _settings.ProtocolBackup;
                foreach (var (scheme, command) in previous)
                {
                    // Keep an existing backup; only fill gaps.
                    if (command is not null || !backup.ContainsKey(scheme)) backup[scheme] = command ?? backup.GetValueOrDefault(scheme);
                }
                _settings.ProtocolBackup = backup;
                _settings.BootstrapperFailures = 0;
            }
            else
            {
                ProtocolRegistration.Restore(_settings.ProtocolBackup, BloomExecutable);
            }
            _settings.BootstrapperEnabled = enabled;
            _logger.LogInformation("Bootstrapper {State}", enabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Changing the protocol registration failed");
            throw;
        }
    }

    /// <summary>On start: re-claim the protocol if the official launcher took it back.</summary>
    public void EnsureProtocolRegistration()
    {
        if (!_settings.BootstrapperEnabled) return;
        try
        {
            if (!ProtocolRegistration.IsRegisteredTo(BloomExecutable))
            {
                _logger.LogInformation("Protocol handler was changed by another program, registering bloom again");
                SetBootstrapperEnabled(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Protocol check failed");
        }
    }

    private IProgress<BootstrapProgress> Progress() => new SyncProgress<BootstrapProgress>(p =>
        _events.Publish(new LaunchProgressChanged(p.Stage switch
        {
            BootstrapStage.Checking => LaunchPhase.Checking,
            BootstrapStage.Downloading => LaunchPhase.Downloading,
            BootstrapStage.Extracting => LaunchPhase.Extracting,
            _ => LaunchPhase.Starting,
        }, p.Fraction)));

    /// <summary>IProgress without SynchronizationContext capture (the UI marshals itself).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
