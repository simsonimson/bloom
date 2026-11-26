using Bloom.Activity;

namespace Bloom.Activity.Tests;

public sealed class RobloxLogParserTests
{
    private const string Prefix = "2026-11-26T18:02:11.123Z,12.345678,1a2b,6 [FLog::Output] ";

    [Fact]
    public void Parses_joining_game()
    {
        var e = RobloxLogParser.Parse(Prefix + "! Joining game '0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0' place 920587237 at 128.116.1.2");
        var join = Assert.IsType<JoiningGame>(e);
        Assert.Equal(920587237, join.PlaceId);
        Assert.Equal("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", join.JobId);
        Assert.Equal("128.116.1.2", join.ServerAddress);
    }

    [Fact]
    public void Parses_joining_game_without_address()
    {
        var join = Assert.IsType<JoiningGame>(RobloxLogParser.Parse(Prefix + "! Joining game '0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0' place 1"));
        Assert.Null(join.ServerAddress);
    }

    [Fact]
    public void Parses_universe()
    {
        var e = RobloxLogParser.Parse("x [FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:1, visitid:0, universeid:383310974, timeToLoad:3.2");
        Assert.Equal(383310974, Assert.IsType<JoiningUniverse>(e).UniverseId);
    }

    [Theory]
    [InlineData("x [FLog::Network] serverId: 128.116.1.2|55555", typeof(JoinedServer))]
    [InlineData("x [FLog::Network] Time to disconnect replication data: 0.01", typeof(Disconnected))]
    [InlineData("x [FLog::SingleSurfaceApp] initiateTeleport", typeof(Teleporting))]
    [InlineData("x [FLog::SingleSurfaceApp] leaveUGCGameInternal", typeof(LeavingGame))]
    public void Parses_state_markers(string line, Type type) => Assert.IsType(type, RobloxLogParser.Parse(line));

    [Theory]
    [InlineData("")]
    [InlineData("random line")]
    [InlineData("! Joining game 'not-a-guid' place 1")]
    public void Ignores_other_lines(string line) => Assert.Null(RobloxLogParser.Parse(line));
}
