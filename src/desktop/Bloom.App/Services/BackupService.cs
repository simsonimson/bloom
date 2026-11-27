using System.Text.Json;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Data;
using Bloom.Launcher.FastFlags;

namespace Bloom.App.Services;

/// <summary>
/// Export/import of bloom settings, favourites and FastFlags (Blueprint L9).
/// Never contains tokens or the protocol backup (machine specific).
/// </summary>
public sealed class BackupService(SqliteSettingsStore settings, IFavoritesStore favorites, FastFlagService fastFlags,
    Bloom.Performance.GameProfileStore profiles)
{
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> MachineSpecific =
    [
        AppSettings.Keys.ProtocolBackup,
        AppSettings.Keys.BootstrapperFailures,
        AppSettings.Keys.StartWithWindows,
        AppSettings.Keys.BootstrapperEnabled,
        AppSettings.Keys.PttDeviceId,
        AppSettings.Keys.PttButton,
        AppSettings.Keys.PerfRestorePowerScheme,
        AppSettings.Keys.PerfGpuBackup,
    ];

    public async Task ExportAsync(string path)
    {
        var file = new BackupFile(
            FormatVersion,
            DateTimeOffset.UtcNow,
            settings.GetAll().Where(kv => !MachineSpecific.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            favorites.All().Select(f => new BackupFavorite(f.PlaceId, f.Name, f.IconUrl)).ToList(),
            fastFlags.Load().ToDictionary(f => f.Name, f => f.Value),
            profiles.All().ToList());
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file, Json));
    }

    /// <returns>Number of restored items.</returns>
    public async Task<int> ImportAsync(string path)
    {
        var file = JsonSerializer.Deserialize<BackupFile>(await File.ReadAllTextAsync(path), Json)
                   ?? throw new InvalidDataException("Empty backup");
        if (file.Version > FormatVersion) throw new InvalidDataException($"Backup version {file.Version} is newer than this bloom");

        var count = 0;
        foreach (var (key, value) in file.Settings ?? new())
        {
            if (MachineSpecific.Contains(key)) continue;
            settings.SetRaw(key, value);
            count++;
        }
        foreach (var fav in file.Favorites ?? [])
        {
            favorites.Add(fav.PlaceId, fav.Name, fav.IconUrl);
            count++;
        }
        if (file.FastFlags is { Count: > 0 } flags)
        {
            fastFlags.Save(flags.Select(kv => new FastFlag(kv.Key, kv.Value)));
            count += flags.Count;
        }
        if (file.GameProfiles is { Count: > 0 } games)
        {
            profiles.ReplaceAll(games);
            count += games.Count;
        }
        return count;
    }

    private sealed record BackupFile(
        int Version,
        DateTimeOffset CreatedAt,
        Dictionary<string, string>? Settings,
        List<BackupFavorite>? Favorites,
        Dictionary<string, string>? FastFlags,
        List<Bloom.Core.Performance.GameProfile>? GameProfiles = null);

    private sealed record BackupFavorite(long PlaceId, string Name, string? IconUrl);
}
