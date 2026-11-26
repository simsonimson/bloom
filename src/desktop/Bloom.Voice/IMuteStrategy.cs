using Bloom.Core.Voice;

namespace Bloom.Voice;

/// <summary>How the microphone is silenced (Blueprint 6.5). All calls happen on the voice thread.</summary>
public interface IMuteStrategy
{
    PttMethod Method { get; }

    /// <summary>False for methods that can only switch (Roblox button): the engine then forces Toggle.</summary>
    bool SupportsHold { get; }

    /// <summary>Records the original state and writes the journal. Does not change the mute state.</summary>
    void Engage();

    /// <summary>
    /// Applies the state. Throws <see cref="MuteActionFailedException"/> when a single action was not
    /// carried out (state unchanged); any other exception puts the engine into Error.
    /// </summary>
    void SetMuted(bool muted);

    /// <summary>Re-applies the state to new devices or sessions. Throws while the device is unusable.</summary>
    void Refresh(bool muted);

    /// <summary>Restores the original state (or keeps the microphone muted) and closes the journal.</summary>
    void Release(bool robloxExited, bool keepMuted);
}

/// <summary>Strategies that cannot read the real state and track an assumption.</summary>
public interface IAssumedStateStrategy
{
    void Assume(bool muted);
}

public sealed class MuteActionFailedException(MuteFailure reason) : Exception($"Mute action failed: {reason}")
{
    public MuteFailure Reason { get; } = reason;
}

/// <summary>No microphone is available for the system-wide method.</summary>
public sealed class NoCaptureDeviceException() : Exception("No active microphone");
