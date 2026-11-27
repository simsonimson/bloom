using System.Text.Json.Nodes;
using Bloom.Core.Roblox;

namespace Bloom.Connect.Discord;

public sealed record PresenceOptions(bool ShowExperience, bool ShowJoinButton);

/// <summary>Localised texts for the activity (filled from the app's resources).</summary>
public sealed record PresenceTexts(string Roblox, string InGame, string InMenu, string ViewGame, string Join);

/// <summary>Builds the Discord activity for the current Roblox state (C1, C2, C13).</summary>
public static class PresenceBuilder
{
    public const string LogoUrl = "https://raw.githubusercontent.com/simsonimson/bloom/main/src/desktop/Bloom.App/Assets/bloom-logo.png";

    /// <summary>Null clears the presence (Roblox not running).</summary>
    public static JsonObject? Build(ActivityState state, PresenceOptions options, PresenceTexts texts)
    {
        if (!state.RobloxRunning) return null;

        var inGame = state.PlaceId is not null;
        var experience = options.ShowExperience ? state.Experience : null;
        var activity = new JsonObject
        {
            ["type"] = 0,
            ["details"] = Limit(inGame ? experience?.Name ?? texts.Roblox : texts.Roblox),
            ["state"] = Limit(inGame ? texts.InGame : texts.InMenu),
            ["instance"] = false,
        };

        if (state.SessionStartedAt is { } started)
            activity["timestamps"] = new JsonObject { ["start"] = started.ToUnixTimeSeconds() };

        var assets = new JsonObject { ["small_image"] = LogoUrl, ["small_text"] = "bloom" };
        if (inGame && experience?.IconUrl is { } icon)
        {
            assets["large_image"] = icon;
            assets["large_text"] = Limit(experience.Name);
        }
        else
        {
            assets["large_image"] = LogoUrl;
            assets["large_text"] = texts.Roblox;
            assets.Remove("small_image");
            assets.Remove("small_text");
        }
        activity["assets"] = assets;

        if (inGame && options.ShowExperience && state.PlaceId is long place)
        {
            var buttons = new JsonArray
            {
                new JsonObject { ["label"] = Label(texts.ViewGame), ["url"] = $"https://www.roblox.com/games/{place}" },
            };
            if (options.ShowJoinButton && state.JobId is { Length: > 0 } job && Guid.TryParse(job, out _))
                buttons.Add(new JsonObject { ["label"] = Label(texts.Join), ["url"] = JoinUrl(place, job) });
            activity["buttons"] = buttons;
        }
        return activity;
    }

    /// <summary>Opens Roblox in the same server (official web launch link).</summary>
    public static string JoinUrl(long placeId, string jobId) =>
        $"https://www.roblox.com/games/start?placeId={placeId}&gameInstanceId={Uri.EscapeDataString(jobId)}";

    // Discord rejects the whole activity if a text is longer than 128 characters or a button label longer than 32.
    private static string Limit(string text) => text.Length <= 128 ? (text.Length < 2 ? text.PadRight(2) : text) : text[..127] + "…";
    private static string Label(string text) => text.Length <= 32 ? text : text[..31] + "…";
}
