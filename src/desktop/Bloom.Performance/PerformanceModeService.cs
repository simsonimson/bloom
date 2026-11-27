using System.Diagnostics;
using Bloom.Core.Events;
using Bloom.Core.Performance;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Platform.Windows.Hardware;

namespace Bloom.Performance;

/// <summary>
/// Performance mode (Phase 5): while Roblox runs, raise its process priority and switch to the
/// best power plan; ask Windows to use the high-performance GPU for Roblox. Everything is
/// restored when Roblox closes, when the mode is switched off, or at the next bloom start.
/// Nothing inside Roblox is touched.
/// </summary>
public sealed class PerformanceModeService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IEventBus _events;
    private readonly Action<string, Exception?> _log;
    private readonly object _gate = new();
    private readonly HashSet<int> _roblox = new();
    private readonly List<IDisposable> _subscriptions = new();
    private bool _active;
    private bool? _override;

    public PerformanceModeService(AppSettings settings, IEventBus events, Action<string, Exception?> log)
    {
        _settings = settings;
        _events = events;
        _log = log;
    }

    public bool Active { get { lock (_gate) return _active; } }
    public IReadOnlyList<string> Skipped { get; private set; } = [];

    /// <summary>A game profile can force the mode on or off; null = the normal setting.</summary>
    public bool? Override
    {
        get { lock (_gate) return _override; }
        set { lock (_gate) _override = value; Evaluate(); }
    }

    public bool Enabled => Override ?? _settings.PerfModeEnabled;

    public void Start(IEnumerable<int> runningRoblox)
    {
        RecoverAfterCrash(runningRoblox.Any());
        lock (_gate) foreach (var pid in runningRoblox) _roblox.Add(pid);
        _subscriptions.Add(_events.Subscribe<RobloxProcessStarted>(e => { lock (_gate) _roblox.Add(e.ProcessId); Evaluate(); }));
        _subscriptions.Add(_events.Subscribe<RobloxProcessExited>(e => { lock (_gate) _roblox.Remove(e.ProcessId); Evaluate(); }));
        _subscriptions.Add(_events.Subscribe<SettingChanged>(e => { if (AppSettings.Keys.IsPerformanceKey(e.Key)) Evaluate(); }));
        Evaluate();
    }

    /// <summary>Before bloom starts Roblox: the GPU preference only takes effect at process start.</summary>
    public void PrepareLaunch(string robloxExecutable)
    {
        if (!Enabled || !_settings.PerfGpuHighPerformance) return;
        try { SetGpuPreference(robloxExecutable); }
        catch (Exception ex) { _log("GPU preference could not be set", ex); }
    }

    public void Evaluate()
    {
        int[] pids;
        bool shouldBeActive;
        lock (_gate)
        {
            pids = _roblox.ToArray();
            shouldBeActive = (_override ?? _settings.PerfModeEnabled) && pids.Length > 0;
        }

        if (shouldBeActive) Apply(pids);
        else if (Active || _settings.PerfRestorePowerScheme is not null) Restore(includeGpu: !Enabled);
        else if (!Enabled && _settings.PerfGpuBackup.Count > 0) RestoreGpu();
    }

    private void Apply(int[] pids)
    {
        var skipped = new List<string>();

        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                var wanted = _settings.PerfPriority switch
                {
                    ProcessPriority.High => ProcessPriorityClass.High,
                    ProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                    _ => ProcessPriorityClass.Normal,
                };
                if (process.PriorityClass != wanted) process.PriorityClass = wanted;
                if (_settings.PerfGpuHighPerformance && ProcessInfo.ImagePath(pid) is { } exe) SetGpuPreference(exe);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                if (!skipped.Contains("priority")) skipped.Add("priority");
                _log($"Priority for Roblox {pid} not set", ex);
            }
        }

        if (_settings.PerfPowerPlan)
        {
            var best = PowerPlans.BestPerformance();
            var current = PowerPlans.Active();
            if (best is null)
            {
                skipped.Add("powerplan");
            }
            else if (current != best.Id)
            {
                if (_settings.PerfRestorePowerScheme is null && current is not null) _settings.PerfRestorePowerScheme = current;
                if (!PowerPlans.SetActive(best.Id)) skipped.Add("powerplan");
            }
        }
        else if (_settings.PerfRestorePowerScheme is { } previous)
        {
            PowerPlans.SetActive(previous);
            _settings.PerfRestorePowerScheme = null;
        }

        var changed = false;
        lock (_gate)
        {
            changed = !_active || !Skipped.SequenceEqual(skipped);
            _active = true;
        }
        Skipped = skipped;
        if (changed) _events.Publish(new PerformanceModeChanged(true, skipped));
    }

    private void Restore(bool includeGpu)
    {
        if (_settings.PerfRestorePowerScheme is { } previous)
        {
            // Only switch back if nobody changed the plan in the meantime.
            var best = PowerPlans.BestPerformance();
            if (best is null || PowerPlans.Active() == best.Id) PowerPlans.SetActive(previous);
            _settings.PerfRestorePowerScheme = null;
        }
        if (includeGpu) RestoreGpu();

        bool wasActive;
        lock (_gate)
        {
            wasActive = _active;
            _active = false;
        }
        Skipped = [];
        if (wasActive) _events.Publish(new PerformanceModeChanged(false, []));
    }

    private void SetGpuPreference(string exe)
    {
        if (GpuPreference.Read(exe) == GpuPreference.HighPerformance) return;
        var backup = _settings.PerfGpuBackup;
        if (!backup.ContainsKey(exe))
        {
            backup[exe] = GpuPreference.Read(exe);
            _settings.PerfGpuBackup = backup;
        }
        GpuPreference.Write(exe, GpuPreference.HighPerformance);
    }

    private void RestoreGpu()
    {
        var backup = _settings.PerfGpuBackup;
        if (backup.Count == 0) return;
        foreach (var (exe, previous) in backup)
        {
            try
            {
                if (GpuPreference.Read(exe) != GpuPreference.HighPerformance) continue; // changed by the user
                if (previous is null) GpuPreference.Delete(exe);
                else GpuPreference.Write(exe, previous);
            }
            catch (Exception ex)
            {
                _log("GPU preference could not be restored", ex);
            }
        }
        _settings.PerfGpuBackup = new Dictionary<string, string?>();
    }

    private void RecoverAfterCrash(bool robloxRunning)
    {
        if (robloxRunning || _settings.PerfRestorePowerScheme is not { } previous) return;
        PowerPlans.SetActive(previous);
        _settings.PerfRestorePowerScheme = null;
    }

    /// <summary>Clean exit and uninstall: give back the power plan; GPU preferences stay only while the mode is on.</summary>
    public void Shutdown()
    {
        lock (_gate) _roblox.Clear();
        Restore(includeGpu: !Enabled);
    }

    public void Dispose()
    {
        foreach (var s in _subscriptions) s.Dispose();
    }
}
