using Bloom.Launcher.FastFlags;

namespace Bloom.Launcher.Tests;

public sealed class FastFlagTests
{
    [Theory]
    [InlineData("FFlagDebugGraphicsPreferD3D11", FastFlagKind.Bool)]
    [InlineData("DFFlagSomething", FastFlagKind.Bool)]
    [InlineData("FIntDebugTextureManagerSkipMips", FastFlagKind.Int)]
    [InlineData("DFIntTaskSchedulerTargetFps", FastFlagKind.Int)]
    [InlineData("FStringSomething", FastFlagKind.String)]
    [InlineData("FLogNetwork", FastFlagKind.String)]
    public void Detects_kind(string name, FastFlagKind kind) => Assert.Equal(kind, FastFlagService.KindOf(name));

    [Theory]
    [InlineData("FFlagX", true)]
    [InlineData("DFIntX_2", true)]
    [InlineData("SFFlagX", true)]
    [InlineData("Flag", false)]
    [InlineData("FFlag", false)]
    [InlineData("FFlag X", false)]
    [InlineData("rm -rf", false)]
    public void Validates_names(string name, bool valid) => Assert.Equal(valid, FastFlagService.IsValidName(name));

    [Theory]
    [InlineData("FFlagX", "true", "True")]
    [InlineData("FFlagX", " FALSE ", "False")]
    [InlineData("FFlagX", "1", "True")]
    [InlineData("FFlagX", "yes", null)]
    [InlineData("FIntX", "60", "60")]
    [InlineData("FIntX", "-1", "-1")]
    [InlineData("FIntX", "1.5", null)]
    [InlineData("FStringX", " hello ", "hello")]
    public void Normalizes_values(string name, string value, string? expected) =>
        Assert.Equal(expected, FastFlagService.NormalizeValue(name, value));

    [Fact]
    public void Extracts_flag_names_from_any_shape()
    {
        var names = FastFlagService.ExtractFlagNames("""
            { "applicationSettings": { "FFlagA": "True", "DFIntB": "1" },
              "allowlist": ["FStringC", "not a flag", 5],
              "nested": [{ "name": "FLogD" }] }
            """);
        Assert.Contains("FFlagA", names);
        Assert.Contains("DFIntB", names);
        Assert.Contains("FStringC", names);
        Assert.Contains("FLogD", names);
        Assert.DoesNotContain("not a flag", names);
        Assert.DoesNotContain("applicationSettings", names);
    }

    [Fact]
    public void Broken_json_yields_no_names() => Assert.Empty(FastFlagService.ExtractFlagNames("{ nope"));

    [Fact]
    public void Allowlist_status()
    {
        var list = new FastFlagAllowlist(new HashSet<string> { "FFlagA" }, DateTimeOffset.UtcNow, false);
        Assert.Equal(AllowlistStatus.Allowed, list.StatusOf("FFlagA"));
        Assert.Equal(AllowlistStatus.NotAllowed, list.StatusOf("FFlagB"));
        Assert.Equal(AllowlistStatus.Unknown, FastFlagAllowlist.Empty.StatusOf("FFlagA"));
    }

    [Fact]
    public void Saves_loads_and_applies()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bloom-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var http = new HttpClient();
            var service = new FastFlagService(http, dir);
            service.Save([new FastFlag("FIntB", "2"), new FastFlag("FFlagA", "True")]);
            var loaded = service.Load();
            Assert.Equal(["FFlagA", "FIntB"], loaded.Select(f => f.Name));

            var version = Path.Combine(dir, "version");
            Directory.CreateDirectory(version);
            service.Apply(version);
            var json = File.ReadAllText(Path.Combine(version, "ClientSettings", "ClientAppSettings.json"));
            Assert.Contains("\"FFlagA\"", json);
            Assert.Contains("\"FIntB\"", json);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
