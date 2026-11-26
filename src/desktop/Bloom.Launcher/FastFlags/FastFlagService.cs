using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bloom.Launcher.FastFlags;

public enum FastFlagKind { Bool, Int, String }

public sealed record FastFlag(string Name, string Value)
{
    public FastFlagKind Kind => FastFlagService.KindOf(Name);
}

public enum AllowlistStatus { Unknown, Allowed, NotAllowed }

public sealed record FastFlagAllowlist(IReadOnlySet<string> Names, DateTimeOffset FetchedAt, bool FromCache)
{
    public static FastFlagAllowlist Empty { get; } = new(new HashSet<string>(), DateTimeOffset.MinValue, true);
    public bool IsAvailable => Names.Count > 0;

    public AllowlistStatus StatusOf(string name) =>
        !IsAvailable ? AllowlistStatus.Unknown : Names.Contains(name) ? AllowlistStatus.Allowed : AllowlistStatus.NotAllowed;
}

/// <summary>
/// Local FastFlags (Blueprint L7/L8). Since 29.09.2025 the Roblox client only honours flags on
/// its allowlist, so bloom only lets users save allowlisted flags. The allowlist endpoint is not
/// a documented API: the parser collects anything that looks like a flag name and treats an
/// empty result as "allowlist unavailable" instead of guessing.
/// </summary>
public sealed partial class FastFlagService
{
    private const string AllowlistUrl = "https://clientsettingscdn.roblox.com/v2/settings/secured-settings/PCDesktopClient";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly HttpClient _http;
    private readonly string _flagsFile;
    private readonly string _allowlistCacheFile;
    private readonly Action<string, Exception?>? _log;

    public FastFlagService(HttpClient http, string dataDirectory, Action<string, Exception?>? log = null)
    {
        _http = http;
        _flagsFile = Path.Combine(dataDirectory, "fastflags.json");
        _allowlistCacheFile = Path.Combine(dataDirectory, "fastflag-allowlist.json");
        _log = log;
    }

    public static FastFlagKind KindOf(string name)
    {
        var core = name.TrimStart('D', 'S');
        return core.StartsWith("FFlag", StringComparison.Ordinal) ? FastFlagKind.Bool
             : core.StartsWith("FInt", StringComparison.Ordinal) ? FastFlagKind.Int
             : FastFlagKind.String;
    }

    public static bool IsValidName(string name) => FlagName().IsMatch(name);

    /// <summary>Normalises a value for its kind, or returns null if it does not fit.</summary>
    public static string? NormalizeValue(string name, string value)
    {
        value = value.Trim();
        return KindOf(name) switch
        {
            FastFlagKind.Bool => value.ToLowerInvariant() switch { "true" or "1" => "True", "false" or "0" => "False", _ => null },
            FastFlagKind.Int => long.TryParse(value, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            _ => value,
        };
    }

    public IReadOnlyList<FastFlag> Load()
    {
        try
        {
            if (!File.Exists(_flagsFile)) return [];
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_flagsFile)) ?? new();
            return dict.Select(kv => new FastFlag(kv.Key, kv.Value)).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log?.Invoke("FastFlags could not be read", ex);
            return [];
        }
    }

    public void Save(IEnumerable<FastFlag> flags)
    {
        var dict = flags.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(_flagsFile)!);
        File.WriteAllText(_flagsFile, JsonSerializer.Serialize(dict, Indented));
    }

    /// <summary>Writes the flags into a Roblox version folder (ClientSettings\ClientAppSettings.json).</summary>
    public void Apply(string versionDirectory, IEnumerable<FastFlag>? flags = null)
    {
        var list = (flags ?? Load()).ToList();
        var dir = Path.Combine(versionDirectory, "ClientSettings");
        var file = Path.Combine(dir, "ClientAppSettings.json");
        if (list.Count == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        Directory.CreateDirectory(dir);
        var json = new JsonObject();
        foreach (var flag in list) json[flag.Name] = flag.Value;
        File.WriteAllText(file, json.ToJsonString(Indented));
    }

    /// <summary>Fetches the allowlist; falls back to the last cached copy.</summary>
    public async Task<FastFlagAllowlist> GetAllowlistAsync(CancellationToken ct = default)
    {
        try
        {
            var text = await _http.GetStringAsync(AllowlistUrl, ct);
            var names = ExtractFlagNames(text);
            if (names.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_allowlistCacheFile)!);
                File.WriteAllText(_allowlistCacheFile, JsonSerializer.Serialize(new CacheFile(DateTimeOffset.UtcNow, names.Order(StringComparer.Ordinal).ToList())));
                return new FastFlagAllowlist(names, DateTimeOffset.UtcNow, FromCache: false);
            }
            _log?.Invoke("Allowlist response contained no flag names", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _log?.Invoke("Allowlist download failed", ex);
        }

        try
        {
            if (File.Exists(_allowlistCacheFile)
                && JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_allowlistCacheFile)) is { } cache)
                return new FastFlagAllowlist(cache.Names.ToHashSet(StringComparer.Ordinal), cache.FetchedAt, FromCache: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log?.Invoke("Allowlist cache unreadable", ex);
        }
        return FastFlagAllowlist.Empty;
    }

    /// <summary>Collects flag-like names from any JSON shape (property names and string values).</summary>
    internal static HashSet<string> ExtractFlagNames(string json)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return names; }
        Walk(root);
        return names;

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (IsValidName(key)) names.Add(key);
                        Walk(value);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array) Walk(item);
                    break;
                case JsonValue value when value.TryGetValue<string>(out var s) && IsValidName(s):
                    names.Add(s);
                    break;
            }
        }
    }

    private sealed record CacheFile(DateTimeOffset FetchedAt, List<string> Names);

    [GeneratedRegex(@"^(D|S)?F(Flag|Int|String|Log)[A-Za-z0-9_]+$")]
    private static partial Regex FlagName();
}
