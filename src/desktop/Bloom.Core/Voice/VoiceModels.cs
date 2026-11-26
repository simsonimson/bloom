namespace Bloom.Core.Voice;

/// <summary>Hold = microphone open while the key is held. Toggle = each press switches.</summary>
public enum PttMode
{
    Hold = 0,
    Toggle = 1,
}

/// <summary>How bloom silences the microphone (Blueprint 6.5).</summary>
public enum PttMethod
{
    /// <summary>B: mute the microphone endpoint Roblox uses. Reliable, affects every app on that microphone.</summary>
    SystemWide = 0,

    /// <summary>A: mute only Roblox's capture session. Experimental (spike S1 unconfirmed).</summary>
    RobloxOnly = 1,

    /// <summary>D: click Roblox's own microphone button. Toggle only, needs calibration.</summary>
    RobloxButton = 2,
}

/// <summary>What happens to the microphone when Roblox closes (system-wide method).</summary>
public enum RobloxExitBehavior
{
    Restore = 0,
    KeepMuted = 1,
}

/// <summary>
/// Engine state. Ready means muted. Any unclear situation goes to Ready or Error, never to
/// Transmitting (fail-closed).
/// </summary>
public enum PttState
{
    Off = 0,
    Ready = 1,
    Transmitting = 2,
    Open = 3,
    Error = 4,
}

/// <summary>Why a single action of the Roblox-button method did not happen. The state stays unchanged.</summary>
public enum MuteFailure
{
    NotCalibrated,
    RobloxWindowNotFound,
    CouldNotFocus,
    MouseLocked,
}

/// <summary>
/// Position of Roblox' microphone button relative to the top-left of the Roblox client area,
/// in device-independent pixels (96 dpi), so it survives DPI changes and window resizes.
/// </summary>
public sealed record ButtonPosition(double X, double Y);

public sealed record PttConfig(
    bool Enabled,
    PttMode Mode,
    int Key,
    PttMethod Method,
    bool ForegroundOnly,
    string? DeviceId,
    RobloxExitBehavior OnRobloxExit,
    ButtonPosition? Button,
    bool Sounds)
{
    public static PttConfig Default { get; } = new(false, PttMode.Hold, 0, PttMethod.SystemWide, true, null, RobloxExitBehavior.Restore, null, false);
}

/// <summary>Published on every engine state change.</summary>
public sealed record PttStateChanged(PttState State, PttMethod Method);

/// <summary>Published when a Roblox-button action could not be carried out.</summary>
public sealed record PttActionFailed(MuteFailure Reason);
