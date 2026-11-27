namespace Bloom.Core.Performance;

public enum ProcessPriority { Normal = 0, AboveNormal = 1, High = 2 }

/// <summary>FastFlag presets (only flags on Roblox's allowlist are written).</summary>
public enum GraphicsPreset { Default = 0, MaxFps = 1, Balanced = 2, Battery = 3 }

public enum OverlayCorner { TopRight = 0, TopLeft = 1, BottomRight = 2, BottomLeft = 3 }

/// <summary>One measurement of the running Roblox client. Null = not measurable on this PC.</summary>
public sealed record PerformanceSample(
    DateTimeOffset At,
    double CpuPercent,
    long WorkingSetBytes,
    double? GpuPercent,
    long? GpuMemoryBytes,
    int? PingMs);

public sealed record PerformanceSampled(PerformanceSample? Sample);

/// <summary>What the performance mode currently changed. Detail lists steps that could not be applied.</summary>
public sealed record PerformanceModeChanged(bool Active, IReadOnlyList<string> Skipped);

public enum HotkeyAction
{
    Rejoin,
    CopyServerLink,
    ToggleOverlay,
    TogglePerformanceMode,
    TogglePushToTalk,
    ShowBloom,
}

/// <summary>Same values as the Win32 MOD_* flags.</summary>
[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

public sealed record HotkeyBinding(HotkeyModifiers Modifiers, int Key);

public sealed record HotkeyPressed(HotkeyAction Action);

/// <summary>
/// Settings for one experience (by universe ID, so every place of the game shares it).
/// Null values mean "use the normal setting".
/// </summary>
public sealed record GameProfile(long UniverseId, string Name, GraphicsPreset? Preset, bool? PerformanceMode, bool? PushToTalk);
