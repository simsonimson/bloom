using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bloom.Performance;

public sealed record ServerLocation(string? City, string? Region, string? Country, string? Organisation)
{
    public string Display => string.Join(", ", new[] { City, Region, Country }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
}

/// <summary>
/// Opt-in server location (E4): the server IP goes to ipinfo.io. Results are cached per IP.
/// </summary>
public sealed class ServerLocator(HttpClient http)
{
    private readonly Dictionary<string, ServerLocation?> _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ServerLocation?> LocateAsync(string address, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(address, out var ip) || !NetworkInfo.IsPublic(ip)) return null;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(address, out var cached)) return cached;
            using var response = await http.GetAsync($"https://ipinfo.io/{ip}/json", ct);
            ServerLocation? location = null;
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                location = Parse(doc.RootElement);
            }
            _cache[address] = location;
            return location;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static ServerLocation Parse(JsonElement root)
    {
        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new ServerLocation(Str("city"), Str("region"), Str("country"), Str("org"));
    }
}

public sealed record PublicServer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("maxPlayers")] int MaxPlayers,
    [property: JsonPropertyName("playing")] int Playing,
    [property: JsonPropertyName("fps")] double? Fps,
    [property: JsonPropertyName("ping")] int? Ping);

public sealed record ServerPage(IReadOnlyList<PublicServer> Servers, string? NextCursor);

public enum ServerSort { FewestPlayers, MostPlayers }

public sealed class RateLimitedException() : Exception("Roblox rate limit");

/// <summary>
/// Public server list of a place (games.roblox.com/v1/games/{placeId}/servers/Public). This is a
/// legacy web API without a stability promise, so the browser is marked experimental.
/// "ping" and "fps" are values the servers report themselves, not measured from this PC.
/// </summary>
public sealed class ServerBrowser(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ServerPage> GetAsync(long placeId, ServerSort sort, string? cursor, CancellationToken ct = default)
    {
        var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder={(sort == ServerSort.FewestPlayers ? "Asc" : "Desc")}" +
                  $"&excludeFullGames=true&limit=100{(cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))}";
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RateLimitedException();
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    internal static ServerPage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var servers = new List<PublicServer>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var server = item.Deserialize<PublicServer>(Json);
                if (server is not null && Guid.TryParse(server.Id, out _)) servers.Add(server);
            }
        }
        var next = root.TryGetProperty("nextPageCursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        return new ServerPage(servers, string.IsNullOrEmpty(next) ? null : next);
    }
}
