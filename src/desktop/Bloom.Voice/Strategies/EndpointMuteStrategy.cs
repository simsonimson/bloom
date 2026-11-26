using Bloom.Core.Voice;
using Bloom.Platform.Windows.Audio;

namespace Bloom.Voice.Strategies;

/// <summary>
/// B: mutes the microphone endpoint Roblox uses (IAudioEndpointVolume::SetMute). Officially
/// documented and reliable, but every app on that microphone is affected.
/// </summary>
public sealed class EndpointMuteStrategy(AudioSystem audio, JournalStore journal, RobloxProcesses roblox, Func<PttConfig> config)
    : IMuteStrategy
{
    private bool _original;

    public PttMethod Method => PttMethod.SystemWide;
    public bool SupportsHold => true;

    /// <summary>The microphone currently controlled.</summary>
    public string? DeviceId { get; private set; }

    public void Engage()
    {
        audio.GetCaptureDevices();
        var target = ResolveTarget() ?? throw new NoCaptureDeviceException();
        Take(target);
    }

    public void SetMuted(bool muted)
    {
        if (DeviceId is null) throw new NoCaptureDeviceException();
        audio.SetEndpointMute(DeviceId, muted);
    }

    public void Refresh(bool muted)
    {
        audio.GetCaptureDevices();
        var target = ResolveTarget() ?? throw new NoCaptureDeviceException();
        if (!string.Equals(target, DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            RestoreCurrent();
            Take(target);
            audio.SetEndpointMute(target, muted);
            return;
        }
        // Enforce muted only; if the user muted the microphone themselves while talking, leave it.
        if (muted && !audio.GetEndpointMute(target)) audio.SetEndpointMute(target, true);
    }

    public void Release(bool robloxExited, bool keepMuted)
    {
        if (!keepMuted) RestoreCurrent();
        DeviceId = null;
        journal.Delete();
    }

    private void Take(string deviceId)
    {
        _original = audio.GetEndpointMute(deviceId);
        journal.Write(new AudioJournal(PttMethod.SystemWide, deviceId, _original, Active: true, PendingSessionUnmute: false,
            Environment.ProcessId, DateTimeOffset.UtcNow));
        DeviceId = deviceId;
    }

    private void RestoreCurrent()
    {
        if (DeviceId is null || !audio.DeviceExists(DeviceId)) return;
        try { audio.SetEndpointMute(DeviceId, _original); }
        catch (AudioException) { /* device went away */ }
    }

    /// <summary>The microphone Roblox has a session on; otherwise the chosen one; otherwise the Windows default.</summary>
    private string? ResolveTarget()
    {
        var preferred = config().DeviceId;
        var used = audio.GetCaptureSessions(roblox.Contains).Select(s => s.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (used.Count > 0)
            return used.FirstOrDefault(d => string.Equals(d, preferred, StringComparison.OrdinalIgnoreCase)) ?? used[0];
        if (preferred is not null && audio.DeviceExists(preferred)) return preferred;
        return audio.GetDefaultCaptureId(communications: false);
    }
}
