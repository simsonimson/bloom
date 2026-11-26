using Bloom.Core.Voice;
using Bloom.Platform.Windows.Audio;
using Bloom.Voice.Strategies;

namespace Bloom.Voice;

/// <summary>Restores what an earlier, unexpectedly ended bloom left behind. Shared by bloom and Bloom.Guard.</summary>
public static class JournalRecovery
{
    /// <summary>bloom start: an unfinished journal is always cleaned up first (Blueprint 6.5).</summary>
    public static void CleanupOnStartup(JournalStore store, AudioSystem audio, Func<int, bool> isRoblox)
    {
        var journal = store.Read();
        if (journal is null) return;
        audio.GetCaptureDevices();
        switch (journal.Method)
        {
            case PttMethod.SystemWide:
                if (journal.Active) RestoreEndpoint(journal, audio);
                store.Delete();
                break;
            case PttMethod.RobloxOnly:
                if (!TryUnmuteSessions(store, audio, isRoblox))
                    store.Write(journal with { Active = false, PendingSessionUnmute = true, WrittenAt = DateTimeOffset.UtcNow });
                break;
            default:
                store.Delete();
                break;
        }
    }

    /// <summary>Undoes a remembered Roblox session mute once Roblox has a capture session again.</summary>
    public static bool TryUnmuteSessions(JournalStore store, AudioSystem audio, Func<int, bool> isRoblox)
    {
        if (SessionMuteStrategy.Apply(audio, isRoblox, false) == 0) return false;
        store.Delete();
        return true;
    }

    public static void RestoreEndpoint(AudioJournal journal, AudioSystem audio)
    {
        if (journal.DeviceId is not { } id || journal.OriginalEndpointMute is not bool original || !audio.DeviceExists(id)) return;
        try { audio.SetEndpointMute(id, original); }
        catch (AudioException) { /* device went away */ }
    }
}
