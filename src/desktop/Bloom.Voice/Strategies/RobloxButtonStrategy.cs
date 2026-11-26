using Bloom.Core.Voice;
using Bloom.Platform.Windows.Input;

namespace Bloom.Voice.Strategies;

/// <summary>
/// D: clicks Roblox's own microphone button at the calibrated position. bloom cannot read the
/// real state, so it tracks an assumption that the user can correct. Toggle only.
/// </summary>
public sealed class RobloxButtonStrategy(RobloxProcesses roblox, Func<PttConfig> config) : IMuteStrategy, IAssumedStateStrategy
{
    private bool _assumedMuted = true;

    public PttMethod Method => PttMethod.RobloxButton;
    public bool SupportsHold => false;

    // Roblox joins voice muted; the user can correct the assumption in the app.
    public void Engage() => _assumedMuted = true;

    public void SetMuted(bool muted)
    {
        if (muted == _assumedMuted) return;
        Click();
        _assumedMuted = muted;
    }

    public void Refresh(bool muted) { }

    public void Release(bool robloxExited, bool keepMuted) { }

    public void Assume(bool muted) => _assumedMuted = muted;

    private void Click()
    {
        var position = config().Button ?? throw new MuteActionFailedException(MuteFailure.NotCalibrated);
        var hwnd = roblox.Snapshot().Select(WindowControl.FindMainWindow).FirstOrDefault(h => h != IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new MuteActionFailedException(MuteFailure.RobloxWindowNotFound);

        var switched = WindowControl.ForegroundWindow() != hwnd;
        if (!WindowControl.BringToForeground(hwnd, TimeSpan.FromMilliseconds(600)))
            throw new MuteActionFailedException(MuteFailure.CouldNotFocus);
        if (switched) Thread.Sleep(120); // let Roblox restore its cursor state after activation
        if (!WindowControl.IsCursorVisible()) throw new MuteActionFailedException(MuteFailure.MouseLocked);

        var (width, height) = WindowControl.ClientSize(hwnd);
        var client = ButtonGeometry.ToClient(position, width, height, WindowControl.ScaleOf(hwnd))
            ?? throw new MuteActionFailedException(MuteFailure.NotCalibrated);
        var (x, y) = WindowControl.ClientToScreen(hwnd, client.X, client.Y);
        WindowControl.ClickAt(x, y);
    }
}
