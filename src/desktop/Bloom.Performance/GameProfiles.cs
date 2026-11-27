using System.Text.Json;
using Bloom.Core.Performance;

namespace Bloom.Performance;

/// <summary>Per-game settings (E5), stored as JSON next to the other bloom data.</summary>
public sealed class GameProfileStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private string Path => System.IO.Path.Combine(dataDirectory, "game-profiles.json");

    public event Action? Changed;

    public IReadOnlyList<GameProfile> All()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(Path)
                    ? JsonSerializer.Deserialize<List<GameProfile>>(File.ReadAllText(Path), Json) ?? []
                    : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return [];
            }
        }
    }

    public GameProfile? Find(long universeId) => All().FirstOrDefault(p => p.UniverseId == universeId);

    public void Save(GameProfile profile) => Write(All().Where(p => p.UniverseId != profile.UniverseId).Append(profile));

    public void Remove(long universeId) => Write(All().Where(p => p.UniverseId != universeId));

    /// <summary>Replaces all profiles (backup import).</summary>
    public void ReplaceAll(IEnumerable<GameProfile> profiles) => Write(profiles);

    private void Write(IEnumerable<GameProfile> profiles)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(dataDirectory);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), Json));
            File.Move(temp, Path, overwrite: true);
        }
        Changed?.Invoke();
    }
}
