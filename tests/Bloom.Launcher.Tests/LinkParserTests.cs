using Bloom.Launcher.Links;

namespace Bloom.Launcher.Tests;

public sealed class LinkParserTests
{
    [Theory]
    [InlineData("920587237")]
    [InlineData("https://www.roblox.com/games/920587237/Adopt-Me")]
    [InlineData("roblox.com/games/920587237")]
    [InlineData("https://www.roblox.com/de/games/920587237/Adopt-Me")]
    [InlineData("https://www.roblox.com/games/start?placeId=920587237")]
    [InlineData("roblox://placeId=920587237")]
    [InlineData("  \"920587237\"  ")]
    public void Parses_place_links(string input)
    {
        Assert.True(LinkParser.TryParse(input, out var request));
        Assert.Equal(920587237, request.PlaceId);
        Assert.Equal("roblox://placeId=920587237", request.ToDeepLink());
    }

    [Fact]
    public void Keeps_private_server_link_code()
    {
        Assert.True(LinkParser.TryParse("https://www.roblox.com/games/123/x?privateServerLinkCode=abc123", out var request));
        Assert.Equal(123, request.PlaceId);
        Assert.Equal("abc123", request.LinkCode);
        Assert.Equal("roblox://placeId=123&linkCode=abc123", request.ToDeepLink());
        Assert.DoesNotContain("abc123", request.ToLogString());
    }

    [Fact]
    public void Passes_player_protocol_through_unchanged()
    {
        const string arg = "roblox-player:1+launchmode:play+gameinfo:SECRET+placelauncherurl:https%3A%2F%2Fexample";
        Assert.True(LinkParser.TryParse(arg, out var request));
        Assert.Equal(arg, request.ToDeepLink());
        Assert.DoesNotContain("SECRET", request.ToLogString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("https://evil.example/games/123")]
    [InlineData("https://roblox.com.evil.example/games/123")]
    [InlineData("-5")]
    public void Rejects_other_input(string input) => Assert.False(LinkParser.TryParse(input, out _));

    [Fact]
    public void Finds_protocol_argument_in_args_and_command_line()
    {
        Assert.Equal("roblox://placeId=1", LinkParser.FindProtocolArgument(["--tray", "roblox://placeId=1"]));
        Assert.Null(LinkParser.FindProtocolArgument(["--tray"]));
        Assert.StartsWith("roblox-player:1+launchmode:play",
            LinkParser.FindProtocolArgument("\"C:\\bloom\\bloom.exe\" \"roblox-player:1+launchmode:play\""));
    }
}
