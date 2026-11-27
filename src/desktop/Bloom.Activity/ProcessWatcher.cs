using System.Diagnostics;
using Bloom.Core.Events;
using Bloom.Core.Roblox;

namespace Bloom.Activity;

/// <summary>
/// Detects RobloxPlayerBeta.exe by polling once per second (Blueprint L5). Polling is cheap,
/// needs no WMI and no admin rights, and keeps working if Windows services misbehave.
/// </summary>
public sealed class ProcessWatcher : IDisposable
{
    public const string ProcessName = "RobloxPlayerBeta";

    private readonly IEventBus _events;
    private readonly Action<string, Exception?>? _log;
    private readonly TimeSpan _interval;
    private readonly Dictionary<int, DateTimeOffset> _known = new();
    private readonly object _gate = new();
    private Timer? _timer;

    public ProcessWatcher(IEventBus events, Action<string, Exception?>? log = null, TimeSpan? interval = null)
    {
        _events = events;
        _log = log;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public bool IsRobloxRunning
    {
        get { lock (_gate) return _known.Count > 0; }
    }

    public IReadOnlyCollection<int> RunningProcessIds
    {
        get { lock (_gate) return _known.Keys.ToArray(); }
    }

    public void Start()
    {
        _timer ??= new Timer(_ => Poll(), null, TimeSpan.Zero, _interval);
    }

    private void Poll()
    {
        if (!Monitor.TryEnter(_gate)) return; // previous poll still running
        try
        {
            var processes = Process.GetProcessesByName(ProcessName);
            var current = new HashSet<int>();
            foreach (var process in processes)
            {
                using (process)
                {
                    current.Add(process.Id);
                    if (_known.ContainsKey(process.Id)) continue;
                    string? path = null;
                    var started = DateTimeOffset.Now;
                    try
                    {
                        // Least-privilege query; never reads Roblox's memory (unlike Process.MainModule).
                        path = Bloom.Platform.Windows.Hardware.ProcessInfo.ImagePath(process.Id);
                        started = process.StartTime;
                    }
                    catch (Exception)
                    {
                        // Access to protected processes can be denied; the event is still useful without details.
                    }
                    _known[process.Id] = started;
                    _events.Publish(new RobloxProcessStarted(process.Id, path, started));
                }
            }

            foreach (var gone in _known.Keys.Where(pid => !current.Contains(pid)).ToList())
            {
                _known.Remove(gone);
                _events.Publish(new RobloxProcessExited(gone, DateTimeOffset.Now));
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke("Process poll failed", ex);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
