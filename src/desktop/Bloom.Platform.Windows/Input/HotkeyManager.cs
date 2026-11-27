using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Input;

/// <summary>
/// System-wide hotkeys with RegisterHotKey (E6). Combinations need a modifier, so they do not
/// collide with game keys. Registration happens on the hotkey thread that also receives WM_HOTKEY.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_APP_APPLY = 0x8001;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly ConcurrentQueue<(IReadOnlyDictionary<int, (uint Modifiers, int Key)> Bindings, TaskCompletionSource<IReadOnlyList<int>> Done)> _pending = new();
    private readonly HashSet<int> _registered = new();
    private Thread? _thread;
    private uint _threadId;

    /// <summary>Raised on the hotkey thread with the binding ID.</summary>
    public event Action<int>? Pressed;

    public void Start()
    {
        if (_thread is not null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Hotkeys" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Replaces all hotkeys. Returns the IDs that Windows refused (already used by another app).</summary>
    public Task<IReadOnlyList<int>> SetAsync(IReadOnlyDictionary<int, (uint Modifiers, int Key)> bindings)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Enqueue((bindings, tcs));
        if (_threadId == 0 || !PostThreadMessageW(_threadId, WM_APP_APPLY, IntPtr.Zero, IntPtr.Zero))
            tcs.TrySetResult(bindings.Keys.ToList());
        return tcs.Task;
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        PeekMessageW(out _, IntPtr.Zero, 0, 0, 0); // create the thread's message queue
        ready.Set();
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                try { Pressed?.Invoke((int)msg.wParam); } catch { }
            }
            else if (msg.message == WM_APP_APPLY)
            {
                while (_pending.TryDequeue(out var job)) Apply(job.Bindings, job.Done);
            }
        }
        foreach (var id in _registered) UnregisterHotKey(IntPtr.Zero, id);
    }

    private void Apply(IReadOnlyDictionary<int, (uint Modifiers, int Key)> bindings, TaskCompletionSource<IReadOnlyList<int>> done)
    {
        foreach (var id in _registered) UnregisterHotKey(IntPtr.Zero, id);
        _registered.Clear();
        var failed = new List<int>();
        foreach (var (id, (modifiers, key)) in bindings)
        {
            if (RegisterHotKey(IntPtr.Zero, id, modifiers | MOD_NOREPEAT, (uint)key)) _registered.Add(id);
            else failed.Add(id);
        }
        done.TrySetResult(failed);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(1));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; public uint priv; }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
