using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Audio;

public sealed class AudioException(string what, int hr) : Exception($"{what} failed (0x{hr:X8})")
{
    public int Code { get; } = hr;
    public bool AccessDenied => Code == Hr.AccessDenied;
    public bool DeviceGone => Code is Hr.DeviceInvalidated or Hr.NotFound;
}

public sealed record CaptureDeviceInfo(string Id, string Name, bool IsDefault, bool IsDefaultCommunications);

/// <summary>One capture session (one app's microphone stream group) on one endpoint.</summary>
public sealed class CaptureSessionHandle
{
    private readonly ISimpleAudioVolume _volume;
    private readonly IAudioMeterInformation _meter;

    internal CaptureSessionHandle(string deviceId, int processId, ISimpleAudioVolume volume, IAudioMeterInformation meter)
    {
        DeviceId = deviceId;
        ProcessId = processId;
        _volume = volume;
        _meter = meter;
    }

    public string DeviceId { get; }
    public int ProcessId { get; }

    public void SetMute(bool mute)
    {
        var ctx = Guid.Empty;
        Hr.Check(_volume.SetMute(mute, ref ctx), "ISimpleAudioVolume.SetMute");
    }

    public bool GetMute()
    {
        Hr.Check(_volume.GetMute(out var mute), "ISimpleAudioVolume.GetMute");
        return mute;
    }

    public float Peak() => _meter.GetPeakValue(out var p) >= 0 ? p : 0f;
}

/// <summary>
/// Capture endpoints, endpoint mute, meters and capture sessions. Create and use on one MTA thread.
/// </summary>
public sealed class AudioSystem : IDisposable
{
    private const uint ClsCtxAll = 0x17;
    private const uint DeviceStateActive = 0x1;
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    private readonly Dictionary<string, Endpoint> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly SessionNotification _notification;
    private bool _watching;

    public AudioSystem()
    {
        _notification = new SessionNotification(this);
    }

    /// <summary>Raised on a COM worker thread when any watched capture endpoint gets a new session.</summary>
    public event Action? SessionCreated;

    /// <summary>Enumerates active microphones and refreshes the internal cache.</summary>
    public IReadOnlyList<CaptureDeviceInfo> GetCaptureDevices()
    {
        Hr.Check(_enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceStateActive, out var collection), "EnumAudioEndpoints");
        collection.GetCount(out var count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; i < count; i++)
        {
            if (collection.Item(i, out var device) < 0 || device.GetId(out var id) < 0) continue;
            seen.Add(id);
            if (_endpoints.ContainsKey(id)) continue;
            try
            {
                _endpoints[id] = Endpoint.Create(id, device);
                if (_watching) _endpoints[id].Watch(_notification);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or AudioException)
            {
                // Device vanished between enumeration and activation.
            }
        }

        foreach (var gone in _endpoints.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _endpoints[gone].Unwatch(_notification);
            _endpoints.Remove(gone);
        }

        var defaultId = GetDefaultCaptureId(false);
        var communicationsId = GetDefaultCaptureId(true);
        return _endpoints.Values
            .Select(e => new CaptureDeviceInfo(e.Id, e.Name,
                string.Equals(e.Id, defaultId, StringComparison.OrdinalIgnoreCase),
                string.Equals(e.Id, communicationsId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string? GetDefaultCaptureId(bool communications)
    {
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, communications ? ERole.Communications : ERole.Console, out var device) < 0 || device is null)
            return null;
        return device.GetId(out var id) >= 0 ? id : null;
    }

    public bool DeviceExists(string id) => _endpoints.ContainsKey(id);

    public bool GetEndpointMute(string id)
    {
        Hr.Check(Get(id).Volume.GetMute(out var mute), "IAudioEndpointVolume.GetMute");
        return mute;
    }

    public void SetEndpointMute(string id, bool mute)
    {
        var ctx = Guid.Empty;
        Hr.Check(Get(id).Volume.SetMute(mute, ref ctx), "IAudioEndpointVolume.SetMute");
    }

    /// <summary>Peak level 0..1 of the endpoint, or 0 if unavailable.</summary>
    public float GetEndpointPeak(string id) =>
        _endpoints.TryGetValue(id, out var e) && e.Meter.GetPeakValue(out var p) >= 0 ? p : 0f;

    /// <summary>All non-expired capture sessions whose process matches the filter, on every active endpoint.</summary>
    public IReadOnlyList<CaptureSessionHandle> GetCaptureSessions(Func<int, bool> processFilter)
    {
        var result = new List<CaptureSessionHandle>();
        foreach (var endpoint in _endpoints.Values)
        {
            if (endpoint.Sessions is null || endpoint.Sessions.GetSessionEnumerator(out var sessions) < 0) continue;
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                try
                {
                    if (sessions.GetSession(i, out var raw) < 0) continue;
                    var control = (IAudioSessionControl2)raw;
                    if (control.GetProcessId(out var pid) < 0 || control.GetState(out var state) < 0) continue;
                    if (state == 2 /* expired */ || !processFilter((int)pid)) continue;
                    result.Add(new CaptureSessionHandle(endpoint.Id, (int)pid, (ISimpleAudioVolume)raw, (IAudioMeterInformation)raw));
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException)
                {
                    // Session expired while enumerating.
                }
            }
        }
        return result;
    }

    /// <summary>Starts raising <see cref="SessionCreated"/> for new sessions (call on an MTA thread).</summary>
    public void WatchSessions()
    {
        if (_watching) return;
        _watching = true;
        foreach (var endpoint in _endpoints.Values) endpoint.Watch(_notification);
    }

    private Endpoint Get(string id) =>
        _endpoints.TryGetValue(id, out var e) ? e : throw new AudioException($"Device {id}", Hr.NotFound);

    public void Dispose()
    {
        foreach (var endpoint in _endpoints.Values) endpoint.Unwatch(_notification);
        _endpoints.Clear();
    }

    private sealed class Endpoint
    {
        private bool _watched;

        public required string Id { get; init; }
        public required string Name { get; init; }
        public required IAudioEndpointVolume Volume { get; init; }
        public required IAudioMeterInformation Meter { get; init; }
        public IAudioSessionManager2? Sessions { get; init; }

        public static Endpoint Create(string id, IMMDevice device) => new()
        {
            Id = id,
            Name = ReadFriendlyName(device) ?? id,
            Volume = (IAudioEndpointVolume)Activate(device, typeof(IAudioEndpointVolume).GUID),
            Meter = (IAudioMeterInformation)Activate(device, typeof(IAudioMeterInformation).GUID),
            Sessions = TryActivate(device, typeof(IAudioSessionManager2).GUID) as IAudioSessionManager2,
        };

        public void Watch(SessionNotification notification)
        {
            if (_watched || Sessions is null) return;
            // The session enumerator must be created once before notifications are delivered.
            Sessions.GetSessionEnumerator(out _);
            _watched = Sessions.RegisterSessionNotification(notification) >= 0;
        }

        public void Unwatch(SessionNotification notification)
        {
            if (!_watched || Sessions is null) return;
            try { Sessions.UnregisterSessionNotification(notification); } catch (COMException) { }
            _watched = false;
        }

        private static object Activate(IMMDevice device, Guid iid)
        {
            Hr.Check(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance), "IMMDevice.Activate");
            return instance;
        }

        private static object? TryActivate(IMMDevice device, Guid iid) =>
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance) >= 0 ? instance : null;

        private static string? ReadFriendlyName(IMMDevice device)
        {
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) < 0) return null;
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) < 0) return null;
            try
            {
                return value.Vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
    }

    [ComVisible(true)]
    internal sealed class SessionNotification(AudioSystem owner) : IAudioSessionNotification
    {
        public int OnSessionCreated(object newSession)
        {
            try { owner.SessionCreated?.Invoke(); } catch { /* never throw into COM */ }
            return 0;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);
}
