using System.Runtime.InteropServices;
using System.Text;

namespace Bloom.Platform.Windows.Input;

/// <summary>
/// Window and pointer helpers for the Roblox-button method (Blueprint 6.5, strategy D).
/// Only documented user-mode APIs; input is synthesised with SendInput like any accessibility tool.
/// </summary>
public static class WindowControl
{
    public static int ForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    /// <summary>The largest visible, unowned top-level window of a process.</summary>
    public static IntPtr FindMainWindow(int processId)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            if (!GetClientRect(hwnd, out var rect)) return true;
            long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area > bestArea) { best = hwnd; bestArea = area; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    /// <summary>Brings a window to the front and waits until Windows reports it as foreground.</summary>
    public static bool BringToForeground(IntPtr hwnd, TimeSpan timeout)
    {
        if (GetForegroundWindow() == hwnd) return true;
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
        SetForegroundWindow(hwnd);
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < until)
        {
            if (GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(15);
        }
        return GetForegroundWindow() == hwnd;
    }

    public static double ScaleOf(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>Screen point → client point of the window (physical pixels).</summary>
    public static (int X, int Y) ScreenToClient(IntPtr hwnd, int x, int y)
    {
        var p = new POINT { X = x, Y = y };
        ScreenToClientNative(hwnd, ref p);
        return (p.X, p.Y);
    }

    public static (int X, int Y) ClientToScreen(IntPtr hwnd, int x, int y)
    {
        var p = new POINT { X = x, Y = y };
        ClientToScreenNative(hwnd, ref p);
        return (p.X, p.Y);
    }

    public static (int Width, int Height) ClientSize(IntPtr hwnd) =>
        GetClientRect(hwnd, out var r) ? (r.Right - r.Left, r.Bottom - r.Top) : (0, 0);

    /// <summary>False while a game has hidden the pointer (Roblox first person / shift lock).</summary>
    public static bool IsCursorVisible()
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        return GetCursorInfo(ref info) && (info.flags & 0x1 /* CURSOR_SHOWING */) != 0;
    }

    /// <summary>Moves the pointer to a screen point, left-clicks, and moves it back.</summary>
    public static void ClickAt(int x, int y)
    {
        var (oldX, oldY) = CursorPosition();
        SendMove(x, y);
        Thread.Sleep(35); // one or two frames so the game registers the hover
        SendButtons(0x0002 /* LEFTDOWN */);
        Thread.Sleep(40);
        SendButtons(0x0004 /* LEFTUP */);
        Thread.Sleep(35);
        SendMove(oldX, oldY);
    }

    private static void SendMove(int x, int y)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var input = new INPUT
        {
            type = 0,
            mi = new MOUSEINPUT
            {
                dx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1)),
                dy = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1)),
                dwFlags = 0x0001 /* MOVE */ | 0x8000 /* ABSOLUTE */ | 0x4000 /* VIRTUALDESK */,
            },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendButtons(uint flags)
    {
        var input = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = flags } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    /// <summary>Localised name of a keyboard key from the active layout, or null.</summary>
    public static string? KeyboardKeyName(int vk)
    {
        var scan = MapVirtualKeyW((uint)vk, 0 /* MAPVK_VK_TO_VSC */);
        if (scan == 0) return null;
        var extended = vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x6F or 0x90 or 0xA3 or 0xA5;
        var lParam = (int)(scan << 16) | (extended ? 1 << 24 : 0);
        var sb = new StringBuilder(64);
        return GetKeyNameTextW(lParam, sb, sb.Capacity) > 0 ? sb.ToString() : null;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT with the mouse member; the union is padded to the size of the largest member.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", EntryPoint = "ScreenToClient")] private static extern bool ScreenToClientNative(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")] private static extern bool ClientToScreenNative(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetKeyNameTextW(int lParam, StringBuilder name, int size);
}
