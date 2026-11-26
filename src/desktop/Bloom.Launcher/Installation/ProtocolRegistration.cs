using Microsoft.Win32;

namespace Bloom.Launcher.Installation;

/// <summary>
/// Registers bloom as handler for roblox-player: and roblox: (opt-in, per user, HKCU).
/// The previous handlers are returned so they can be restored when the bootstrapper is
/// switched off or bloom is uninstalled (Blueprint 6.11).
/// </summary>
public static class ProtocolRegistration
{
    public static readonly string[] Schemes = ["roblox-player", "roblox"];

    public static bool IsRegisteredTo(string bloomExecutable) =>
        Schemes.All(s => RobloxInstallation.ReadHandler(s)?.Contains(bloomExecutable, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Registers bloom and returns the previous command per scheme (null where none existed).</summary>
    public static Dictionary<string, string?> Register(string bloomExecutable)
    {
        var previous = new Dictionary<string, string?>();
        foreach (var scheme in Schemes)
        {
            var current = RobloxInstallation.ReadHandler(scheme);
            // Never back up our own handler as "previous".
            previous[scheme] = current?.Contains(bloomExecutable, StringComparison.OrdinalIgnoreCase) == true ? null : current;

            using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}", writable: true);
            root.SetValue(null, $"URL: {scheme} Protocol");
            root.SetValue("URL Protocol", "");
            using (var icon = root.CreateSubKey("DefaultIcon", writable: true))
                icon.SetValue(null, $"\"{bloomExecutable}\",0");
            using var command = root.CreateSubKey(@"shell\open\command", writable: true);
            command.SetValue(null, $"\"{bloomExecutable}\" \"%1\"");
        }
        return previous;
    }

    /// <summary>Restores previous handlers. Schemes without a previous handler are removed if they point to bloom.</summary>
    public static void Restore(IReadOnlyDictionary<string, string?> previous, string bloomExecutable)
    {
        foreach (var scheme in Schemes)
        {
            var current = RobloxInstallation.ReadHandler(scheme);
            var ours = current?.Contains(bloomExecutable, StringComparison.OrdinalIgnoreCase) == true
                       || current?.Contains("bloom.exe", StringComparison.OrdinalIgnoreCase) == true;
            if (!ours) continue; // someone else (e.g. the official launcher) already took it back

            previous.TryGetValue(scheme, out var command);
            if (command is null)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{scheme}", throwOnMissingSubKey: false);
                continue;
            }
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}\shell\open\command", writable: true);
            key.SetValue(null, command);
            var exe = RobloxInstallation.ExecutableFromCommand(command);
            if (exe is not null)
            {
                using var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}\DefaultIcon", writable: true);
                icon.SetValue(null, $"\"{exe}\",0");
            }
        }
    }
}
