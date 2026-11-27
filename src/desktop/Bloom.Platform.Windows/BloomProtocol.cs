using Microsoft.Win32;

namespace Bloom.Platform.Windows;

/// <summary>Registers bloom:// for the current user (OAuth sign-in returns to bloom through it).</summary>
public static class BloomProtocol
{
    public const string Scheme = "bloom";
    private const string KeyPath = @"Software\Classes\bloom";

    public static void Ensure(string bloomExecutable)
    {
        var command = $"\"{bloomExecutable}\" \"%1\"";
        using (var existing = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command"))
        {
            if (existing?.GetValue(null) as string == command) return;
        }
        using var root = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        root.SetValue(null, "URL:bloom Protocol");
        root.SetValue("URL Protocol", "");
        using (var icon = root.CreateSubKey("DefaultIcon", writable: true))
            icon.SetValue(null, $"\"{bloomExecutable}\",0");
        using var open = root.CreateSubKey(@"shell\open\command", writable: true);
        open.SetValue(null, command);
    }

    public static void Remove() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}
