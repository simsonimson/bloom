using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Bloom.Core.Events;
using Bloom.Core.Performance;
using Bloom.Core.Roblox;
using Bloom.Platform.Windows.Hardware;

namespace Bloom.Performance;

/// <summary>
/// Samples the Roblox client once per second while someone is watching (performance page or
/// overlay): CPU, RAM, GPU from Windows counters, ping to the game server via ICMP.
/// FPS is not measured: that needs ETW with administrator rights; Roblox shows it with Shift+F5.
/// </summary>
public sealed class PerformanceMonitor : IDisposable
{
    private readonly IEventBus _events;
    private readonly Action<string, Exception?> _log;
    private readonly object _gate = new();
    private readonly HashSet<int> _roblox = new();
    private readonly List<IDisposable> _subscriptions = new();
    private int _watchers;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile string? _serverAddress;
    private (int Pid, TimeSpan Cpu, long Ticks)? _lastCpu;

    public PerformanceMonitor(IEventBus events, Action<string, Exception?> log)
    {
        _events = events;
        _log = log;
    }

    public PerformanceSample? Latest { get; private set; }

    public void Start(IEnumerable<int> runningRoblox, ActivityState activity)
    {
        lock (_gate) foreach (var pid in runningRoblox) _roblox.Add(pid);
        _serverAddress = activity.ServerAddress;
        _subscriptions.Add(_events.Subscribe<RobloxProcessStarted>(e => { lock (_gate) _roblox.Add(e.ProcessId); }));
        _subscriptions.Add(_events.Subscribe<RobloxProcessExited>(e => { lock (_gate) _roblox.Remove(e.ProcessId); }));
        _subscriptions.Add(_events.Subscribe<ActivityStateChanged>(e => _serverAddress = e.State.ServerAddress));
        _thread = new Thread(Run) { IsBackground = true, Name = "PerfMonitor", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Sampling only runs while at least one watcher is registered.</summary>
    public IDisposable Watch()
    {
        Interlocked.Increment(ref _watchers);
        return new Watcher(this);
    }

    private void Run()
    {
        GpuCounters? gpu = null;
        using var ping = new Ping();
        long nextPing = 0;
        int? lastPing = null;
        try
        {
            while (!_stop)
            {
                Thread.Sleep(1000);
                if (Volatile.Read(ref _watchers) == 0) continue;

                int pid;
                lock (_gate) pid = _roblox.Count == 0 ? 0 : _roblox.Max();
                if (pid == 0)
                {
                    if (Latest is not null) Publish(null);
                    _lastCpu = null;
                    continue;
                }

                try
                {
                    gpu ??= new GpuCounters();
                    using var process = Process.GetProcessById(pid);
                    var cpu = CpuPercent(pid, process.TotalProcessorTime);
                    var (gpuPercent, gpuMemory) = gpu.Sample(pid);

                    var now = Environment.TickCount64;
                    if (now >= nextPing)
                    {
                        nextPing = now + 2000;
                        lastPing = MeasurePing(ping, _serverAddress);
                    }
                    Publish(new PerformanceSample(DateTimeOffset.Now, cpu, process.WorkingSet64, gpuPercent, gpuMemory, lastPing));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Roblox just exited.
                }
            }
        }
        catch (Exception ex)
        {
            _log("Performance monitor stopped", ex);
        }
        finally
        {
            gpu?.Dispose();
        }
    }

    private double CpuPercent(int pid, TimeSpan total)
    {
        var ticks = Stopwatch.GetTimestamp();
        var previous = _lastCpu;
        _lastCpu = (pid, total, ticks);
        if (previous is not { } p || p.Pid != pid) return 0;
        var wall = Stopwatch.GetElapsedTime(p.Ticks, ticks).TotalMilliseconds;
        return wall <= 0 ? 0 : Math.Clamp((total - p.Cpu).TotalMilliseconds / wall / Environment.ProcessorCount * 100, 0, 100);
    }

    internal static int? MeasurePing(Ping ping, string? address)
    {
        if (address is null || !IPAddress.TryParse(address, out var ip) || !NetworkInfo.IsPublic(ip)) return null;
        try
        {
            var reply = ping.Send(ip, 1000);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }

    private void Publish(PerformanceSample? sample)
    {
        Latest = sample;
        _events.Publish(new PerformanceSampled(sample));
    }

    public void Dispose()
    {
        _stop = true;
        foreach (var s in _subscriptions) s.Dispose();
    }

    private sealed class Watcher(PerformanceMonitor owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref owner._watchers);
        }
    }
}

public static class NetworkInfo
{
    /// <summary>False for private, loopback, link-local and CGNAT addresses (no ping or lookup for those).</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return !(b[0] == 10
                 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                 || b[0] == 0 || b[0] >= 224);
    }
}
