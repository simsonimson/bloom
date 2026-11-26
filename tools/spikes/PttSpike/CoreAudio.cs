// Minimal Windows Core Audio interop for the PTT spike (no third-party packages).
// Interface method order follows the Windows SDK headers mmdeviceapi.h,
// audiopolicy.h, audioclient.h and endpointvolume.h exactly.
using System.Runtime.InteropServices;

namespace Bloom.Spikes.Ptt;

internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }
internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;
    public PropertyKey(Guid fmt, int pid) { FormatId = fmt; PropertyId = pid; }
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Vt;
    [FieldOffset(8)] public IntPtr Pointer;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IntPtr client);
    void UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(uint stgmAccess, out IPropertyStore properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetState(out uint state);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);
    // IAudioSessionManager2
    void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    [PreserveSig] int RegisterSessionNotification(IntPtr sessionNotification);
    [PreserveSig] int UnregisterSessionNotification(IntPtr sessionNotification);
    [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
    [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingParam);
    [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    // IAudioSessionControl2
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    void GetPeakValue(out float peak);
    void GetMeteringChannelCount(out uint channelCount);
    void GetChannelsPeakValues(uint channelCount, [Out, MarshalAs(UnmanagedType.LPArray)] float[] peakValues);
    void QueryHardwareSupport(out uint hardwareSupportMask);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IntPtr notify);
    void UnregisterControlChangeNotify(IntPtr notify);
    void GetChannelCount(out uint channelCount);
    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    void GetChannelVolumeLevel(uint channel, out float levelDb);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    void GetVolumeStepInfo(out uint step, out uint stepCount);
    void VolumeStepUp(ref Guid eventContext);
    void VolumeStepDown(ref Guid eventContext);
    void QueryHardwareSupport(out uint hardwareSupportMask);
    void GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

/// <summary>A capture endpoint (microphone) with the interfaces the spike needs.</summary>
internal sealed class CaptureDevice
{
    private static readonly PropertyKey FriendlyNameKey =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IMMDevice Device { get; init; }
    public required IAudioEndpointVolume EndpointVolume { get; init; }
    public required IAudioMeterInformation EndpointMeter { get; init; }
    public required IAudioSessionManager2 SessionManager { get; init; }

    public static CaptureDevice FromDevice(IMMDevice device)
    {
        device.GetId(out var id);
        return new CaptureDevice
        {
            Id = id,
            Name = ReadFriendlyName(device) ?? id,
            Device = device,
            EndpointVolume = (IAudioEndpointVolume)Activate(device, typeof(IAudioEndpointVolume).GUID),
            EndpointMeter = (IAudioMeterInformation)Activate(device, typeof(IAudioMeterInformation).GUID),
            SessionManager = (IAudioSessionManager2)Activate(device, typeof(IAudioSessionManager2).GUID),
        };
    }

    private static object Activate(IMMDevice device, Guid iid)
    {
        const uint ClsCtxAll = 0x17;
        device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance);
        return instance;
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(0 /* STGM_READ */, out var store);
            var key = FriendlyNameKey;
            store.GetValue(ref key, out var value);
            try
            {
                return value.Vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    public bool GetEndpointMute()
    {
        EndpointVolume.GetMute(out var mute);
        return mute;
    }

    public void SetEndpointMute(bool mute)
    {
        var ctx = Guid.Empty;
        EndpointVolume.SetMute(mute, ref ctx);
    }

    public float EndpointPeak()
    {
        try { EndpointMeter.GetPeakValue(out var p); return p; }
        catch (COMException) { return 0f; }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);
}

/// <summary>One audio session (one app's stream group) on a capture endpoint.</summary>
internal sealed class CaptureSession
{
    public required uint ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string InstanceId { get; init; }
    public required CaptureDevice Device { get; init; }
    public required ISimpleAudioVolume Volume { get; init; }
    public required IAudioMeterInformation Meter { get; init; }
    public required int State { get; init; } // 0 inactive, 1 active, 2 expired

    public bool GetMute()
    {
        Volume.GetMute(out var mute);
        return mute;
    }

    public void SetMute(bool mute)
    {
        var ctx = Guid.Empty;
        Volume.SetMute(mute, ref ctx);
    }

    public float Peak()
    {
        try { Meter.GetPeakValue(out var p); return p; }
        catch (COMException) { return 0f; }
    }
}

internal static class CoreAudio
{
    private static IMMDeviceEnumerator? _enumerator;
    private static IMMDeviceEnumerator Enumerator =>
        _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    public static List<CaptureDevice> GetActiveCaptureDevices()
    {
        var result = new List<CaptureDevice>();
        Enumerator.EnumAudioEndpoints(EDataFlow.Capture, 0x1 /* DEVICE_STATE_ACTIVE */, out var collection);
        collection.GetCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            try
            {
                collection.Item(i, out var device);
                result.Add(CaptureDevice.FromDevice(device));
            }
            catch (COMException)
            {
                // Device vanished between enumeration and activation.
            }
        }
        return result;
    }

    public static string? GetDefaultCaptureId(ERole role)
    {
        if (Enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, role, out var device) != 0 || device is null)
            return null;
        device.GetId(out var id);
        return id;
    }

    public static List<CaptureSession> GetSessions(CaptureDevice device)
    {
        var result = new List<CaptureSession>();
        device.SessionManager.GetSessionEnumerator(out var sessions);
        sessions.GetCount(out var count);
        for (var i = 0; i < count; i++)
        {
            try
            {
                sessions.GetSession(i, out var raw);
                var control = (IAudioSessionControl2)raw;
                control.GetProcessId(out var pid);
                control.GetState(out var state);
                string instanceId = $"{device.Id}#{i}";
                if (control.GetSessionInstanceIdentifier(out var idPtr) == 0 && idPtr != IntPtr.Zero)
                {
                    instanceId = Marshal.PtrToStringUni(idPtr) ?? instanceId;
                    Marshal.FreeCoTaskMem(idPtr);
                }
                result.Add(new CaptureSession
                {
                    ProcessId = pid,
                    ProcessName = ProcessNameOf(pid),
                    InstanceId = instanceId,
                    Device = device,
                    Volume = (ISimpleAudioVolume)raw,
                    Meter = (IAudioMeterInformation)raw,
                    State = state,
                });
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // Session expired while enumerating.
            }
        }
        return result;
    }

    private static string ProcessNameOf(uint pid)
    {
        if (pid == 0) return "(System)";
        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return $"PID {pid}"; }
    }
}
