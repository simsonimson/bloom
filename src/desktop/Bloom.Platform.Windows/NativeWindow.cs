using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows;

/// <summary>Small Win32 helpers for top-level windows.</summary>
public static class NativeWindow
{
    /// <summary>Restores (if minimised) and brings a window to the foreground.</summary>
    public static void BringToFront(IntPtr hwnd)
    {
        const int SW_RESTORE = 9, SW_SHOW = 5;
        ShowWindow(hwnd, IsIconic(hwnd) ? SW_RESTORE : SW_SHOW);
        SetForegroundWindow(hwnd);
    }

    /// <summary>Work area (screen without taskbar) of the monitor the window is on, in physical pixels.</summary>
    public static (int X, int Y, int Width, int Height) WorkAreaFor(IntPtr hwnd)
    {
        const uint MONITOR_DEFAULTTONEAREST = 2;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
            return (0, 0, 1920, 1080);
        var r = info.rcWork;
        return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>Whether Windows animation effects are enabled (Settings → Accessibility → Visual effects).</summary>
    public static bool AnimationsEnabled()
    {
        const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
        return !SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, out var enabled, 0) || enabled != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfoW(uint action, uint param, out int value, uint winIni);
}
