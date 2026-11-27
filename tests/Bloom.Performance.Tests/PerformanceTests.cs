using System.Net;
using System.Text.Json;
using Bloom.Core.Performance;
using Bloom.Launcher.FastFlags;

namespace Bloom.Performance.Tests;

public sealed class PresetTests
{
    private static FastFlagAllowlist Allow(params string[] names) => new(new HashSet<string>(names), DateTimeOffset.UtcNow, false);

    [Fact]
    public void Default_preset_writes_nothing()
    {
        Assert.Empty(GraphicsPresets.Flags(GraphicsPreset.Default, 0, 144));
    }

    [Fact]
    public void Fps_cap_overrides_preset()
    {
        var flags = GraphicsPresets.Flags(GraphicsPreset.Default, 120, 60);
        var single = Assert.Single(flags);
        Assert.Equal(new FastFlag(GraphicsPresets.FpsFlag, "120"), single);
    }

    [Theory]
    [InlineData(GraphicsPreset.MaxFps, null, "9999")]
    [InlineData(GraphicsPreset.Balanced, 165, "165")]
    [InlineData(GraphicsPreset.Balanced, null, "144")]
    [InlineData(GraphicsPreset.Battery, 240, "30")]
    public void Preset_fps(GraphicsPreset preset, int? refreshRate, string expected)
    {
        var fps = GraphicsPresets.Flags(preset, 0, refreshRate).Single(f => f.Name == GraphicsPresets.FpsFlag);
        Assert.Equal(expected, fps.Value);
    }

    [Fact]
    public void All_preset_flags_are_known_and_valid()
    {
        foreach (var preset in Enum.GetValues<GraphicsPreset>())
        foreach (var flag in GraphicsPresets.Flags(preset, 0, 60))
        {
            Assert.Contains(flag.Name, GraphicsPresets.KnownFlags);
            Assert.True(FastFlagService.IsValidName(flag.Name));
            Assert.Equal(flag.Value, FastFlagService.NormalizeValue(flag.Name, flag.Value));
        }
    }

    [Fact]
    public void Compose_filters_by_allowlist_and_custom_wins()
    {
        var custom = new[] { new FastFlag("FFlagDisablePostFx", "False"), new FastFlag("FFlagMine", "True") };
        var result = GraphicsPresets.Compose(custom, GraphicsPreset.MaxFps, 0, 60, Allow(GraphicsPresets.FpsFlag, "FFlagDisablePostFx", "FFlagMine"));

        Assert.Equal(new[] { GraphicsPresets.FpsFlag, "FFlagDisablePostFx", "FFlagMine" }.Order(StringComparer.Ordinal), result.Select(f => f.Name));
        Assert.Equal("False", result.Single(f => f.Name == "FFlagDisablePostFx").Value);
    }

    [Fact]
    public void Compose_without_allowlist_keeps_all()
    {
        var result = GraphicsPresets.Compose([], GraphicsPreset.Battery, 0, 60, FastFlagAllowlist.Empty);
        Assert.Equal(GraphicsPresets.Flags(GraphicsPreset.Battery, 0, 60).Count, result.Count);
    }

    [Fact]
    public void Coverage_counts_allowed()
    {
        var (allowed, total) = GraphicsPresets.Coverage(GraphicsPreset.Battery, 0, 60, Allow(GraphicsPresets.FpsFlag));
        Assert.Equal(1, allowed);
        Assert.Equal(5, total);
    }
}

public sealed class ServerTests
{
    [Fact]
    public void Parses_server_page()
    {
        var page = ServerBrowser.Parse("""
            { "previousPageCursor": null, "nextPageCursor": "abc",
              "data": [
                { "id": "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", "maxPlayers": 30, "playing": 12, "playerTokens": ["x"], "players": [], "fps": 59.8, "ping": 76 },
                { "id": "not-a-guid", "maxPlayers": 30, "playing": 1 },
                { "id": "1f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", "maxPlayers": 8, "playing": 0 }
              ] }
            """);
        Assert.Equal("abc", page.NextCursor);
        Assert.Equal(2, page.Servers.Count);
        Assert.Equal(12, page.Servers[0].Playing);
        Assert.Equal(76, page.Servers[0].Ping);
        Assert.Null(page.Servers[1].Ping);
    }

    [Fact]
    public void Empty_cursor_means_no_more()
    {
        Assert.Null(ServerBrowser.Parse("""{ "nextPageCursor": "", "data": [] }""").NextCursor);
        Assert.Null(ServerBrowser.Parse("""{ "data": [] }""").NextCursor);
    }

    [Fact]
    public void Parses_location()
    {
        using var doc = JsonDocument.Parse("""{ "ip": "128.116.1.1", "city": "Frankfurt am Main", "region": "Hesse", "country": "DE", "org": "AS22697 Roblox" }""");
        var location = ServerLocator.Parse(doc.RootElement);
        Assert.Equal("Frankfurt am Main, Hesse, DE", location.Display);
        Assert.Equal("AS22697 Roblox", location.Organisation);
    }

    [Theory]
    [InlineData("128.116.50.7", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("224.0.0.1", false)]
    public void Public_address_check(string ip, bool expected) => Assert.Equal(expected, NetworkInfo.IsPublic(IPAddress.Parse(ip)));
}

public sealed class FileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bloom-tests", Guid.NewGuid().ToString("N"));

    public FileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Make(string relative, int bytes, DateTime? written = null)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (written is { } w) File.SetLastWriteTimeUtc(path, w);
        return path;
    }

    private CleanupService Service(Func<CleanupCategory, bool>? blocked = null) => new(
        () => new CleanupRoots(Path.Combine(_dir, "logs"), Path.Combine(_dir, "cache"), Path.Combine(_dir, "downloads"),
            Path.Combine(_dir, "versions"), Path.Combine(_dir, "versions", "version-current")),
        blocked ?? (_ => false));

    [Fact]
    public void Scans_and_cleans_categories()
    {
        Make("logs/old.log", 100, DateTime.UtcNow.AddDays(-3));
        var fresh = Make("logs/new.log", 50);
        Make("cache/a/b.bin", 10);
        Make("downloads/x.zip", 20);
        var current = Make("versions/version-current/RobloxPlayerBeta.exe", 30);
        Make("versions/version-old/RobloxPlayerBeta.exe", 40);
        var service = Service();

        var scan = service.Scan().ToDictionary(s => s.Category);
        Assert.Equal(100, scan[CleanupCategory.RobloxLogs].Bytes);
        Assert.Equal(10, scan[CleanupCategory.RobloxCache].Bytes);
        Assert.Equal(20, scan[CleanupCategory.BloomDownloads].Bytes);
        Assert.Equal(40, scan[CleanupCategory.OldVersions].Bytes);

        Assert.Equal(100, service.Clean(CleanupCategory.RobloxLogs));
        Assert.True(File.Exists(fresh));
        Assert.Equal(40, service.Clean(CleanupCategory.OldVersions));
        Assert.True(File.Exists(current));
        Assert.False(Directory.Exists(Path.Combine(_dir, "versions", "version-old")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "versions", "version-current")));
    }

    [Fact]
    public void Blocked_category_is_not_touched()
    {
        var file = Make("cache/a.bin", 10);
        var service = Service(c => c == CleanupCategory.RobloxCache);
        Assert.True(service.Scan().Single(s => s.Category == CleanupCategory.RobloxCache).Blocked);
        Assert.Equal(0, service.Clean(CleanupCategory.RobloxCache));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Missing_folders_are_empty()
    {
        var scan = Service().Scan();
        Assert.All(scan, s => Assert.Equal(0, s.Files));
    }

    [Fact]
    public void Game_profiles_round_trip()
    {
        var store = new GameProfileStore(_dir);
        var changed = 0;
        store.Changed += () => changed++;
        Assert.Empty(store.All());
        store.Save(new GameProfile(1, "Bee", GraphicsPreset.MaxFps, true, null));
        store.Save(new GameProfile(2, "Ant", null, null, false));
        store.Save(new GameProfile(1, "Bee", GraphicsPreset.Battery, true, null));
        Assert.Equal(new[] { "Ant", "Bee" }, store.All().Select(p => p.Name));
        Assert.Equal(GraphicsPreset.Battery, store.Find(1)!.Preset);
        store.Remove(2);
        Assert.Null(store.Find(2));
        Assert.Equal(4, changed);
    }
}
