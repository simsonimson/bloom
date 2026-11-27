using Bloom.Core.Performance;
using Bloom.Launcher.FastFlags;

namespace Bloom.Performance;

/// <summary>
/// FastFlag presets. Only flags on Roblox's official allowlist are written; anything else would
/// be ignored by Roblox anyway (allowlist enforced since 29.09.2025).
/// </summary>
public static class GraphicsPresets
{
    public const string FpsFlag = "DFIntTaskSchedulerTargetFps";
    public const int Unlimited = 9999;

    /// <summary>Every flag a preset may set; used to tell preset flags from the user's own.</summary>
    public static readonly IReadOnlySet<string> KnownFlags = new HashSet<string>(StringComparer.Ordinal)
    {
        FpsFlag,
        "FFlagDisablePostFx",
        "FIntDebugForceMSAASamples",
        "DFFlagTextureQualityOverrideEnabled",
        "DFIntTextureQualityOverride",
        "FIntRenderShadowIntensity",
        "FIntFRMMinGrassDistance",
        "FIntFRMMaxGrassDistance",
        "FIntRenderGrassDetailStrands",
    };

    /// <summary>Flags of a preset before the allowlist filter. fpsCap 0 = the preset's own limit.</summary>
    public static IReadOnlyList<FastFlag> Flags(GraphicsPreset preset, int fpsCap, int? refreshRate)
    {
        var flags = new List<FastFlag>();
        switch (preset)
        {
            case GraphicsPreset.MaxFps:
                flags.Add(new("FFlagDisablePostFx", "True"));
                flags.Add(new("FIntDebugForceMSAASamples", "0"));
                flags.Add(new("DFFlagTextureQualityOverrideEnabled", "True"));
                flags.Add(new("DFIntTextureQualityOverride", "0"));
                flags.Add(new("FIntRenderShadowIntensity", "0"));
                flags.Add(new("FIntFRMMinGrassDistance", "0"));
                flags.Add(new("FIntFRMMaxGrassDistance", "0"));
                flags.Add(new("FIntRenderGrassDetailStrands", "0"));
                break;
            case GraphicsPreset.Battery:
                flags.Add(new("FFlagDisablePostFx", "True"));
                flags.Add(new("DFFlagTextureQualityOverrideEnabled", "True"));
                flags.Add(new("DFIntTextureQualityOverride", "1"));
                flags.Add(new("FIntRenderShadowIntensity", "0"));
                break;
        }

        var fps = fpsCap > 0 ? fpsCap : preset switch
        {
            GraphicsPreset.MaxFps => Unlimited,
            GraphicsPreset.Balanced => refreshRate is >= 30 and <= 500 ? refreshRate.Value : 144,
            GraphicsPreset.Battery => 30,
            _ => 0,
        };
        if (fps > 0) flags.Add(new(FpsFlag, fps.ToString()));
        return flags;
    }

    /// <summary>
    /// What is written to ClientAppSettings.json: preset flags (allowlisted only, if the allowlist is
    /// known) plus the user's own flags, which win on the same name.
    /// </summary>
    public static IReadOnlyList<FastFlag> Compose(IEnumerable<FastFlag> custom, GraphicsPreset preset, int fpsCap, int? refreshRate,
        FastFlagAllowlist allowlist)
    {
        var result = new Dictionary<string, FastFlag>(StringComparer.Ordinal);
        foreach (var flag in Flags(preset, fpsCap, refreshRate))
        {
            if (allowlist.IsAvailable && !allowlist.Names.Contains(flag.Name)) continue;
            result[flag.Name] = flag;
        }
        foreach (var flag in custom) result[flag.Name] = flag;
        return result.Values.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>How many of a preset's flags Roblox accepts (for the UI).</summary>
    public static (int Allowed, int Total) Coverage(GraphicsPreset preset, int fpsCap, int? refreshRate, FastFlagAllowlist allowlist)
    {
        var flags = Flags(preset, fpsCap, refreshRate);
        return allowlist.IsAvailable ? (flags.Count(f => allowlist.Names.Contains(f.Name)), flags.Count) : (flags.Count, flags.Count);
    }
}
