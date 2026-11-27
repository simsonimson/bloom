using System.Text;
using Bloom.Core.Events;
using Bloom.Core.Roblox;

namespace Bloom.Activity;

/// <summary>
/// EXPERIMENTAL (Blueprint E1): tails the newest Roblox player log while Roblox runs and turns
/// join/leave lines into <see cref="ExperienceJoined"/> / <see cref="ExperienceLeft"/> events.
/// Opens logs read-only with FileShare.ReadWrite so Roblox is never blocked. Can be switched off.
/// </summary>
public sealed class LogWatcher : IDisposable
{
    private readonly IEventBus _events;
    private readonly string _logDirectory;
    private readonly Action<string, Exception?>? _log;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _cts;
    private bool _enabled;

    public LogWatcher(IEventBus events, string logDirectory, Action<string, Exception?>? log = null)
    {
        _events = events;
        _logDirectory = logDirectory;
        _log = log;
        _subscriptions.Add(events.Subscribe<RobloxProcessStarted>(OnStarted));
        _subscriptions.Add(events.Subscribe<RobloxProcessExited>(_ => Stop()));
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Stop();
        }
    }

    private void OnStarted(RobloxProcessStarted started)
    {
        if (!_enabled) return;
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => TailAsync(started.StartedAt, token), token);
    }

    private void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task TailAsync(DateTimeOffset processStart, CancellationToken ct)
    {
        try
        {
            var file = await WaitForLogFileAsync(processStart, ct);
            if (file is null)
            {
                _log?.Invoke("No Roblox log file found for this session", null);
                return;
            }

            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            JoiningGame? pending = null;
            long? universe = null;
            var inGame = false;

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                switch (RobloxLogParser.Parse(line))
                {
                    case JoiningGame joining:
                        pending = joining;
                        universe = null;
                        break;
                    case UdmuxAddress relay when pending is not null:
                        pending = pending with { ServerAddress = relay.Address };
                        break;
                    case JoiningUniverse u:
                        universe = u.UniverseId;
                        break;
                    case JoinedServer when pending is not null:
                        inGame = true;
                        _events.Publish(new ExperienceJoined(pending.PlaceId, pending.JobId, universe, pending.ServerAddress, DateTimeOffset.Now));
                        pending = null;
                        break;
                    case Disconnected or LeavingGame when inGame:
                        inGame = false;
                        _events.Publish(new ExperienceLeft(DateTimeOffset.Now));
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Invoke("Log watcher stopped", ex);
        }
    }

    private async Task<string?> WaitForLogFileAsync(DateTimeOffset processStart, CancellationToken ct)
    {
        var threshold = processStart.UtcDateTime.AddSeconds(-5);
        for (var i = 0; i < 30 && !ct.IsCancellationRequested; i++)
        {
            if (Directory.Exists(_logDirectory))
            {
                var candidate = new DirectoryInfo(_logDirectory)
                    .EnumerateFiles("*Player*.log")
                    .Where(f => f.CreationTimeUtc >= threshold)
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .FirstOrDefault();
                if (candidate is not null) return candidate.FullName;
            }
            await Task.Delay(1000, ct);
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
        foreach (var s in _subscriptions) s.Dispose();
    }
}
