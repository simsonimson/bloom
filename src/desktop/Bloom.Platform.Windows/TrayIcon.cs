using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows;

/// <summary>
/// Notification-area icon via Shell_NotifyIcon. Must be created on a thread that pumps
/// Win32 messages (the WinUI UI thread does). Re-adds itself when Explorer restarts.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000 + 1;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_GRAYED = 0x1;
    private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;

    private static int _classCounter;
    private readonly WndProc _wndProc;
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private string _tooltip;
    private bool _added;
    private bool _disposed;

    /// <summary>Menu entries; null entries become separators. Rebuilt every time the menu opens.</summary>
    public Func<IReadOnlyList<TrayMenuItem?>>? MenuProvider { get; set; }

    /// <summary>Left click (single or double).</summary>
    public event EventHandler? Activated;

    public TrayIcon(string iconPath, string tooltip)
    {
        _tooltip = tooltip;
        _wndProc = WindowProc;
        var className = $"BloomTrayWindow{Interlocked.Increment(ref _classCounter)}";
        var hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = className,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()})");

        // A hidden top-level window (not message-only) so it receives the TaskbarCreated broadcast.
        _hwnd = CreateWindowExW(0, className, "bloom tray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");

        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        _icon = LoadIconFile(iconPath);
        Add();
    }

    public void SetIcon(string iconPath)
    {
        var old = _icon;
        _icon = LoadIconFile(iconPath);
        Modify(NIF_ICON);
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        Modify(NIF_TIP);
    }

    /// <summary>Shows a Windows notification from the tray icon.</summary>
    public void ShowNotification(string title, string text)
    {
        var data = CreateData(NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = 0x1; // NIIF_INFO
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private void Modify(uint flags)
    {
        if (!_added) return;
        var data = CreateData(flags);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = Truncate(_tooltip, 127),
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_APP_TRAY)
            {
                var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse is WM_LBUTTONUP or WM_LBUTTONDBLCLK) Activated?.Invoke(this, EventArgs.Empty);
                else if (mouse == WM_RBUTTONUP) ShowMenu();
                return IntPtr.Zero;
            }
            if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
            {
                Add();
                return IntPtr.Zero;
            }
        }
        catch
        {
            // Never let managed exceptions cross into native code.
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item is null) AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                else AppendMenuW(menu, MF_STRING | (item.Enabled ? 0 : MF_GRAYED), (UIntPtr)(uint)(i + 1), item.Text);
            }
            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd); // required so the menu closes when clicking elsewhere
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
            if (cmd > 0 && cmd <= items.Count) items[cmd - 1]?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static IntPtr LoadIconFile(string path)
    {
        const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
        var size = GetSystemMetrics(49); // SM_CXSMICON
        return LoadImageW(IntPtr.Zero, path, IMAGE_ICON, size, size, LR_LOADFROMFILE);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        DestroyWindow(_hwnd);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

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
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}

public sealed record TrayMenuItem(string Text, Action Invoke, bool Enabled = true);
