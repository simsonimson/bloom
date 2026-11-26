// Raw Input listener on a dedicated thread with a message-only window (ADR-004).
// RIDEV_INPUTSINK delivers keyboard and mouse input even while another application (Roblox)
// has focus. Nothing is blocked, modified or injected.
using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Input;

/// <summary>A key or mouse button changed. Virtual-key codes; mouse buttons use VK_LBUTTON..VK_XBUTTON2.</summary>
public readonly record struct RawInputEvent(int VirtualKey, bool Down, int ForegroundProcessId);

public sealed class RawInputListener : IDisposable
{
    private const uint WM_INPUT = 0x00FF;
    private const uint WM_QUIT = 0x0012;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const ushort RI_KEY_BREAK = 0x01;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static int _classCounter;

    private readonly Action<RawInputEvent> _handler;
    private WndProc? _wndProc; // kept alive for the lifetime of the window
    private Thread? _thread;
    private uint _threadId;

    public RawInputListener(Action<RawInputEvent> handler) => _handler = handler;

    public string? StartupError { get; private set; }

    public bool Start()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "RawInput", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        return StartupError is null;
    }

    private void Run(ManualResetEventSlim ready)
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _wndProc = WindowProc;
            var hInstance = GetModuleHandleW(null);
            var className = $"BloomRawInput{Interlocked.Increment(ref _classCounter)}";
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = className,
            };
            if (RegisterClassExW(ref wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()})");

            var hwnd = CreateWindowExW(0, className, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");

            var devices = new[]
            {
                new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd }, // keyboard
                new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd }, // mouse
            };
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                throw new InvalidOperationException($"RegisterRawInputDevices failed ({Marshal.GetLastWin32Error()})");
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
        uint headerSize = (uint)(8 + 2 * IntPtr.Size);
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 1024) return;

        byte* buffer = stackalloc byte[(int)size];
        if (GetRawInputData(hRawInput, RID_INPUT, (IntPtr)buffer, ref size, headerSize) != size) return;

        uint type = *(uint*)buffer;
        byte* data = buffer + headerSize;
        var foreground = WindowControl.ForegroundProcessId();

        if (type == RIM_TYPEKEYBOARD)
        {
            ushort flags = *(ushort*)(data + 2);
            ushort vkey = *(ushort*)(data + 6);
            if (vkey == 0 || vkey == 0xFF) return; // fake key / overrun
            _handler(new RawInputEvent(vkey, (flags & RI_KEY_BREAK) == 0, foreground));
        }
        else if (type == RIM_TYPEMOUSE)
        {
            ushort buttonFlags = *(ushort*)(data + 4);
            if (buttonFlags == 0) return; // movement only
            void Emit(ushort downFlag, ushort upFlag, int vk)
            {
                if ((buttonFlags & downFlag) != 0) _handler(new RawInputEvent(vk, true, foreground));
                if ((buttonFlags & upFlag) != 0) _handler(new RawInputEvent(vk, false, foreground));
            }
            Emit(0x0001, 0x0002, 0x01); // left
            Emit(0x0004, 0x0008, 0x02); // right
            Emit(0x0010, 0x0020, 0x04); // middle
            Emit(0x0040, 0x0080, 0x05); // XButton1 (mouse 4)
            Emit(0x0100, 0x0200, 0x06); // XButton2 (mouse 5)
        }
    }

    /// <summary>Physical state of a key or mouse button right now (watchdog, ADR-004).</summary>
    public static bool IsPhysicallyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(1));
    }

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

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);
}
