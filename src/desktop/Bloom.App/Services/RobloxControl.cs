using System.Diagnostics;
using Bloom.Activity;
using Bloom.Launcher.Links;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace Bloom.App.Services;

/// <summary>Rejoin, server switching and the shareable server link (Phase 5, utility).</summary>
public sealed class RobloxControl(ActivityTracker activity, ProcessWatcher processes, LaunchCoordinator launcher, ILogger<RobloxControl> logger)
{
    /// <summary>Same server (or a new one), after closing the running client.</summary>
    public async Task<bool> RejoinAsync(bool sameServer)
    {
        var state = activity.State;
        if (state.PlaceId is not long place) return false;
        var request = sameServer && state.JobId is { Length: > 0 } job ? LaunchRequest.ForServer(place, job) : LaunchRequest.ForPlace(place);
        await CloseRobloxAsync();
        await launcher.LaunchAsync(request);
        return true;
    }

    /// <summary>Joins a server from the browser; a running client is closed first.</summary>
    public async Task JoinServerAsync(long placeId, string jobId)
    {
        await CloseRobloxAsync();
        await launcher.LaunchAsync(LaunchRequest.ForServer(placeId, jobId));
    }

    /// <summary>Copies the web link of the current server. Call on the UI thread.</summary>
    public bool CopyServerLink()
    {
        var state = activity.State;
        if (state.PlaceId is not long place || state.JobId is not { Length: > 0 } job) return false;
        try
        {
            var package = new DataPackage();
            package.SetText(LaunchRequest.ServerWebLink(place, job));
            Clipboard.SetContent(package);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Clipboard unavailable");
            return false;
        }
    }

    /// <summary>Asks Roblox to close like the window's X button, and ends it only if it does not react.</summary>
    public async Task CloseRobloxAsync()
    {
        var pids = processes.RunningProcessIds.ToList();
        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.CloseMainWindow();
                if (!await Task.Run(() => process.WaitForExit(8000)))
                {
                    logger.LogInformation("Roblox {Pid} did not close, ending it", pid);
                    process.Kill();
                    await Task.Run(() => process.WaitForExit(3000));
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }
        if (pids.Count > 0) await Task.Delay(1500); // let Roblox release its single-instance lock
    }
}
