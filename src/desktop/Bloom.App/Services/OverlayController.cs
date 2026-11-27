using System.Globalization;
using Bloom.Core.Events;
using Bloom.Core.Performance;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Performance;
using Bloom.Platform.Windows.Hardware;
using Bloom.Platform.Windows.Input;

namespace Bloom.App.Services;

/// <summary>Shows the performance overlay above the Roblox window while Roblox is in the foreground.</summary>
public sealed class OverlayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly PerformanceMonitor _monitor;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly HashSet<int> _roblox = new();
    private readonly object _gate = new();
    private OverlayWindow? _window;
    private IDisposable? _watch;
    private Timer? _timer;

    public OverlayController(AppSettings settings, PerformanceMonitor monitor, IEventBus events)
    {
        _settings = settings;
        _monitor = monitor;
        _subscriptions.Add(events.Subscribe<SettingChanged>(e =>
        {
            if (e.Key is AppSettings.Keys.PerfOverlay or AppSettings.Keys.PerfOverlayCorner) Evaluate();
        }));
        _subscriptions.Add(events.Subscribe<RobloxProcessStarted>(e => { lock (_gate) _roblox.Add(e.ProcessId); Evaluate(); }));
        _subscriptions.Add(events.Subscribe<RobloxProcessExited>(e => { lock (_gate) _roblox.Remove(e.ProcessId); Evaluate(); }));
    }

    public void Start(IEnumerable<int> running)
    {
        lock (_gate) foreach (var pid in running) _roblox.Add(pid);
        Evaluate();
    }

    private void Evaluate()
    {
        bool want;
        lock (_gate) want = _settings.PerfOverlay && _roblox.Count > 0;
        if (want && _timer is null)
        {
            _window ??= new OverlayWindow();
            _window.Start();
            _watch = _monitor.Watch();
            _timer = new Timer(_ => Tick(), null, 0, 500);
        }
        else if (!want && _timer is not null)
        {
            _timer.Dispose();
            _timer = null;
            _watch?.Dispose();
            _watch = null;
            _window?.Hide();
        }
    }

    private void Tick()
    {
        try
        {
            int[] pids;
            lock (_gate) pids = _roblox.ToArray();
            var foreground = WindowControl.ForegroundProcessId();
            if (!pids.Contains(foreground) || _monitor.Latest is not { } sample)
            {
                _window?.Hide();
                return;
            }
            var hwnd = WindowControl.ForegroundWindow();
            var (width, height) = WindowControl.ClientSize(hwnd);
            var (left, top) = WindowControl.ClientToScreen(hwnd, 0, 0);
            _window?.Show(Format(sample), left, top, left + width, top + height, (int)_settings.PerfOverlayCorner, WindowControl.ScaleOf(hwnd));
        }
        catch
        {
            // Window vanished between the calls.
        }
    }

    internal static string Format(PerformanceSample s)
    {
        var c = CultureInfo.CurrentCulture;
        var gpu = s.GpuPercent is { } g ? $"GPU {g.ToString("0", c)} %" : "GPU –";
        var ping = s.PingMs is { } p ? $"Ping {p} ms" : "Ping –";
        return $"CPU {s.CpuPercent.ToString("0", c)} %   {gpu}\nRAM {(s.WorkingSetBytes / 1073741824.0).ToString("0.0", c)} GB   {ping}";
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _watch?.Dispose();
        _window?.Dispose();
        foreach (var s in _subscriptions) s.Dispose();
    }
}
