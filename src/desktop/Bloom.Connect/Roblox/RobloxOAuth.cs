using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bloom.Core.Connect;

namespace Bloom.Connect.Roblox;

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public enum CallbackResult { Ok, StateMismatch, Denied, Invalid }

/// <summary>Thrown when Roblox rejects a refresh token or code (invalid_grant): sign in again.</summary>
public sealed class OAuthGrantException(string message) : Exception(message);

/// <summary>
/// Roblox OAuth 2.0 authorization code flow with PKCE for a public client (no client secret
/// in the app). Endpoints from create.roblox.com/docs/cloud/reference/oauth2.
/// </summary>
public sealed class RobloxOAuth(HttpClient http)
{
    public const string RedirectUri = "bloom://oauth/callback";
    public const string Scopes = "openid profile";
    private const string Base = "https://apis.roblox.com/oauth";

    public static string AuthorizeUrl(string clientId, Pkce pkce, string state) =>
        $"{Base}/v1/authorize?client_id={Uri.EscapeDataString(clientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
        $"&scope={Uri.EscapeDataString(Scopes)}" +
        "&response_type=code" +
        $"&code_challenge={pkce.Challenge}&code_challenge_method=S256" +
        $"&state={Uri.EscapeDataString(state)}";

    public static bool IsCallback(string? argument) =>
        argument is not null && argument.StartsWith(RedirectUri, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses bloom://oauth/callback?code=…&amp;state=… and checks the state value.</summary>
    public static CallbackResult ParseCallback(string uri, string expectedState, out string? code)
    {
        code = null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !IsCallback(uri)) return CallbackResult.Invalid;
        var query = ParseQuery(parsed.Query);
        if (query.ContainsKey("error")) return CallbackResult.Denied;
        if (!query.TryGetValue("state", out var state) || !CryptographicEquals(state, expectedState)) return CallbackResult.StateMismatch;
        if (!query.TryGetValue("code", out code) || string.IsNullOrEmpty(code)) return CallbackResult.Invalid;
        return CallbackResult.Ok;
    }

    public Task<TokenResponse> ExchangeCodeAsync(string clientId, string code, string verifier, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["client_id"] = clientId,
        }, ct);

    /// <summary>Refresh tokens rotate: the returned refresh token replaces the old one, which becomes invalid.</summary>
    public Task<TokenResponse> RefreshAsync(string clientId, string refreshToken, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }, ct);

    public async Task RevokeAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken, ["client_id"] = clientId });
        using var response = await http.PostAsync($"{Base}/v1/token/revoke", content, ct);
        // Best effort: the local copy is deleted either way.
    }

    public async Task<RobloxProfile> GetProfileAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseProfile(doc.RootElement);
    }

    internal static RobloxProfile ParseProfile(JsonElement root)
    {
        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var sub = long.Parse(Str("sub") ?? throw new InvalidDataException("userinfo without sub"));
        var username = Str("preferred_username") ?? sub.ToString();
        DateTimeOffset? created = root.TryGetProperty("created_at", out var c) && c.TryGetInt64(out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;
        return new RobloxProfile(sub, username, Str("name") ?? Str("nickname") ?? username, Str("picture"), created);
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync($"{Base}/v1/token", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new OAuthGrantException(ErrorCode(body) ?? response.StatusCode.ToString());
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<TokenResponse>(body) ?? throw new InvalidDataException("Empty token response");
    }

    private static string? ErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            var key = Uri.UnescapeDataString(i < 0 ? part : part[..i]);
            result[key] = i < 0 ? "" : Uri.UnescapeDataString(part[(i + 1)..].Replace('+', ' '));
        }
        return result;
    }

    private static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    /// <summary>For logs: never write codes or tokens.</summary>
    public static string Redact(string argument) =>
        IsCallback(argument) ? RedirectUri + "?…" : argument;
}
