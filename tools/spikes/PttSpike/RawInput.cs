// Raw Input listener on a dedicated thread with a message-only window.
// RIDEV_INPUTSINK delivers keyboard and mouse input even while another
// application (Roblox) has focus. Nothing is blocked or modified.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bloom.Spikes.Ptt;

internal readonly record struct InputEvent(int VirtualKey, bool Down, long Timestamp, bool RobloxInForeground);

internal sealed class RawInputListener
{
    private const uint WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const ushort RI_KEY_BREAK = 0x01;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly BlockingCollection<InputEvent> _events;
    private readonly Func<uint, bool> _isRobloxPid;
    private WndProc? _wndProc; // kept alive for the lifetime of the window
    private Thread? _thread;
    private IntPtr _hwnd;

    public string? StartupError { get; private set; }

    public RawInputListener(BlockingCollection<InputEvent> events, Func<uint, bool> isRobloxPid)
    {
        _events = events;
        _isRobloxPid = isRobloxPid;
    }

    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "RawInput", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run(ManualResetEventSlim ready)
    {
        try
        {
            _wndProc = WindowProc;
            var hInstance = GetModuleHandleW(null);
            var className = "BloomPttSpikeRawInput";
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = className,
            };
            if (RegisterClassExW(ref wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx fehlgeschlagen ({Marshal.GetLastWin32Error()})");

            _hwnd = CreateWindowExW(0, className, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx fehlgeschlagen ({Marshal.GetLastWin32Error()})");

            var devices = new[]
            {
                new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RIDEV_INPUTSINK, hwndTarget = _hwnd }, // keyboard
                new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = _hwnd }, // mouse
            };
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                throw new InvalidOperationException($"RegisterRawInputDevices fehlgeschlagen ({Marshal.GetLastWin32Error()})");
        }
        catch (Exception ex)
        {
            StartupError = ex.Message;
            ready.Set();
            return;
        }

        ready.Set();
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            try { HandleRawInput(lParam); }
            catch { /* never let an exception escape into native code */ }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private unsafe void HandleRawInput(IntPtr hRawInput)
    {
        var timestamp = Stopwatch.GetTimestamp();
        uint headerSize = (uint)(8 + 2 * IntPtr.Size);
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 1024) return;

        byte* buffer = stackalloc byte[(int)size];
        if (GetRawInputData(hRawInput, RID_INPUT, (IntPtr)buffer, ref size, headerSize) != size) return;

        uint type = *(uint*)buffer;
        byte* data = buffer + headerSize;
        bool foreground = RobloxInForeground();

        if (type == RIM_TYPEKEYBOARD)
        {
            ushort flags = *(ushort*)(data + 2);
            ushort vkey = *(ushort*)(data + 6);
            if (vkey == 0 || vkey == 0xFF) return; // fake key / overrun
            _events.TryAdd(new InputEvent(vkey, (flags & RI_KEY_BREAK) == 0, timestamp, foreground));
        }
        else if (type == RIM_TYPEMOUSE)
        {
            ushort buttonFlags = *(ushort*)(data + 4);
            if (buttonFlags == 0) return; // movement only
            void Emit(ushort downFlag, ushort upFlag, int vk)
            {
                if ((buttonFlags & downFlag) != 0) _events.TryAdd(new InputEvent(vk, true, timestamp, foreground));
                if ((buttonFlags & upFlag) != 0) _events.TryAdd(new InputEvent(vk, false, timestamp, foreground));
            }
            Emit(0x0001, 0x0002, 0x01); // left
            Emit(0x0004, 0x0008, 0x02); // right
            Emit(0x0010, 0x0020, 0x04); // middle
            Emit(0x0040, 0x0080, 0x05); // XButton1 (Maus 4)
            Emit(0x0100, 0x0200, 0x06); // XButton2 (Maus 5)
        }
    }

    private bool RobloxInForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        return _isRobloxPid(pid);
    }

    public static bool IsPhysicallyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static string KeyName(int vk) => vk switch
    {
        0x01 => "Linke Maustaste",
        0x02 => "Rechte Maustaste",
        0x04 => "Mittlere Maustaste",
        0x05 => "Maus 4",
        0x06 => "Maus 5",
        0x08 => "Rücktaste",
        0x09 => "Tab",
        0x0D => "Enter",
        0x10 => "Shift",
        0x11 => "Strg",
        0x12 => "Alt",
        0x14 => "Feststelltaste",
        0x20 => "Leertaste",
        0xC0 => "Ö / `",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        _ => $"Taste 0x{vk:X2}",
    };

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint numDevices, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
