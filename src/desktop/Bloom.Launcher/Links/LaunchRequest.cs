using System.Text.RegularExpressions;
using System.Web;

namespace Bloom.Launcher.Links;

/// <summary>
/// What should be launched. Either a structured request (built into an official deep link)
/// or a raw protocol argument received from the browser (passed through unchanged).
/// </summary>
public sealed record LaunchRequest
{
    public long? PlaceId { get; init; }
    public long? UserId { get; init; }
    public string? GameInstanceId { get; init; }
    public string? AccessCode { get; init; }
    public string? LinkCode { get; init; }
    public string? LaunchData { get; init; }

    /// <summary>Raw roblox-player: or roblox: argument from the browser. Never logged in full (may contain tickets).</summary>
    public string? RawProtocolArgument { get; init; }

    public bool OpensAppOnly => RawProtocolArgument is null && PlaceId is null && UserId is null;

    public static LaunchRequest App { get; } = new();

    public static LaunchRequest ForPlace(long placeId) => new() { PlaceId = placeId };

    /// <summary>
    /// Official deep link (create.roblox.com/docs/production/promotion/deeplinks):
    /// roblox://placeId=&lt;id&gt;[&amp;gameInstanceId|accessCode|linkCode|launchData]  or  roblox://userId=&lt;id&gt;.
    /// </summary>
    public string ToDeepLink()
    {
        if (RawProtocolArgument is not null) return RawProtocolArgument;
        var parts = new List<string>();
        if (PlaceId is long place) parts.Add($"placeId={place}");
        else if (UserId is long user) parts.Add($"userId={user}");
        else return "roblox://";
        if (!string.IsNullOrEmpty(GameInstanceId)) parts.Add($"gameInstanceId={Uri.EscapeDataString(GameInstanceId)}");
        if (!string.IsNullOrEmpty(AccessCode)) parts.Add($"accessCode={Uri.EscapeDataString(AccessCode)}");
        if (!string.IsNullOrEmpty(LinkCode)) parts.Add($"linkCode={Uri.EscapeDataString(LinkCode)}");
        if (!string.IsNullOrEmpty(LaunchData)) parts.Add($"launchData={Uri.EscapeDataString(LaunchData)}");
        return "roblox://" + string.Join('&', parts);
    }

    /// <summary>Text safe for logs: no tickets, no access codes.</summary>
    public string ToLogString() =>
        RawProtocolArgument is not null
            ? $"protocol:{RawProtocolArgument.Split(':', 2)[0]}"
            : PlaceId is long p ? $"place {p}{(LinkCode is null ? "" : " (private server)")}{(GameInstanceId is null ? "" : " (server)")}"
            : UserId is long u ? $"user {u}" : "app";
}

public static partial class LinkParser
{
    /// <summary>
    /// Accepts: roblox-player: / roblox: protocol strings, roblox:// deep links,
    /// roblox.com/games/&lt;id&gt;/... links (optionally with privateServerLinkCode),
    /// roblox.com/games/start?placeId=... and a bare place ID.
    /// </summary>
    public static bool TryParse(string? input, out LaunchRequest request)
    {
        request = LaunchRequest.App;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var text = input.Trim().Trim('"');

        if (text.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
        {
            request = new LaunchRequest { RawProtocolArgument = text };
            return true;
        }

        if (text.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase))
        {
            var query = text["roblox:".Length..].TrimStart('/');
            var qIndex = query.IndexOf('?');
            if (qIndex >= 0) query = query[(qIndex + 1)..];
            var values = HttpUtility.ParseQueryString(query);
            request = FromQuery(values) ?? new LaunchRequest { RawProtocolArgument = text };
            return true;
        }

        if (long.TryParse(text, out var bare) && bare > 0)
        {
            request = LaunchRequest.ForPlace(bare);
            return true;
        }

        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase)) return false;

        var queryValues = HttpUtility.ParseQueryString(uri.Query);
        var gamesMatch = GamesPath().Match(uri.AbsolutePath);
        if (gamesMatch.Success)
        {
            request = new LaunchRequest
            {
                PlaceId = long.Parse(gamesMatch.Groups[1].Value),
                LinkCode = queryValues["privateServerLinkCode"],
                GameInstanceId = queryValues["gameInstanceId"] ?? queryValues["jobId"],
            };
            return true;
        }

        if (uri.AbsolutePath.TrimEnd('/').EndsWith("/games/start", StringComparison.OrdinalIgnoreCase)
            && FromQuery(queryValues) is { } start)
        {
            request = start;
            return true;
        }

        return false;
    }

    private static LaunchRequest? FromQuery(System.Collections.Specialized.NameValueCollection values)
    {
        long? place = long.TryParse(values["placeId"], out var p) ? p : null;
        long? user = long.TryParse(values["userId"], out var u) ? u : null;
        if (place is null && user is null) return null;
        return new LaunchRequest
        {
            PlaceId = place,
            UserId = place is null ? user : null,
            GameInstanceId = values["gameInstanceId"],
            AccessCode = values["accessCode"],
            LinkCode = values["linkCode"],
            LaunchData = values["launchData"],
        };
    }

    [GeneratedRegex(@"^/(?:[a-z]{2}(?:-[a-z]{2})?/)?games/(\d+)(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex GamesPath();

    /// <summary>Finds a roblox-player:/roblox: argument in a command line (browser activation).</summary>
    public static string? FindProtocolArgument(IEnumerable<string> args) =>
        args.Select(a => a.Trim().Trim('"'))
            .FirstOrDefault(a => a.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase)
                                 || a.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase));

    /// <summary>Same, for a single command-line string (redirected activations).</summary>
    public static string? FindProtocolArgument(string? commandLine) =>
        commandLine is null ? null : ProtocolInCommandLine().Match(commandLine) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"roblox(?:-player)?:[^\s""]+", RegexOptions.IgnoreCase)]
    private static partial Regex ProtocolInCommandLine();
}
