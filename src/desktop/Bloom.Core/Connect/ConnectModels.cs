namespace Bloom.Core.Connect;

public enum DiscordStatus
{
    /// <summary>Rich Presence is switched off in bloom.</summary>
    Disabled,
    /// <summary>No Discord application ID configured (self-built bloom without IDs).</summary>
    NotConfigured,
    /// <summary>Discord desktop app not running or not reachable.</summary>
    NotRunning,
    Connected,
}

public sealed record DiscordUser(string Id, string Username, string? GlobalName, string? AvatarHash)
{
    public string DisplayName => string.IsNullOrWhiteSpace(GlobalName) ? Username : GlobalName;

    public string? AvatarUrl => AvatarHash is null ? null : $"https://cdn.discordapp.com/avatars/{Id}/{AvatarHash}.png?size=128";
}

public sealed record DiscordStatusChanged(DiscordStatus Status, DiscordUser? User);

public enum RobloxAccountState
{
    NotConfigured,
    SignedOut,
    SigningIn,
    SignedIn,
    /// <summary>The refresh token was rejected (90 days passed or access revoked): sign in again (C5).</summary>
    Expired,
}

/// <summary>From the OpenID userinfo endpoint (scopes openid profile).</summary>
public sealed record RobloxProfile(long UserId, string Username, string DisplayName, string? PictureUrl, DateTimeOffset? CreatedAt)
{
    public string ProfileUrl => $"https://www.roblox.com/users/{UserId}/profile";
}

public sealed record RobloxAccountChanged(RobloxAccountState State, RobloxProfile? Profile, bool Offline);
