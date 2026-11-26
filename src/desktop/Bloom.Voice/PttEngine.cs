using Bloom.Core.Voice;

namespace Bloom.Voice;

/// <summary>
/// Push-to-talk state machine (Blueprint 6.5). Not thread-safe: all calls come from the voice thread.
/// Safety rules: Ready means muted; every unclear state goes to Ready or Error, never to Transmitting.
/// </summary>
public sealed class PttEngine
{
    private readonly Func<PttMethod, IMuteStrategy> _factory;
    private IMuteStrategy? _strategy;
    private PttConfig _config = PttConfig.Default;
    private bool _robloxRunning;
    private bool _keyHeld;

    public PttEngine(Func<PttMethod, IMuteStrategy> factory) => _factory = factory;

    public event Action<PttState, PttState>? StateChanged;
    public event Action<MuteFailure>? ActionFailed;
    public event Action<Exception>? StrategyError;

    public PttState State { get; private set; } = PttState.Off;
    public PttConfig Config => _config;
    public IMuteStrategy? Strategy => _strategy;
    public bool RobloxRunning => _robloxRunning;

    /// <summary>Hold is only possible when the method can both open and close reliably.</summary>
    public PttMode EffectiveMode => _config.Method == PttMethod.RobloxButton || _strategy is { SupportsHold: false }
        ? PttMode.Toggle
        : _config.Mode;

    public void Configure(PttConfig config)
    {
        var previous = _config;
        _config = config;

        if (State != PttState.Off && (!config.Enabled || config.Method != previous.Method))
        {
            Disarm(robloxExited: false);
        }
        else if (State is PttState.Transmitting or PttState.Open && (config.Key != previous.Key || config.Mode != previous.Mode))
        {
            _keyHeld = false;
            Transition(muted: true, PttState.Ready);
        }
        Evaluate();
    }

    public void SetRobloxRunning(bool running)
    {
        if (running == _robloxRunning) return;
        _robloxRunning = running;
        if (!running && State != PttState.Off) Disarm(robloxExited: true);
        Evaluate();
    }

    public void OnKey(int virtualKey, bool down, bool robloxInForeground)
    {
        if (virtualKey == 0 || virtualKey != _config.Key) return;

        if (!down)
        {
            _keyHeld = false;
            // Key-up always closes, wherever the focus is (fail-closed).
            if (State == PttState.Transmitting) Transition(muted: true, PttState.Ready);
            return;
        }

        if (_keyHeld) return; // keyboard auto-repeat
        _keyHeld = true;
        if (_config.ForegroundOnly && !robloxInForeground) return;
        Press();
    }

    /// <summary>The "toggle" button in the app or tray. Only in toggle mode.</summary>
    public void ToggleFromUi()
    {
        if (EffectiveMode != PttMode.Toggle) return;
        Press();
    }

    /// <summary>For strategies that only assume the state: flips the assumption without acting.</summary>
    public void CorrectAssumedState()
    {
        if (_strategy is not IAssumedStateStrategy assumed || State is not (PttState.Ready or PttState.Open)) return;
        var nowMuted = State == PttState.Open;
        assumed.Assume(nowMuted);
        SetState(nowMuted ? PttState.Ready : PttState.Open);
    }

    /// <summary>50-ms watchdog (ADR-004): a lost key-up is corrected by polling the physical key state.</summary>
    public void Watchdog(Func<int, bool> isPhysicallyDown)
    {
        if (State != PttState.Transmitting || isPhysicallyDown(_config.Key)) return;
        _keyHeld = false;
        Transition(muted: true, PttState.Ready);
    }

    /// <summary>Periodic re-application (new devices/sessions) and error recovery.</summary>
    public void Poll()
    {
        if (_strategy is null) return;
        var muted = State is not (PttState.Transmitting or PttState.Open);
        try
        {
            _strategy.Refresh(muted);
            if (State == PttState.Error) SetState(PttState.Ready);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    public void Shutdown()
    {
        if (State != PttState.Off) Disarm(robloxExited: false);
    }

    private void Evaluate()
    {
        if (State == PttState.Off && _config.Enabled && _robloxRunning) Arm();
    }

    private void Arm()
    {
        _keyHeld = false;
        _strategy = _factory(_config.Method);
        try
        {
            _strategy.Engage();
            _strategy.SetMuted(true);
            SetState(PttState.Ready);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Disarm(bool robloxExited)
    {
        var strategy = _strategy;
        _strategy = null;
        _keyHeld = false;
        if (strategy is not null)
        {
            try
            {
                strategy.Release(robloxExited, keepMuted: robloxExited && _config.OnRobloxExit == RobloxExitBehavior.KeepMuted);
            }
            catch (Exception ex)
            {
                StrategyError?.Invoke(ex);
            }
        }
        SetState(PttState.Off);
    }

    private void Press()
    {
        if (EffectiveMode == PttMode.Hold)
        {
            if (State == PttState.Ready) Transition(muted: false, PttState.Transmitting);
        }
        else if (State == PttState.Ready)
        {
            Transition(muted: false, PttState.Open);
        }
        else if (State == PttState.Open)
        {
            Transition(muted: true, PttState.Ready);
        }
    }

    private void Transition(bool muted, PttState target)
    {
        if (_strategy is null) return;
        try
        {
            _strategy.SetMuted(muted);
            SetState(target);
        }
        catch (MuteActionFailedException failure)
        {
            ActionFailed?.Invoke(failure.Reason);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex)
    {
        StrategyError?.Invoke(ex);
        try { _strategy?.SetMuted(true); } catch { /* best effort: the device may be gone */ }
        SetState(PttState.Error);
    }

    private void SetState(PttState state)
    {
        if (state == State) return;
        var old = State;
        State = state;
        StateChanged?.Invoke(old, state);
    }
}
