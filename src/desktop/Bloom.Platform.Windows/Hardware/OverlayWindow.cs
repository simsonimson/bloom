using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Hardware;

/// <summary>
/// Small always-on-top, click-through window drawn with GDI on its own thread. It sits above the
/// Roblox window like any other desktop window — no injection, Roblox is not touched (E10).
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    private const uint WM_APP_UPDATE = 0x8001;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_QUIT = 0x0012;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOPMOST = 0x00000008,
        WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private const uint DT_CALCRECT = 0x400, DT_NOPREFIX = 0x800, DT_LEFT = 0x0;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static int _classCounter;

    private readonly object _gate = new();
    private WndProc? _proc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private IntPtr _font;
    private string _text = "";
    private (int X, int Y, int Right, int Bottom) _anchor;
    private int _corner;
    private bool _visible;
    private double _scale = 1;

    public void Start()
    {
        if (_thread is not null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Overlay" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Shows the text next to a corner of the anchor rectangle (screen pixels). corner: 0 TR, 1 TL, 2 BR, 3 BL.</summary>
    public void Show(string text, int left, int top, int right, int bottom, int corner, double scale)
    {
        lock (_gate)
        {
            _text = text;
            _anchor = (left, top, right, bottom);
            _corner = corner;
            _visible = true;
            _scale = scale;
        }
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero);
    }

    public void Hide()
    {
        lock (_gate) _visible = false;
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_APP_UPDATE, IntPtr.Zero, IntPtr.Zero);
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _proc = WindowProc;
        var instance = GetModuleHandleW(null);
        var className = $"BloomOverlay{Interlocked.Increment(ref _classCounter)}";
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = instance,
            lpszClassName = className,
        };
        RegisterClassExW(ref wc);
        _hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            className, "bloom overlay", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        SetLayeredWindowAttributes(_hwnd, 0, 220, 0x2 /* LWA_ALPHA */);
        ready.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        if (_font != IntPtr.Zero) DeleteObject(_font);
        DestroyWindow(_hwnd);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_APP_UPDATE:
                    Layout();
                    return IntPtr.Zero;
                case WM_ERASEBKGND:
                    return new IntPtr(1);
                case WM_PAINT:
                    Paint();
                    return IntPtr.Zero;
            }
        }
        catch
        {
            // never throw into native code
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void EnsureFont(double scale)
    {
        var height = -(int)Math.Round(13 * scale);
        if (_font != IntPtr.Zero && _fontHeight == height) return;
        if (_font != IntPtr.Zero) DeleteObject(_font);
        _font = CreateFontW(height, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE */, 0, "Segoe UI");
        _fontHeight = height;
    }

    private int _fontHeight;

    private void Layout()
    {
        string text;
        (int X, int Y, int Right, int Bottom) anchor;
        int corner;
        bool visible;
        double scale;
        lock (_gate) { text = _text; anchor = _anchor; corner = _corner; visible = _visible; scale = _scale; }

        if (!visible || text.Length == 0)
        {
            ShowWindow(_hwnd, 0);
            return;
        }

        EnsureFont(scale);
        var dc = GetDC(_hwnd);
        var old = SelectObject(dc, _font);
        var rect = new RECT();
        DrawTextW(dc, text, -1, ref rect, DT_CALCRECT | DT_NOPREFIX | DT_LEFT);
        SelectObject(dc, old);
        ReleaseDC(_hwnd, dc);

        var pad = (int)Math.Round(8 * scale);
        var margin = (int)Math.Round(12 * scale);
        var width = rect.Right - rect.Left + 2 * pad;
        var height = rect.Bottom - rect.Top + 2 * pad;
        var x = corner is 0 or 2 ? anchor.Right - width - margin : anchor.X + margin;
        var y = corner is 0 or 1 ? anchor.Y + margin + (int)Math.Round(36 * scale) /* below Roblox' top bar */ : anchor.Bottom - height - margin;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private void Paint()
    {
        string text;
        double scale;
        lock (_gate) { text = _text; scale = _scale; }
        var dc = BeginPaint(_hwnd, out var ps);
        try
        {
            GetClientRect(_hwnd, out var client);
            var brush = CreateSolidBrush(0x00160F14); // BGR of #140F16 (bloom background)
            FillRect(dc, ref client, brush);
            DeleteObject(brush);

            EnsureFont(scale);
            var old = SelectObject(dc, _font);
            SetBkMode(dc, 1 /* TRANSPARENT */);
            SetTextColor(dc, 0x00F2EEF5);
            var pad = (int)Math.Round(8 * scale);
            var textRect = new RECT { Left = pad, Top = pad, Right = client.Right - pad, Bottom = client.Bottom - pad };
            DrawTextW(dc, text, -1, ref textRect, DT_NOPREFIX | DT_LEFT);
            SelectObject(dc, old);
        }
        finally
        {
            EndPaint(_hwnd, ref ps);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(1));
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
        public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
        public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; public uint priv; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore; public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(IntPtr dc, string text, int count, ref RECT rect, uint format);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
}
