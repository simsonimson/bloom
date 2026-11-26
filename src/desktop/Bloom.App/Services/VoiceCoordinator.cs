using System.Diagnostics;
using Bloom.Core;
using Bloom.Core.Events;
using Bloom.Core.Settings;
using Bloom.Voice;
using Microsoft.Extensions.Logging;

namespace Bloom.App.Services;

/// <summary>
/// App side of Bloom Voice: keeps the engine in sync with the settings, starts Bloom.Guard and
/// performs the clean exit (Blueprint 6.6: PTT safe, journal closed, guard told "clean").
/// </summary>
public sealed class VoiceCoordinator : IDisposable
{
    private readonly VoiceService _voice;
    private readonly AppSettings _settings;
    private readonly BloomPaths _paths;
    private readonly ILogger<VoiceCoordinator> _logger;
    private readonly IDisposable _subscription;
    private readonly EventWaitHandle _cleanExit;
    private bool _exited;

    public VoiceCoordinator(VoiceService voice, AppSettings settings, BloomPaths paths, IEventBus events, ILogger<VoiceCoordinator> logger)
    {
        _voice = voice;
        _settings = settings;
        _paths = paths;
        _logger = logger;
        _cleanExit = new EventWaitHandle(false, EventResetMode.ManualReset, GuardNames.CleanEvent(Environment.ProcessId));
        _subscription = events.Subscribe<SettingChanged>(e =>
        {
            if (AppSettings.Keys.IsVoiceKey(e.Key)) _voice.UpdateConfig(_settings.PttConfig);
        });
    }

    /// <summary>Order from Blueprint 6.6: take over from an old guard, clean the journal (voice start), start the guard.</summary>
    public void Start(IEnumerable<int> runningRobloxPids)
    {
        TakeOverFromPreviousGuard();
        _voice.Start(_settings.PttConfig, runningRobloxPids);
        StartGuard();
    }

    /// <summary>Clean exit. Safe to call more than once.</summary>
    public void Exit()
    {
        if (_exited) return;
        _exited = true;
        try { _voice.Shutdown(); }
        catch (Exception ex) { _logger.LogError(ex, "Voice shutdown failed"); }
        _cleanExit.Set();
    }

    private void TakeOverFromPreviousGuard()
    {
        try
        {
            using var handover = new EventWaitHandle(false, EventResetMode.ManualReset, GuardNames.HandoverEvent);
            handover.Set();
            using var recovering = new Mutex(false, GuardNames.RecoveringMutex);
            bool acquired;
            try { acquired = recovering.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) recovering.ReleaseMutex();
            handover.Reset();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guard handover failed");
        }
    }

    private void StartGuard()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Bloom.Guard.exe");
        if (!File.Exists(exe))
        {
            _logger.LogWarning("Bloom.Guard.exe not found next to bloom.exe; crash recovery relies on the next start");
            return;
        }
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("--pid");
            info.ArgumentList.Add(Environment.ProcessId.ToString());
            info.ArgumentList.Add("--journal");
            info.ArgumentList.Add(_paths.AudioJournal);
            info.ArgumentList.Add("--icon");
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "bloom-ptt-error.ico"));
            info.ArgumentList.Add("--lang");
            info.ArgumentList.Add(Strings.Get("GuardLanguage"));
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bloom.Guard could not be started");
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _cleanExit.Dispose();
    }
}
