using System.Text.Json;
using Bloom.Core.Voice;

namespace Bloom.Voice;

/// <summary>
/// What bloom changed, written before every change (Blueprint 6.5) so Bloom.Guard and the next
/// bloom start can restore the safe or original state.
/// </summary>
public sealed record AudioJournal(
    PttMethod Method,
    string? DeviceId,
    bool? OriginalEndpointMute,
    bool Active,
    bool PendingSessionUnmute,
    int BloomProcessId,
    DateTimeOffset WrittenAt);

public sealed class JournalStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();

    public string Path { get; } = path;

    public AudioJournal? Read()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(Path) ? JsonSerializer.Deserialize<AudioJournal>(File.ReadAllText(Path), Json) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public void Write(AudioJournal journal)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(journal, Json));
            File.Move(temp, Path, overwrite: true);
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            try { File.Delete(Path); } catch (IOException) { }
        }
    }
}
