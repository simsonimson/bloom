using System.Text.Json.Nodes;
using Bloom.Connect.Discord;
using Bloom.Core.Roblox;

namespace Bloom.Connect.Tests;

public sealed class DiscordTests
{
    private static readonly PresenceTexts Texts = new("Roblox", "Im Spiel", "Im Menü", "Spiel ansehen", "Beitreten");
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private const string Job = "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0";

    private static ActivityState InGame(string? name = "Adopt Me!", string? icon = "https://tr.rbxcdn.com/icon.png") =>
        new(true, Start, 920587237, Job, name is null ? null : new ExperienceInfo(383310974, 920587237, name, icon, Start));

    [Fact]
    public void Frame_round_trips()
    {
        var bytes = DiscordIpcConnection.Encode(DiscordOpcode.Frame, new JsonObject { ["cmd"] = "X" });
        Assert.Equal(1, BitConverter.ToInt32(bytes, 0));
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));

        var connection = new DiscordIpcConnection(new MemoryStream(bytes));
        var frame = connection.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(DiscordOpcode.Frame, frame.Opcode);
        Assert.Equal("X", frame.Payload["cmd"]!.GetValue<string>());
    }

    [Fact]
    public void Handshake_reads_ready_user()
    {
        var ready = DiscordIpcConnection.Encode(DiscordOpcode.Frame, JsonNode.Parse("""
            { "cmd": "DISPATCH", "evt": "READY", "data": { "v": 1, "user": { "id": "42", "username": "simon", "global_name": "Simon", "avatar": "abc" } } }
            """)!.AsObject());
        var stream = new DuplexStream(ready);
        var connection = new DiscordIpcConnection(stream);
        var user = connection.HandshakeAsync("1234", CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(user);
        Assert.Equal("Simon", user!.DisplayName);
        Assert.Equal("https://cdn.discordapp.com/avatars/42/abc.png?size=128", user.AvatarUrl);
        Assert.Equal(0, BitConverter.ToInt32(stream.Written.ToArray(), 0)); // handshake opcode
        Assert.Contains("\"client_id\":\"1234\"", System.Text.Encoding.UTF8.GetString(stream.Written.ToArray()));
    }

    [Fact]
    public void Handshake_close_throws()
    {
        var close = DiscordIpcConnection.Encode(DiscordOpcode.Close, new JsonObject { ["code"] = 4000, ["message"] = "Invalid Client ID" });
        var connection = new DiscordIpcConnection(new DuplexStream(close));
        var ex = Assert.Throws<DiscordIpcException>(() => connection.HandshakeAsync("x", CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal("Invalid Client ID", ex.Message);
    }

    [Fact]
    public void No_presence_without_roblox() => Assert.Null(PresenceBuilder.Build(ActivityState.Idle, new(true, true), Texts));

    [Fact]
    public void Presence_in_game_shows_experience()
    {
        var a = PresenceBuilder.Build(InGame(), new(true, false), Texts)!;
        Assert.Equal("Adopt Me!", a["details"]!.GetValue<string>());
        Assert.Equal("Im Spiel", a["state"]!.GetValue<string>());
        Assert.Equal(1_700_000_000, a["timestamps"]!["start"]!.GetValue<long>());
        Assert.Equal("https://tr.rbxcdn.com/icon.png", a["assets"]!["large_image"]!.GetValue<string>());
        var buttons = a["buttons"]!.AsArray();
        Assert.Single(buttons);
        Assert.Equal("https://www.roblox.com/games/920587237", buttons[0]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Join_button_only_when_allowed()
    {
        var a = PresenceBuilder.Build(InGame(), new(true, true), Texts)!;
        var buttons = a["buttons"]!.AsArray();
        Assert.Equal(2, buttons.Count);
        Assert.Equal($"https://www.roblox.com/games/start?placeId=920587237&gameInstanceId={Job}", buttons[1]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Hidden_experience_shows_only_roblox()
    {
        var a = PresenceBuilder.Build(InGame(), new(false, true), Texts)!;
        Assert.Equal("Roblox", a["details"]!.GetValue<string>());
        Assert.Null(a["buttons"]);
        Assert.Equal(PresenceBuilder.LogoUrl, a["assets"]!["large_image"]!.GetValue<string>());
    }

    [Fact]
    public void Menu_state()
    {
        var a = PresenceBuilder.Build(new ActivityState(true, Start, null, null, null), new(true, true), Texts)!;
        Assert.Equal("Roblox", a["details"]!.GetValue<string>());
        Assert.Equal("Im Menü", a["state"]!.GetValue<string>());
        Assert.Null(a["buttons"]);
    }

    [Fact]
    public void Long_texts_are_limited()
    {
        var a = PresenceBuilder.Build(InGame(new string('x', 300)), new(true, false), Texts)!;
        Assert.True(a["details"]!.GetValue<string>().Length <= 128);
    }

    /// <summary>Reads from a fixed buffer and records writes.</summary>
    private sealed class DuplexStream(byte[] input) : Stream
    {
        private readonly MemoryStream _input = new(input);
        public MemoryStream Written { get; } = new();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
    }
}
