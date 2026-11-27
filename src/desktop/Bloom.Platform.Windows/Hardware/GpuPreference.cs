using Microsoft.Win32;

namespace Bloom.Platform.Windows.Hardware;

/// <summary>
/// Per-app graphics preference (Settings → Display → Graphics). Stored per executable path under
/// HKCU\Software\Microsoft\DirectX\UserGpuPreferences; takes effect when the app starts.
/// </summary>
public static class GpuPreference
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string HighPerformance = "GpuPreference=2;";

    public static string? Read(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(executable) as string;
    }

    public static void Write(string executable, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(executable, value, RegistryValueKind.String);
    }

    public static void Delete(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(executable, throwOnMissingValue: false);
    }
}
