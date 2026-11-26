using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Bloom.Core.Roblox;

namespace Bloom.Activity;

/// <summary>
/// Looks up experience name and icon from public Roblox web APIs (no login), cached for 24 h.
/// Failures return the cached value or null; callers fall back to "Roblox".
/// </summary>
public sealed class ExperienceResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private readonly HttpClient _http;
    private readonly IExperienceCache _cache;
    private readonly Action<string, Exception?>? _log;

    public ExperienceResolver(HttpClient http, IExperienceCache cache, Action<string, Exception?>? log = null)
    {
        _http = http;
        _cache = cache;
        _log = log;
    }

    public async Task<ExperienceInfo?> ResolveAsync(long placeId, long? universeHint = null, CancellationToken ct = default)
    {
        var cached = _cache.GetByPlace(placeId);
        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < CacheLifetime) return cached;

        try
        {
            var universeId = universeHint ?? (await _http.GetFromJsonAsync<UniverseResponse>(
                $"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct))?.UniverseId;
            if (universeId is null or 0) return cached;

            var games = await _http.GetFromJsonAsync<DataResponse<GameResponse>>(
                $"https://games.roblox.com/v1/games?universeIds={universeId}", ct);
            var game = games?.Data.FirstOrDefault();
            if (game is null) return cached;

            string? icon = null;
            try
            {
                var icons = await _http.GetFromJsonAsync<DataResponse<ThumbnailResponse>>(
                    $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&size=150x150&format=Png&isCircular=false", ct);
                icon = icons?.Data.FirstOrDefault(t => t.State == "Completed")?.ImageUrl;
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
            {
                _log?.Invoke($"Icon lookup for universe {universeId} failed", ex);
            }

            var info = new ExperienceInfo(universeId.Value, game.RootPlaceId, game.Name, icon, DateTimeOffset.UtcNow);
            _cache.Put(placeId, info);
            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _log?.Invoke($"Experience lookup for place {placeId} failed", ex);
            return cached;
        }
    }

    private sealed record UniverseResponse([property: JsonPropertyName("universeId")] long? UniverseId);
    private sealed record DataResponse<T>([property: JsonPropertyName("data")] List<T> Data);
    private sealed record GameResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("rootPlaceId")] long RootPlaceId,
        [property: JsonPropertyName("name")] string Name);
    private sealed record ThumbnailResponse(
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("imageUrl")] string? ImageUrl);
}
