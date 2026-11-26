using System.Diagnostics;
using Bloom.Core.Voice;
using Bloom.Platform.Windows;
using Bloom.Platform.Windows.Audio;
using Bloom.Voice;
using Bloom.Voice.Strategies;
using Microsoft.Win32.SafeHandles;

namespace Bloom.Guard;

/// <summary>
/// Bloom.Guard --pid &lt;bloom pid&gt; --journal &lt;path&gt; [--icon &lt;path&gt;] [--lang de|en]
/// Waits for bloom. If bloom ends without signalling a clean exit, it restores the safe audio
/// state from the journal (Blueprint 6.5): Roblox-only → mute Roblox's sessions; system-wide →
/// keep the microphone muted while Roblox runs, then restore the original state.
/// </summary>
internal static class Program
{
    private const string RobloxProcessName = "RobloxPlayerBeta";

    private static int Main(string[] args)
    {
        var options = GuardOptions.Parse(args);
        if (options is null) return 2;

        if (!EventWaitHandle.TryOpenExisting(GuardNames.CleanEvent(options.BloomProcessId), out var clean)) return 3;
        using (clean)
        {
            Process bloom;
            try { bloom = Process.GetProcessById(options.BloomProcessId); }
            catch (ArgumentException) { return clean.WaitOne(0) ? 0 : Recover(options); }

            using (bloom)
            using (var exited = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(bloom.Handle, ownsHandle: false) })
            {
                if (WaitHandle.WaitAny([clean, exited]) == 0 || clean.WaitOne(0)) return 0;
            }
        }
        return Recover(options);
    }

    private static int Recover(GuardOptions options)
    {
        var store = new JournalStore(options.JournalPath);
        var journal = store.Read();
        if (journal is not { Active: true }) return 0;

        using var recovering = new Mutex(false, GuardNames.RecoveringMutex);
        var owns = false;
        try { owns = recovering.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
        using var handover = new EventWaitHandle(false, EventResetMode.ManualReset, GuardNames.HandoverEvent);

        try
        {
            var text = GuardText.For(options.Language);
            using var audio = new AudioSystem();
            audio.GetCaptureDevices();
            switch (journal.Method)
            {
                case PttMethod.RobloxOnly:
                    SessionMuteStrategy.Apply(audio, IsRoblox(), true);
                    store.Write(journal with { Active = false, PendingSessionUnmute = true, WrittenAt = DateTimeOffset.UtcNow });
                    Notify(options, text.Title, text.SessionMuted, TimeSpan.FromSeconds(8));
                    break;

                case PttMethod.SystemWide:
                    HoldEndpointMuted(options, store, journal, audio, handover, text);
                    break;

                default:
                    store.Delete();
                    break;
            }
        }
        finally
        {
            if (owns) recovering.ReleaseMutex();
        }
        return 0;
    }

    private static void HoldEndpointMuted(GuardOptions options, JournalStore store, AudioJournal journal, AudioSystem audio,
        EventWaitHandle handover, GuardText text)
    {
        using var tray = TryCreateTray(options, text.HoldingTooltip);
        tray?.ShowNotification(text.Title, text.EndpointMuted);
        while (true)
        {
            if (handover.WaitOne(0)) return; // a new bloom takes over and restores the journal itself

            if (!IsRobloxRunning())
            {
                audio.GetCaptureDevices();
                JournalRecovery.RestoreEndpoint(journal, audio);
                store.Delete();
                return;
            }

            try
            {
                audio.GetCaptureDevices();
                if (journal.DeviceId is { } id && audio.DeviceExists(id) && !audio.GetEndpointMute(id)) audio.SetEndpointMute(id, true);
            }
            catch (AudioException)
            {
                // Device unplugged; retry on the next round.
            }
            MessagePump.Run(TimeSpan.FromSeconds(1));
        }
    }

    private static bool IsRobloxRunning()
    {
        var processes = Process.GetProcessesByName(RobloxProcessName);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private static Func<int, bool> IsRoblox()
    {
        var pids = Process.GetProcessesByName(RobloxProcessName).Select(p => { using (p) return p.Id; }).ToHashSet();
        return pids.Contains;
    }

    private static void Notify(GuardOptions options, string title, string message, TimeSpan visible)
    {
        using var tray = TryCreateTray(options, title);
        if (tray is null) return;
        tray.ShowNotification(title, message);
        MessagePump.Run(visible);
    }

    private static TrayIcon? TryCreateTray(GuardOptions options, string tooltip)
    {
        if (options.IconPath is null || !File.Exists(options.IconPath)) return null;
        try { return new TrayIcon(options.IconPath, tooltip); }
        catch (Exception) { return null; }
    }
}

internal sealed record GuardOptions(int BloomProcessId, string JournalPath, string? IconPath, string Language)
{
    public static GuardOptions? Parse(string[] args)
    {
        string? Value(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (!int.TryParse(Value("--pid"), out var pid) || Value("--journal") is not { Length: > 0 } journal) return null;
        return new GuardOptions(pid, journal, Value("--icon"), Value("--lang") ?? "de");
    }
}

internal sealed record GuardText(string Title, string SessionMuted, string EndpointMuted, string HoldingTooltip)
{
    public static GuardText For(string language) => language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
        ? new("bloom closed unexpectedly",
            "Roblox's microphone has been muted for safety. Start bloom again to continue using push-to-talk.",
            "Your microphone stays muted while Roblox is running and is restored afterwards. Start bloom again to continue.",
            "bloom Guard: microphone muted until Roblox closes")
        : new("bloom wurde unerwartet beendet",
            "Das Roblox-Mikrofon wurde sicherheitshalber stummgeschaltet. Starte bloom neu, um Push-to-Talk weiter zu nutzen.",
            "Dein Mikrofon bleibt stumm, solange Roblox läuft, und wird danach wiederhergestellt. Starte bloom neu, um weiterzumachen.",
            "bloom Guard: Mikrofon stumm, bis Roblox beendet wird");
}
