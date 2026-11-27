using System.Text.Json;
using Bloom.Connect.Roblox;
using Bloom.Core.Connect;

namespace Bloom.Connect.Tests;

public sealed class OAuthTests
{
    [Fact]
    public void Pkce_challenge_is_base64url_sha256_of_verifier()
    {
        var pkce = Pkce.FromVerifier("dBjftJeZ4CVP-mJ92ZB4p8d-T7fVmTlYgNNLu8Tq3sQ");
        Assert.Equal("dEr4Z3y7uXpGWTasAyyyuqhSIzJvvyPaiATKZvjsR34", pkce.Challenge);
    }

    [Fact]
    public void Pkce_values_are_random_and_url_safe()
    {
        var a = Pkce.Create();
        var b = Pkce.Create();
        Assert.NotEqual(a.Verifier, b.Verifier);
        Assert.Equal(43, a.Verifier.Length);
        Assert.DoesNotContain('=', a.Verifier + a.Challenge);
        Assert.DoesNotContain('+', a.Verifier + a.Challenge);
        Assert.DoesNotContain('/', a.Verifier + a.Challenge);
    }

    [Fact]
    public void Authorize_url_has_pkce_and_no_secret()
    {
        var url = RobloxOAuth.AuthorizeUrl("123", Pkce.FromVerifier("dBjftJeZ4CVP-mJ92ZB4p8d-T7fVmTlYgNNLu8Tq3sQ"), "xyz");
        Assert.StartsWith("https://apis.roblox.com/oauth/v1/authorize?client_id=123", url);
        Assert.Contains("redirect_uri=bloom%3A%2F%2Foauth%2Fcallback", url);
        Assert.Contains("scope=openid%20profile", url);
        Assert.Contains("code_challenge=dEr4Z3y7uXpGWTasAyyyuqhSIzJvvyPaiATKZvjsR34&code_challenge_method=S256", url);
        Assert.Contains("state=xyz", url);
        Assert.DoesNotContain("secret", url);
    }

    [Fact]
    public void Callback_ok()
    {
        Assert.Equal(CallbackResult.Ok, RobloxOAuth.ParseCallback("bloom://oauth/callback?code=abc%2B1&state=s1", "s1", out var code));
        Assert.Equal("abc+1", code);
    }

    [Theory]
    [InlineData("bloom://oauth/callback?code=abc&state=other", CallbackResult.StateMismatch)]
    [InlineData("bloom://oauth/callback?code=abc", CallbackResult.StateMismatch)]
    [InlineData("bloom://oauth/callback?error=access_denied&state=s1", CallbackResult.Denied)]
    [InlineData("bloom://oauth/callback?state=s1", CallbackResult.Invalid)]
    [InlineData("bloom://other?code=abc&state=s1", CallbackResult.Invalid)]
    [InlineData("roblox://placeId=1", CallbackResult.Invalid)]
    public void Callback_rejects(string uri, CallbackResult expected) =>
        Assert.Equal(expected, RobloxOAuth.ParseCallback(uri, "s1", out _));

    [Fact]
    public void Redacts_codes_in_logs()
    {
        Assert.DoesNotContain("secretcode", RobloxOAuth.Redact("bloom://oauth/callback?code=secretcode&state=x"));
        Assert.Equal("--tray", RobloxOAuth.Redact("--tray"));
    }

    [Fact]
    public void Parses_userinfo()
    {
        using var doc = JsonDocument.Parse("""
            { "sub": "123456", "name": "Simon", "nickname": "Simon", "preferred_username": "simsonimson",
              "created_at": 1600000000, "profile": "https://www.roblox.com/users/123456/profile",
              "picture": "https://tr.rbxcdn.com/abc/150/150/AvatarHeadshot/Png" }
            """);
        var profile = RobloxOAuth.ParseProfile(doc.RootElement);
        Assert.Equal(new RobloxProfile(123456, "simsonimson", "Simon", "https://tr.rbxcdn.com/abc/150/150/AvatarHeadshot/Png",
            DateTimeOffset.FromUnixTimeSeconds(1600000000)), profile);
        Assert.Equal("https://www.roblox.com/users/123456/profile", profile.ProfileUrl);
    }

    [Fact]
    public void Parses_minimal_userinfo()
    {
        using var doc = JsonDocument.Parse("""{ "sub": "42", "picture": null }""");
        var profile = RobloxOAuth.ParseProfile(doc.RootElement);
        Assert.Equal(42, profile.UserId);
        Assert.Equal("42", profile.Username);
        Assert.Null(profile.PictureUrl);
    }

    [Fact]
    public void Session_vault_round_trips_encrypted()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI
        var path = Path.Combine(Path.GetTempPath(), $"bloom-vault-{Guid.NewGuid():N}.bin");
        try
        {
            var vault = new SessionVault(path);
            Assert.Null(vault.Load());
            var session = new StoredSession("refresh-token-value", new RobloxProfile(1, "u", "U", null, null), DateTimeOffset.UtcNow);
            vault.Save(session);
            Assert.DoesNotContain("refresh-token-value", File.ReadAllText(path));
            Assert.Equal(session.RefreshToken, vault.Load()!.RefreshToken);
            vault.Delete();
            Assert.Null(vault.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
