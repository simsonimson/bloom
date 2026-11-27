using Bloom.Activity;
using Bloom.Core.Performance;
using Bloom.Core.Settings;
using Bloom.Launcher.FastFlags;
using Bloom.Performance;
using Bloom.Platform.Windows.Hardware;
using Microsoft.Extensions.Logging;

namespace Bloom.App.Services;

/// <summary>
/// Everything that has to happen right before Roblox starts: FastFlags = preset (or the game
/// profile's preset) + own flags, and the GPU preference of the performance mode.
/// </summary>
public sealed class LaunchPreparation(
    AppSettings settings,
    FastFlagService fastFlags,
    GameProfileStore profiles,
    ExperienceResolver resolver,
    PerformanceModeService performance,
    ILogger<LaunchPreparation> logger)
{
    public async Task PrepareAsync(string versionDirectory, string? playerExe, long? placeId)
    {
        GraphicsPreset? profilePreset = null;
        if (placeId is long place)
        {
            try
            {
                var info = await resolver.ResolveAsync(place);
                if (info is not null) profilePreset = profiles.Find(info.UniverseId)?.Preset;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Experience lookup for the game profile failed");
            }
        }
        await ApplyFlagsAsync(versionDirectory, profilePreset);
        if (playerExe is not null) performance.PrepareLaunch(playerExe);
    }

    /// <summary>Writes the effective flags. Used before launches and when flags or the preset change.</summary>
    public async Task ApplyFlagsAsync(string versionDirectory, GraphicsPreset? presetOverride = null)
    {
        try
        {
            var allowlist = await fastFlags.GetAllowlistAsync();
            var flags = GraphicsPresets.Compose(fastFlags.Load(), presetOverride ?? settings.PerfPreset, settings.PerfFpsCap,
                DisplayInfo.PrimaryRefreshRate(), allowlist);
            fastFlags.Apply(versionDirectory, flags);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FastFlags could not be written to {Dir}", versionDirectory);
        }
    }
}
