using Bloom.Core.Events;
using Bloom.Core.Roblox;

namespace Bloom.Activity;

/// <summary>
/// Combines process and (optional) log events into one <see cref="ActivityState"/>,
/// keeps the play history and publishes <see cref="ActivityStateChanged"/>.
/// </summary>
public sealed class ActivityTracker : IDisposable
{
    private readonly IEventBus _events;
    private readonly ISessionStore _sessions;
    private readonly ExperienceResolver _resolver;
    private readonly Action<string, Exception?>? _log;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _gate = new();
    private readonly HashSet<int> _pids = new();
    private long? _sessionId;
    private ActivityState _state = ActivityState.Idle;

    public ActivityTracker(IEventBus events, ISessionStore sessions, ExperienceResolver resolver, Action<string, Exception?>? log = null)
    {
        _events = events;
        _sessions = sessions;
        _resolver = resolver;
        _log = log;
        _subscriptions.Add(events.Subscribe<RobloxProcessStarted>(OnStarted));
        _subscriptions.Add(events.Subscribe<RobloxProcessExited>(OnExited));
        _subscriptions.Add(events.Subscribe<ExperienceJoined>(OnJoined));
        _subscriptions.Add(events.Subscribe<ExperienceLeft>(OnLeft));
    }

    public ActivityState State
    {
        get { lock (_gate) return _state; }
    }

    private void OnStarted(RobloxProcessStarted e)
    {
        lock (_gate)
        {
            _pids.Add(e.ProcessId);
            if (_state.RobloxRunning) return;
            _sessionId = SafeStore(() => _sessions.Start(e.StartedAt));
            Set(new ActivityState(true, e.StartedAt, null, null, null));
        }
    }

    private void OnExited(RobloxProcessExited e)
    {
        lock (_gate)
        {
            _pids.Remove(e.ProcessId);
            if (_pids.Count > 0) return;
            if (_sessionId is long id) SafeStore(() => { _sessions.End(id, e.ExitedAt); return 0L; });
            _sessionId = null;
            Set(ActivityState.Idle);
        }
    }

    private void OnJoined(ExperienceJoined e)
    {
        lock (_gate)
        {
            if (!_state.RobloxRunning) return;
            Set(_state with { PlaceId = e.PlaceId, JobId = e.JobId, Experience = null, ServerAddress = e.ServerAddress, UniverseId = e.UniverseId });
        }
        _ = ResolveAsync(e);
    }

    private void OnLeft(ExperienceLeft e)
    {
        lock (_gate)
        {
            if (!_state.RobloxRunning) return;
            Set(_state with { PlaceId = null, JobId = null, Experience = null, ServerAddress = null, UniverseId = null });
        }
    }

    private async Task ResolveAsync(ExperienceJoined e)
    {
        var info = await _resolver.ResolveAsync(e.PlaceId, e.UniverseId);
        lock (_gate)
        {
            if (_state.PlaceId != e.PlaceId) return; // already somewhere else
            if (_sessionId is long id)
                SafeStore(() => { _sessions.SetExperience(id, e.PlaceId, info?.UniverseId ?? e.UniverseId, e.JobId, info?.Name); return 0L; });
            if (info is not null) Set(_state with { Experience = info });
        }
    }

    private void Set(ActivityState state)
    {
        _state = state;
        _events.Publish(new ActivityStateChanged(state));
    }

    private long? SafeStore(Func<long> action)
    {
        try { return action(); }
        catch (Exception ex)
        {
            _log?.Invoke("Play history could not be written", ex);
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var s in _subscriptions) s.Dispose();
    }
}
