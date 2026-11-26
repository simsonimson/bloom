namespace Bloom.Voice;

/// <summary>Thread-safe set of running Roblox process IDs, fed from process events.</summary>
public sealed class RobloxProcesses
{
    private readonly object _gate = new();
    private readonly HashSet<int> _ids = new();

    public void Add(int pid) { lock (_gate) _ids.Add(pid); }
    public void Remove(int pid) { lock (_gate) _ids.Remove(pid); }

    public void Reset(IEnumerable<int> pids)
    {
        lock (_gate)
        {
            _ids.Clear();
            foreach (var pid in pids) _ids.Add(pid);
        }
    }

    public bool Contains(int pid) { lock (_gate) return _ids.Contains(pid); }
    public bool Any { get { lock (_gate) return _ids.Count > 0; } }
    public int[] Snapshot() { lock (_gate) return _ids.ToArray(); }
}
