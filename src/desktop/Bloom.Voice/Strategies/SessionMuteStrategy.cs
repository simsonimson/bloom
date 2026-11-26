using Bloom.Core.Voice;
using Bloom.Platform.Windows.Audio;

namespace Bloom.Voice.Strategies;

/// <summary>
/// A: mutes only Roblox's capture sessions (ISimpleAudioVolume::SetMute). Experimental: whether
/// Roblox really receives silence is not documented (spike S1).
/// </summary>
public sealed class SessionMuteStrategy(AudioSystem audio, JournalStore journal, RobloxProcesses roblox) : IMuteStrategy
{
    public PttMethod Method => PttMethod.RobloxOnly;
    public bool SupportsHold => true;

    public void Engage() =>
        journal.Write(new AudioJournal(PttMethod.RobloxOnly, null, null, Active: true, PendingSessionUnmute: false,
            Environment.ProcessId, DateTimeOffset.UtcNow));

    public void SetMuted(bool muted) => Apply(audio, roblox.Contains, muted);

    public void Refresh(bool muted)
    {
        audio.GetCaptureDevices();
        Apply(audio, roblox.Contains, muted);
    }

    public void Release(bool robloxExited, bool keepMuted)
    {
        if (!robloxExited)
        {
            Apply(audio, roblox.Contains, false);
            journal.Delete();
            return;
        }
        // Windows may remember the session mute for the next Roblox start: undo it then.
        journal.Write(new AudioJournal(PttMethod.RobloxOnly, null, null, Active: false, PendingSessionUnmute: true,
            Environment.ProcessId, DateTimeOffset.UtcNow));
    }

    /// <summary>Sets the mute of all Roblox capture sessions. Returns how many sessions there were.</summary>
    public static int Apply(AudioSystem audio, Func<int, bool> isRoblox, bool muted)
    {
        var sessions = audio.GetCaptureSessions(isRoblox);
        foreach (var session in sessions)
        {
            if (session.GetMute() != muted) session.SetMute(muted);
        }
        return sessions.Count;
    }
}
