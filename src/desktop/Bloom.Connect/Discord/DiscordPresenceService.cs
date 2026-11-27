using System.Text.Json.Nodes;
using Bloom.Core.Connect;
using Bloom.Core.Events;
using Bloom.Core.Roblox;

namespace Bloom.Connect.Discord;

public sealed record DiscordPresenceConfig(bool Enabled, string? ApplicationId, PresenceOptions Options);

/// <summary>
/// Keeps the Discord connection alive (reconnects every 15 s) and mirrors the Roblox activity.
/// Discord allows about 5 activity updates per 20 s, so updates are spaced at least 4 s apart.
/// </summary>
public sealed class DiscordPresenceService : IAsyncDisposable
{
    private static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinUpdateGap = TimeSpan.FromSeconds(4);

    private readonly IEventBus _events;
    private readonly Func<DiscordPresenceConfig> _config;
    private readonly Func<PresenceTexts> _texts;
    private readonly Action<string, Exception?> _log;
    private readonly SemaphoreSlim _changed = new(0, int.MaxValue);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<IDisposable> _subscriptions = new();
    private ActivityState _activity = ActivityState.Idle;
    private Task? _loop;

    public DiscordPresenceService(IEventBus events, Func<DiscordPresenceConfig> config, Func<PresenceTexts> texts, Action<string, Exception?> log)
    {
        _events = events;
        _config = config;
        _texts = texts;
        _log = log;
    }

    public DiscordStatus Status { get; private set; } = DiscordStatus.NotRunning;
    public DiscordUser? User { get; private set; }

    public void Start(ActivityState initial)
    {
        if (_loop is not null) return;
        _activity = initial;
        _subscriptions.Add(_events.Subscribe<ActivityStateChanged>(e =>
        {
            _activity = e.State;
            Signal();
        }));
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>Settings changed: re-evaluate (connect, disconnect or update the activity).</summary>
    public void Signal() => _changed.Release();

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var config = _config();
            if (!config.Enabled)
            {
                SetStatus(DiscordStatus.Disabled, null);
                await WaitForChangeAsync(Timeout.InfiniteTimeSpan, ct);
                continue;
            }
            if (string.IsNullOrWhiteSpace(config.ApplicationId))
            {
                SetStatus(DiscordStatus.NotConfigured, null);
                await WaitForChangeAsync(Timeout.InfiniteTimeSpan, ct);
                continue;
            }

            try
            {
                await using var connection = await DiscordIpcConnection.ConnectAsync(ct);
                if (connection is null)
                {
                    SetStatus(DiscordStatus.NotRunning, null);
                    await WaitForChangeAsync(Reconnect, ct);
                    continue;
                }
                var user = await connection.HandshakeAsync(config.ApplicationId, ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
                SetStatus(DiscordStatus.Connected, user);
                await RunConnectedAsync(connection, config.ApplicationId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log("Discord connection lost", ex);
            }
            SetStatus(DiscordStatus.NotRunning, null);
            await WaitForChangeAsync(Reconnect, ct);
        }
    }

    private async Task RunConnectedAsync(DiscordIpcConnection connection, string applicationId, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = ReadLoopAsync(connection, linked.Token);
        string? lastSent = null;
        var lastUpdate = DateTimeOffset.MinValue;
        try
        {
            while (!linked.IsCancellationRequested && !reader.IsCompleted)
            {
                var config = _config();
                if (!config.Enabled || config.ApplicationId != applicationId)
                {
                    await connection.SetActivityAsync(null, Environment.ProcessId, ct);
                    return; // outer loop handles the new configuration
                }

                var activity = PresenceBuilder.Build(_activity, config.Options, _texts());
                var json = activity?.ToJsonString() ?? "null";
                if (json != lastSent)
                {
                    var wait = lastUpdate + MinUpdateGap - DateTimeOffset.UtcNow;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                    // The state may have changed while waiting; build again.
                    activity = PresenceBuilder.Build(_activity, _config().Options, _texts());
                    json = activity?.ToJsonString() ?? "null";
                    await connection.SetActivityAsync(activity is null ? null : (JsonObject)activity.DeepClone(), Environment.ProcessId, ct);
                    lastSent = json;
                    lastUpdate = DateTimeOffset.UtcNow;
                }
                await Task.WhenAny(WaitForChangeAsync(TimeSpan.FromSeconds(30), linked.Token), reader);
            }
        }
        finally
        {
            linked.Cancel();
            try { await reader; } catch { /* reported below */ }
        }
        if (reader.IsFaulted) throw reader.Exception!.GetBaseException();
    }

    private static async Task ReadLoopAsync(DiscordIpcConnection connection, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await connection.ReadAsync(ct);
            switch (frame.Opcode)
            {
                case DiscordOpcode.Ping:
                    await connection.WriteAsync(DiscordOpcode.Pong, frame.Payload, ct);
                    break;
                case DiscordOpcode.Close:
                    throw new DiscordIpcException(frame.Payload["message"]?.GetValue<string>() ?? "closed");
                default:
                    // SET_ACTIVITY replies and errors; errors are visible in Discord's own logs.
                    break;
            }
        }
    }

    private async Task WaitForChangeAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            if (await _changed.WaitAsync(timeout, ct))
            {
                while (_changed.CurrentCount > 0) _changed.Wait(0); // coalesce bursts
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetStatus(DiscordStatus status, DiscordUser? user)
    {
        if (status == Status && user == User) return;
        Status = status;
        User = user;
        _events.Publish(new DiscordStatusChanged(status, user));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _subscriptions) s.Dispose();
        _stop.Cancel();
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        _stop.Dispose();
    }
}
