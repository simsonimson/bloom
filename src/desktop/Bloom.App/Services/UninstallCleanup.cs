using Bloom.Core;
using Bloom.Core.Settings;
using Bloom.Data;
using Bloom.Launcher.Installation;
using Bloom.Platform.Windows;
using Bloom.Platform.Windows.Audio;
using Bloom.Voice;

namespace Bloom.App.Services;

/// <summary>
/// Runs from the Velopack uninstall hook (must be fast, no UI): gives the roblox-player/roblox
/// protocols back to their previous owner, removes the autostart entry and restores a microphone
/// that a crashed bloom may have left muted.
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
        RestoreMicrophone();
    }

    private static void RestoreMicrophone()
    {
        // Core Audio on its own MTA thread; bounded so the uninstall never hangs.
        var thread = new Thread(() =>
        {
            try
            {
                var store = new JournalStore(new BloomPaths().AudioJournal);
                if (store.Read() is { Active: true, Method: Core.Voice.PttMethod.SystemWide } journal)
                {
                    using var audio = new AudioSystem();
                    audio.GetCaptureDevices();
                    JournalRecovery.RestoreEndpoint(journal, audio);
                }
                store.Delete();
            }
            catch
            {
                // Best effort.
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(3));
    }
}
