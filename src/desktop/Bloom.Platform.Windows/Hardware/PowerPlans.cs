using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Hardware;

public sealed record PowerScheme(Guid Id, string Name);

/// <summary>Windows power schemes (powrprof.dll). Switching needs no administrator rights.</summary>
public static class PowerPlans
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private const uint AccessScheme = 16;

    public static Guid? Active()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    public static bool SetActive(Guid scheme)
    {
        var id = scheme;
        return PowerSetActiveScheme(IntPtr.Zero, ref id) == 0;
    }

    public static IReadOnlyList<PowerScheme> All()
    {
        var result = new List<PowerScheme>();
        var buffer = new byte[16];
        for (uint i = 0; ; i++)
        {
            uint size = (uint)buffer.Length;
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, i, buffer, ref size) != 0) break;
            var id = new Guid(buffer);
            result.Add(new PowerScheme(id, FriendlyName(id) ?? id.ToString()));
        }
        return result;
    }

    /// <summary>Ultimate if present, otherwise High performance, otherwise null (many laptops only offer Balanced).</summary>
    public static PowerScheme? BestPerformance()
    {
        var all = All();
        return all.FirstOrDefault(s => s.Id == UltimatePerformance) ?? all.FirstOrDefault(s => s.Id == HighPerformance);
    }

    private static string? FriendlyName(Guid scheme)
    {
        var id = scheme;
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid,
        uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid,
        IntPtr settingGuid, IntPtr buffer, ref uint bufferSize);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
}
