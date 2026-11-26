using System.Collections.Concurrent;
using Bloom.Core.Events;
using Bloom.Core.Roblox;
using Bloom.Core.Voice;
using Bloom.Platform.Windows.Audio;
using Bloom.Platform.Windows.Input;
using Bloom.Voice.Strategies;

namespace Bloom.Voice;

public sealed record VoiceDevice(string Id, string Name, bool IsDefault, bool UsedByRoblox, float Level);

/// <summary>What the UI shows; replaced about ten times per second.</summary>
public sealed record VoiceSnapshot(
    PttState State,
    PttMethod Method,
    PttMode EffectiveMode,
    bool RobloxRunning,
    IReadOnlyList<VoiceDevice> Devices,
    string? ControlledDeviceId,
    string? InputError)
{
    public static VoiceSnapshot Empty { get; } = new(PttState.Off, PttMethod.SystemWide, PttMode.Hold, false, [], null, null);
}

/// <summary>
/// Hosts the PTT engine on its own MTA thread (Core Audio), the Raw Input thread and the
/// 50-ms watchdog. Everything that touches the engine is queued onto the voice thread.
/// </summary>
public sealed class VoiceService : IDisposable
{
    private const int VkLeftButton = 0x01;
    private const int VkEscape = 0x1B;

    private readonly IEventBus _events;
    private readonly JournalStore _journal;
    private readonly Action<string, Exception?> _log;
    private readonly RobloxProcesses _roblox = new();
    private readonly BlockingCollection<Action> _queue = new();
    private readonly List<IDisposable> _subscriptions = new();
    private Thread? _thread;
    private RawInputListener? _input;
    private AudioSystem? _audio;
    private PttEngine? _engine;
    private PttConfig _config = PttConfig.Default;
    private IReadOnlyList<CaptureDeviceInfo> _devices = [];
    private HashSet<string> _usedByRoblox = new(StringComparer.OrdinalIgnoreCase);
    private string? _inputError;
    private volatile VoiceSnapshot _snapshot = VoiceSnapshot.Empty;
    private volatile TaskCompletionSource<int>? _keyCapture;
    private volatile TaskCompletionSource<ButtonPosition?>? _calibration;
    private volatile bool _stopping;

    public VoiceService(IEventBus events, JournalStore journal, Action<string, Exception?> log)
    {
        _events = events;
        _journal = journal;
        _log = log;
    }

    public VoiceSnapshot Snapshot => _snapshot;

    public void Start(PttConfig config, IEnumerable<int> runningRobloxPids)
    {
        if (_thread is not null) return;
        _config = config;
        _roblox.Reset(runningRobloxPids);
        _subscriptions.Add(_events.Subscribe<RobloxProcessStarted>(e => { _roblox.Add(e.ProcessId); Post(UpdateRoblox); }));
        _subscriptions.Add(_events.Subscribe<RobloxProcessExited>(e => { _roblox.Remove(e.ProcessId); Post(UpdateRoblox); }));

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Voice", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        _input = new RawInputListener(OnInput);
        if (!_input.Start())
        {
            _inputError = _input.StartupError;
            _log($"Raw input unavailable: {_inputError}", null);
        }
    }

    public void UpdateConfig(PttConfig config) => Post(() =>
    {
        _config = config;
        _engine?.Configure(config);
    });

    public void ToggleFromUi() => Post(() => _engine?.ToggleFromUi());

    public void CorrectAssumedState() => Post(() => _engine?.CorrectAssumedState());

    /// <summary>Next key or mouse button (not the left button). Esc cancels and returns 0.</summary>
    public Task<int> CaptureKeyAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetResult(0));
        _keyCapture = tcs;
        return tcs.Task;
    }

    /// <summary>Waits for the user to left-click inside the Roblox window and returns that position.</summary>
    public Task<ButtonPosition?> CalibrateAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ButtonPosition?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetResult(null));
        _calibration = tcs;
        return tcs.Task;
    }

    public Task RunMicTestAsync(string? deviceId, Action<MicTestPhase, double> progress, CancellationToken ct) =>
        MicTest.RunAsync(deviceId, TimeSpan.FromSeconds(5), progress, ct);

    /// <summary>Clean exit: restores the original microphone state and closes the journal.</summary>
    public void Shutdown()
    {
        if (_thread is null || _stopping) return;
        using var done = new ManualResetEventSlim();
        Post(() =>
        {
            try { _engine?.Shutdown(); }
            finally { done.Set(); }
        });
        done.Wait(TimeSpan.FromSeconds(3));
        _stopping = true;
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
        _input?.Dispose();
    }

    public void Dispose()
    {
        Shutdown();
        foreach (var s in _subscriptions) s.Dispose();
    }

    // ------------------------------------------------------------------ voice thread

    private void Run(ManualResetEventSlim ready)
    {
        try
        {
            _audio = new AudioSystem();
            _devices = _audio.GetCaptureDevices();
            _audio.SessionCreated += () => Post(() => _engine?.Poll());
            _audio.WatchSessions();
            try { JournalRecovery.CleanupOnStartup(_journal, _audio, _roblox.Contains); }
            catch (Exception ex) { _log("Journal cleanup failed", ex); }

            _engine = new PttEngine(CreateStrategy);
            _engine.StateChanged += OnStateChanged;
            _engine.ActionFailed += reason => _events.Publish(new PttActionFailed(reason));
            _engine.StrategyError += ex => _log("PTT strategy error", ex);
            _engine.Configure(_config);
            _engine.SetRobloxRunning(_roblox.Any);
        }
        catch (Exception ex)
        {
            _log("Voice thread could not start", ex);
            _inputError ??= ex.Message;
        }
        finally
        {
            ready.Set();
        }

        long nextWatchdog = 0, nextLevels = 0, nextPoll = 0;
        while (!_queue.IsCompleted)
        {
            try
            {
                if (_queue.TryTake(out var work, 25)) work();
                var now = Environment.TickCount64;
                if (now >= nextWatchdog)
                {
                    nextWatchdog = now + 50;
                    _engine?.Watchdog(RawInputListener.IsPhysicallyDown);
                }
                if (now >= nextPoll)
                {
                    nextPoll = now + 1000;
                    PollAudio();
                }
                if (now >= nextLevels)
                {
                    nextLevels = now + 100;
                    PublishSnapshot();
                }
            }
            catch (Exception ex)
            {
                _log("Voice loop error", ex);
            }
        }

        _audio?.Dispose();
    }

    private IMuteStrategy CreateStrategy(PttMethod method) => method switch
    {
        PttMethod.RobloxOnly => new SessionMuteStrategy(_audio!, _journal, _roblox),
        PttMethod.RobloxButton => new RobloxButtonStrategy(_roblox, () => _config),
        _ => new EndpointMuteStrategy(_audio!, _journal, _roblox, () => _config),
    };

    private void UpdateRoblox()
    {
        _engine?.SetRobloxRunning(_roblox.Any);
        PollAudio();
    }

    private void PollAudio()
    {
        if (_audio is null) return;
        try
        {
            _devices = _audio.GetCaptureDevices();
            _usedByRoblox = _audio.GetCaptureSessions(_roblox.Contains).Select(s => s.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log("Device enumeration failed", ex);
        }
        _engine?.Poll();

        // A session mute remembered by Windows from an earlier run is undone once Roblox records again.
        var pendingUnmute = _journal.Read() is { PendingSessionUnmute: true };
        var sessionMethodActive = _engine is { State: not PttState.Off, Config.Method: PttMethod.RobloxOnly };
        if (pendingUnmute && !sessionMethodActive && _roblox.Any)
        {
            try { JournalRecovery.TryUnmuteSessions(_journal, _audio, _roblox.Contains); }
            catch (Exception ex) { _log("Session unmute failed", ex); }
        }
    }

    private void PublishSnapshot()
    {
        if (_audio is null || _engine is null) return;
        var used = _usedByRoblox;
        var devices = _devices.Select(d => new VoiceDevice(d.Id, d.Name, d.IsDefault, used.Contains(d.Id), _audio.GetEndpointPeak(d.Id))).ToList();
        _snapshot = new VoiceSnapshot(_engine.State, _config.Method, _engine.EffectiveMode, _engine.RobloxRunning, devices,
            (_engine.Strategy as EndpointMuteStrategy)?.DeviceId, _inputError);
    }

    private void OnStateChanged(PttState from, PttState to)
    {
        _events.Publish(new PttStateChanged(to, _config.Method));
        if (!_config.Sounds) return;
        if (to is PttState.Transmitting or PttState.Open) Tones.PlayOn();
        else if (to == PttState.Ready && from is PttState.Transmitting or PttState.Open) Tones.PlayOff();
    }

    // ------------------------------------------------------------------ input thread

    private void OnInput(RawInputEvent e)
    {
        if (_keyCapture is { } capture && e.Down && e.VirtualKey != VkLeftButton)
        {
            _keyCapture = null;
            capture.TrySetResult(e.VirtualKey == VkEscape ? 0 : e.VirtualKey);
            return;
        }

        if (_calibration is { } calibration && e.Down && e.VirtualKey == VkLeftButton && _roblox.Contains(e.ForegroundProcessId))
        {
            var hwnd = WindowControl.ForegroundWindow();
            var (sx, sy) = WindowControl.CursorPosition();
            var (cx, cy) = WindowControl.ScreenToClient(hwnd, sx, sy);
            var (w, h) = WindowControl.ClientSize(hwnd);
            var position = ButtonGeometry.FromClient(cx, cy, w, h, WindowControl.ScaleOf(hwnd));
            if (position is not null)
            {
                _calibration = null;
                calibration.TrySetResult(position);
                // The calibration click itself toggled Roblox's microphone.
                Post(() => _engine?.CorrectAssumedState());
            }
            return;
        }

        var inForeground = _roblox.Contains(e.ForegroundProcessId);
        Post(() => _engine?.OnKey(e.VirtualKey, e.Down, inForeground));
    }

    private void Post(Action action)
    {
        if (_stopping) return;
        try { _queue.Add(action); }
        catch (InvalidOperationException) { /* shutting down */ }
    }
}
