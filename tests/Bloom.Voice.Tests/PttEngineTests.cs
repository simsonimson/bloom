using Bloom.Core.Voice;

namespace Bloom.Voice.Tests;

internal sealed class FakeStrategy(PttMethod method = PttMethod.SystemWide, bool supportsHold = true) : IMuteStrategy, IAssumedStateStrategy
{
    public List<string> Calls { get; } = new();
    public bool? Muted { get; private set; }
    public Exception? ThrowOnSetMuted { get; set; }
    public Exception? ThrowOnRefresh { get; set; }
    public bool Released { get; private set; }
    public bool ReleasedKeepMuted { get; private set; }

    public PttMethod Method => method;
    public bool SupportsHold => supportsHold;

    public void Engage() => Calls.Add("engage");

    public void SetMuted(bool muted)
    {
        Calls.Add(muted ? "mute" : "unmute");
        if (ThrowOnSetMuted is { } ex) throw ex;
        Muted = muted;
    }

    public void Refresh(bool muted)
    {
        Calls.Add("refresh");
        if (ThrowOnRefresh is { } ex) throw ex;
        Muted = muted;
    }

    public void Release(bool robloxExited, bool keepMuted)
    {
        Released = true;
        ReleasedKeepMuted = keepMuted;
    }

    public void Assume(bool muted) => Muted = muted;
}

public sealed class PttEngineTests
{
    private const int Key = 0x56; // V
    private readonly FakeStrategy _strategy = new();
    private readonly PttEngine _engine;
    private readonly List<PttState> _states = new();

    public PttEngineTests()
    {
        _engine = new PttEngine(_ => _strategy);
        _engine.StateChanged += (_, s) => _states.Add(s);
    }

    private static PttConfig Config(PttMode mode = PttMode.Hold, PttMethod method = PttMethod.SystemWide, bool foregroundOnly = true,
        RobloxExitBehavior exit = RobloxExitBehavior.Restore) =>
        PttConfig.Default with { Enabled = true, Key = Key, Mode = mode, Method = method, ForegroundOnly = foregroundOnly, OnRobloxExit = exit };

    private void Armed(PttConfig? config = null)
    {
        _engine.Configure(config ?? Config());
        _engine.SetRobloxRunning(true);
    }

    [Fact]
    public void Stays_off_until_enabled_and_roblox_runs()
    {
        _engine.SetRobloxRunning(true);
        Assert.Equal(PttState.Off, _engine.State);
        _engine.Configure(Config());
        Assert.Equal(PttState.Ready, _engine.State);
        Assert.Equal(new[] { "engage", "mute" }, _strategy.Calls);
        Assert.True(_strategy.Muted);
    }

    [Fact]
    public void Hold_opens_while_pressed_and_ignores_auto_repeat()
    {
        Armed();
        _engine.OnKey(Key, down: true, robloxInForeground: true);
        _engine.OnKey(Key, down: true, robloxInForeground: true);
        Assert.Equal(PttState.Transmitting, _engine.State);
        Assert.False(_strategy.Muted);
        _engine.OnKey(Key, down: false, robloxInForeground: true);
        Assert.Equal(PttState.Ready, _engine.State);
        Assert.True(_strategy.Muted);
        Assert.Equal(1, _strategy.Calls.Count(c => c == "unmute"));
    }

    [Fact]
    public void Key_up_closes_even_outside_roblox()
    {
        Armed();
        _engine.OnKey(Key, true, true);
        _engine.OnKey(Key, false, robloxInForeground: false);
        Assert.Equal(PttState.Ready, _engine.State);
    }

    [Fact]
    public void Foreground_only_ignores_presses_elsewhere()
    {
        Armed();
        _engine.OnKey(Key, true, robloxInForeground: false);
        Assert.Equal(PttState.Ready, _engine.State);

        _engine.OnKey(Key, false, false);
        _engine.Configure(Config(foregroundOnly: false));
        _engine.OnKey(Key, true, robloxInForeground: false);
        Assert.Equal(PttState.Transmitting, _engine.State);
    }

    [Fact]
    public void Other_keys_do_nothing()
    {
        Armed();
        _engine.OnKey(0x57, true, true);
        Assert.Equal(PttState.Ready, _engine.State);
    }

    [Fact]
    public void Watchdog_closes_on_lost_key_up()
    {
        Armed();
        _engine.OnKey(Key, true, true);
        _engine.Watchdog(_ => true);
        Assert.Equal(PttState.Transmitting, _engine.State);
        _engine.Watchdog(_ => false);
        Assert.Equal(PttState.Ready, _engine.State);
        Assert.True(_strategy.Muted);
    }

    [Fact]
    public void Toggle_switches_on_each_press()
    {
        Armed(Config(PttMode.Toggle));
        _engine.OnKey(Key, true, true);
        _engine.OnKey(Key, false, true);
        Assert.Equal(PttState.Open, _engine.State);
        _engine.Watchdog(_ => false); // watchdog only applies to hold
        Assert.Equal(PttState.Open, _engine.State);
        _engine.OnKey(Key, true, true);
        Assert.Equal(PttState.Ready, _engine.State);
    }

    [Fact]
    public void Roblox_exit_releases_and_honours_keep_muted()
    {
        Armed(Config(exit: RobloxExitBehavior.KeepMuted));
        _engine.OnKey(Key, true, true);
        _engine.SetRobloxRunning(false);
        Assert.Equal(PttState.Off, _engine.State);
        Assert.True(_strategy.Released);
        Assert.True(_strategy.ReleasedKeepMuted);
    }

    [Fact]
    public void Disabling_releases_without_keep_muted()
    {
        Armed(Config(exit: RobloxExitBehavior.KeepMuted));
        _engine.Configure(Config() with { Enabled = false });
        Assert.Equal(PttState.Off, _engine.State);
        Assert.True(_strategy.Released);
        Assert.False(_strategy.ReleasedKeepMuted);
    }

    [Fact]
    public void Device_error_goes_to_error_never_transmitting_and_recovers()
    {
        Armed();
        _strategy.ThrowOnSetMuted = new InvalidOperationException("device gone");
        _engine.OnKey(Key, true, true);
        Assert.Equal(PttState.Error, _engine.State);

        _strategy.ThrowOnSetMuted = null;
        _engine.OnKey(Key, false, true);
        _engine.OnKey(Key, true, true); // ignored in Error
        Assert.Equal(PttState.Error, _engine.State);

        _engine.OnKey(Key, false, true);
        _engine.Poll();
        Assert.Equal(PttState.Ready, _engine.State);
        Assert.True(_strategy.Muted);
    }

    [Fact]
    public void Failed_poll_goes_to_error()
    {
        Armed();
        _strategy.ThrowOnRefresh = new InvalidOperationException();
        _engine.Poll();
        Assert.Equal(PttState.Error, _engine.State);
    }

    [Fact]
    public void Soft_failure_keeps_state_and_reports()
    {
        var reasons = new List<MuteFailure>();
        _engine.ActionFailed += reasons.Add;
        Armed(Config(PttMode.Toggle));
        _strategy.ThrowOnSetMuted = new MuteActionFailedException(MuteFailure.MouseLocked);
        _engine.OnKey(Key, true, true);
        Assert.Equal(PttState.Ready, _engine.State);
        Assert.Equal(new[] { MuteFailure.MouseLocked }, reasons);
    }

    [Fact]
    public void Button_method_forces_toggle_and_supports_correction()
    {
        var button = new FakeStrategy(PttMethod.RobloxButton, supportsHold: false);
        var engine = new PttEngine(_ => button);
        engine.Configure(Config(PttMode.Hold, PttMethod.RobloxButton));
        engine.SetRobloxRunning(true);
        Assert.Equal(PttMode.Toggle, engine.EffectiveMode);

        engine.ToggleFromUi();
        Assert.Equal(PttState.Open, engine.State);
        engine.CorrectAssumedState();
        Assert.Equal(PttState.Ready, engine.State);
        Assert.True(button.Muted);
    }

    [Fact]
    public void Ui_toggle_does_nothing_in_hold_mode()
    {
        Armed();
        _engine.ToggleFromUi();
        Assert.Equal(PttState.Ready, _engine.State);
    }

    [Fact]
    public void Changing_the_key_while_open_closes_first()
    {
        Armed(Config(PttMode.Toggle));
        _engine.OnKey(Key, true, true);
        Assert.Equal(PttState.Open, _engine.State);
        _engine.Configure(Config(PttMode.Toggle) with { Key = 0x42 });
        Assert.Equal(PttState.Ready, _engine.State);
        Assert.True(_strategy.Muted);
    }

    [Fact]
    public void Shutdown_releases()
    {
        Armed();
        _engine.Shutdown();
        Assert.Equal(PttState.Off, _engine.State);
        Assert.True(_strategy.Released);
    }
}
