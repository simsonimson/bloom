namespace Bloom.Launcher.Bootstrapper;

public sealed record PackageEntry(string Name, string Md5, long CompressedSize, long Size);

/// <summary>
/// rbxPkgManifest.txt: first line is the schema version ("v0"), then four lines per file:
/// name, MD5, compressed size, decompressed size.
/// </summary>
public static class PackageManifest
{
    public static IReadOnlyList<PackageEntry> Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines[0] != "v0")
            throw new BootstrapperException(BootstrapperFailure.UnexpectedManifest, $"Unknown manifest schema '{(lines.Length > 0 ? lines[0] : "")}'");
        if ((lines.Length - 1) % 4 != 0)
            throw new BootstrapperException(BootstrapperFailure.UnexpectedManifest, "Manifest has an incomplete entry");

        var entries = new List<PackageEntry>();
        for (var i = 1; i < lines.Length; i += 4)
        {
            if (!long.TryParse(lines[i + 2], out var compressed) || !long.TryParse(lines[i + 3], out var size)
                || lines[i + 1].Length != 32)
                throw new BootstrapperException(BootstrapperFailure.UnexpectedManifest, $"Invalid manifest entry '{lines[i]}'");
            entries.Add(new PackageEntry(lines[i], lines[i + 1].ToLowerInvariant(), compressed, size));
        }
        return entries;
    }
}

/// <summary>
/// Where each package is extracted inside the version folder. The layout follows the
/// official client (as used by Bloxstrap, MIT). Unknown packages abort the install so
/// bloom falls back to the official installation instead of producing a broken client.
/// </summary>
public static class PackageMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = "",
        ["redist.zip"] = "",
        ["shaders.zip"] = @"shaders\",
        ["ssl.zip"] = @"ssl\",
        ["WebView2.zip"] = "",
        ["WebView2RuntimeInstaller.zip"] = @"WebView2RuntimeInstaller\",
        ["content-avatar.zip"] = @"content\avatar\",
        ["content-configs.zip"] = @"content\configs\",
        ["content-fonts.zip"] = @"content\fonts\",
        ["content-sky.zip"] = @"content\sky\",
        ["content-sounds.zip"] = @"content\sounds\",
        ["content-textures2.zip"] = @"content\textures\",
        ["content-models.zip"] = @"content\models\",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts\",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries\",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain\",
        ["content-textures3.zip"] = @"PlatformContent\pc\textures\",
        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages\",
        ["extracontent-translations.zip"] = @"ExtraContent\translations\",
        ["extracontent-models.zip"] = @"ExtraContent\models\",
        ["extracontent-textures.zip"] = @"ExtraContent\textures\",
        ["extracontent-places.zip"] = @"ExtraContent\places\",
    };

    /// <summary>Entries that are not part of the player folder (the official launcher itself).</summary>
    public static bool IsIgnored(string name) =>
        !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || name.Equals("RobloxPlayerLauncher.zip", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetTarget(string packageName, out string relativeDirectory) =>
        Map.TryGetValue(packageName, out relativeDirectory!);
}

public enum BootstrapperFailure
{
    VersionCheckFailed,
    UnexpectedManifest,
    UnknownPackage,
    DownloadFailed,
    ChecksumMismatch,
    ExtractFailed,
    LaunchFailed,
}

public sealed class BootstrapperException(BootstrapperFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public BootstrapperFailure Failure { get; } = failure;
}
