using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Hardware;

/// <summary>
/// GPU usage and dedicated memory of one process from the Windows performance counters
/// (the same source as Task Manager). No administrator rights needed. Use from one thread.
/// </summary>
public sealed class GpuCounters : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x200;
    private const uint PDH_FMT_LARGE = 0x400;
    private const uint PDH_FMT_NOCAP100 = 0x8000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    private const uint PDH_CSTATUS_VALID_DATA = 0x0;
    private const uint PDH_CSTATUS_NEW_DATA = 0x1;

    private IntPtr _query;
    private IntPtr _engine;
    private IntPtr _memory;

    public bool Available { get; }

    public GpuCounters()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) return;
        var engine = PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engine) == 0;
        PdhAddEnglishCounterW(_query, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out _memory);
        Available = engine;
        PdhCollectQueryData(_query); // rate counters need a first sample
    }

    /// <summary>Collects a sample. Usage = busiest engine type (3D, compute, …) like Task Manager.</summary>
    public (double? Percent, long? DedicatedBytes) Sample(int processId)
    {
        if (!Available || PdhCollectQueryData(_query) != 0) return (null, null);
        var prefix = $"pid_{processId}_";

        double? percent = null;
        var engines = Read(_engine, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100);
        if (engines is not null)
        {
            var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in engines)
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var i = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                var type = i < 0 ? "?" : name[(i + 8)..];
                byType[type] = byType.GetValueOrDefault(type) + BitConverter.Int64BitsToDouble(value);
            }
            percent = byType.Count == 0 ? 0 : Math.Min(100, byType.Values.Max());
        }

        long? memory = null;
        if (_memory != IntPtr.Zero && Read(_memory, PDH_FMT_LARGE) is { } items)
            memory = items.Where(i => i.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Sum(i => i.Raw);

        return (percent, memory);
    }

    /// <summary>Instance name and the raw 8-byte value (double bits or long, depending on the format).</summary>
    private static List<(string Name, long Raw)>? Read(IntPtr counter, uint format)
    {
        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, format, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, format, ref size, out var count, buffer) != 0) return null;
            var result = new List<(string, long)>((int)count);
            var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (item.CStatus is not (PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA)) continue;
                result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr Name;
        public uint CStatus;
        // 4 bytes padding before the 8-byte union on x64 (and x86, where the union is 8-aligned).
        public uint Padding;
        public long Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
}
