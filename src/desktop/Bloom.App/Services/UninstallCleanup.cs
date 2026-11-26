using Bloom.Core;
using Bloom.Core.Settings;
using Bloom.Data;
using Bloom.Launcher.Installation;
using Bloom.Platform.Windows;

namespace Bloom.App.Services;

/// <summary>
/// Runs from the Velopack uninstall hook (must be fast, no UI): gives the roblox-player/roblox
/// protocols back to their previous owner and removes the autostart entry.
/// </summary>
internal static class UninstallCleanup
{
    public static void Run()
    {
        try
        {
            var paths = new BloomPaths();
            var settings = File.Exists(paths.Database) ? new AppSettings(new SqliteSettingsStore(new BloomDatabase(paths.Database))) : null;
            ProtocolRegistration.Restore(settings?.ProtocolBackup ?? new Dictionary<string, string?>(), "bloom.exe");
        }
        catch
        {
            // Best effort: the uninstall must never fail because of cleanup.
        }
        try { Autostart.Set(false, ""); } catch { }
    }
}
