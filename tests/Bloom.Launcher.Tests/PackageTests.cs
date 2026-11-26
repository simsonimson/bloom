using System.IO.Compression;
using Bloom.Launcher.Bootstrapper;

namespace Bloom.Launcher.Tests;

public sealed class PackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bloom-tests", Guid.NewGuid().ToString("N"));

    public PackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Parses_manifest()
    {
        var entries = PackageManifest.Parse("v0\r\nRobloxApp.zip\r\n0123456789ABCDEF0123456789ABCDEF\r\n100\r\n200\r\nshaders.zip\r\nfedcba9876543210fedcba9876543210\r\n10\r\n20\r\n");
        Assert.Equal(2, entries.Count);
        Assert.Equal(new PackageEntry("RobloxApp.zip", "0123456789abcdef0123456789abcdef", 100, 200), entries[0]);
        Assert.Equal("shaders.zip", entries[1].Name);
    }

    [Theory]
    [InlineData("v1\nRobloxApp.zip\n0123456789abcdef0123456789abcdef\n1\n2\n")]
    [InlineData("v0\nRobloxApp.zip\nabc\n1\n2\n")]
    [InlineData("v0\nRobloxApp.zip\nabc\n1\n")]
    [InlineData("v0\nRobloxApp.zip\n0123456789abcdef0123456789abcdef\nx\n2\n")]
    [InlineData("")]
    public void Rejects_broken_manifest(string text) => Assert.Throws<BootstrapperException>(() => PackageManifest.Parse(text));

    [Fact]
    public void Maps_known_packages_and_ignores_launcher()
    {
        Assert.True(PackageMap.TryGetTarget("RobloxApp.zip", out var app));
        Assert.Equal("", app);
        Assert.True(PackageMap.TryGetTarget("content-textures2.zip", out var tex));
        Assert.Equal(@"content\textures\", tex);
        Assert.True(PackageMap.IsIgnored("RobloxPlayerLauncher.zip"));
        Assert.True(PackageMap.IsIgnored("RobloxPlayerBeta.exe"));
        Assert.False(PackageMap.IsIgnored("RobloxApp.zip"));
        Assert.False(PackageMap.TryGetTarget("something-new.zip", out _));
    }

    private string Zip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public void Extracts_into_version_folder()
    {
        var root = Path.Combine(_dir, "version");
        RobloxBootstrapper.Extract(Zip(("RobloxPlayerBeta.exe", "x"), ("sub/file.txt", "y")), root, "");
        Assert.True(File.Exists(Path.Combine(root, "RobloxPlayerBeta.exe")));
        Assert.Equal("y", File.ReadAllText(Path.Combine(root, "sub", "file.txt")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    public void Refuses_zip_slip(string entry)
    {
        var root = Path.Combine(_dir, "version");
        var ex = Assert.Throws<BootstrapperException>(() => RobloxBootstrapper.Extract(Zip((entry, "x")), root, ""));
        Assert.Equal(BootstrapperFailure.ExtractFailed, ex.Failure);
        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
    }
}
