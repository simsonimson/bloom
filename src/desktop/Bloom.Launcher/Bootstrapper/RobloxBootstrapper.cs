using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Bloom.Launcher.Links;

namespace Bloom.Launcher.Bootstrapper;

public enum BootstrapStage { Checking, Downloading, Extracting, Launching }

public sealed record BootstrapProgress(BootstrapStage Stage, double Fraction);

public sealed record ClientVersion(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("clientVersionUpload")] string ClientVersionUpload);

public sealed record InstalledClient(string VersionGuid, string Directory)
{
    public string PlayerExe => Path.Combine(Directory, Installation.RobloxInstallation.PlayerExeName);
}

/// <summary>
/// Downloads and starts the unmodified official Roblox client (ADR-002).
/// Version check: clientsettings v2/client-version/WindowsPlayer (legacy, documented).
/// Packages: setup.rbxcdn.com/{version}-rbxPkgManifest.txt and setup.rbxcdn.com/{version}-{package} (inofficial).
/// Every file is verified against the MD5 from the manifest; nothing is patched.
/// </summary>
public sealed class RobloxBootstrapper
{
    private static readonly string[] VersionEndpoints =
    [
        "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer",
        "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer",
    ];
    private const string SetupBase = "https://setup.rbxcdn.com/";
    private const string CompleteMarker = ".bloom-complete";
    private const int ParallelDownloads = 4;

    private readonly HttpClient _http;
    private readonly string _versionsRoot;
    private readonly string _downloadsRoot;
    private readonly Action<string, Exception?>? _log;

    public RobloxBootstrapper(HttpClient http, string root, Action<string, Exception?>? log = null)
    {
        _http = http;
        _versionsRoot = Path.Combine(root, "Versions");
        _downloadsRoot = Path.Combine(root, "Downloads");
        _log = log;
    }

    public string VersionsRoot => _versionsRoot;

    /// <summary>The newest completely installed client, if any (offline use).</summary>
    public InstalledClient? GetInstalled()
    {
        if (!Directory.Exists(_versionsRoot)) return null;
        return new DirectoryInfo(_versionsRoot)
            .EnumerateDirectories("version-*")
            .Where(d => File.Exists(Path.Combine(d.FullName, CompleteMarker)))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .Select(d => new InstalledClient(d.Name, d.FullName))
            .FirstOrDefault();
    }

    public async Task<ClientVersion> GetLatestVersionAsync(CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var endpoint in VersionEndpoints)
        {
            try
            {
                var version = await _http.GetFromJsonAsync<ClientVersion>(endpoint, ct);
                if (version is not null && version.ClientVersionUpload.StartsWith("version-", StringComparison.Ordinal))
                    return version;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                last = ex;
                _log?.Invoke($"Version check via {endpoint} failed", ex);
            }
        }
        throw new BootstrapperException(BootstrapperFailure.VersionCheckFailed, "Could not determine the current Roblox version", last);
    }

    /// <summary>Makes sure the current client is installed and returns it.</summary>
    public async Task<InstalledClient> EnsureCurrentAsync(IProgress<BootstrapProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new BootstrapProgress(BootstrapStage.Checking, 0));
        var version = await GetLatestVersionAsync(ct);
        var guid = version.ClientVersionUpload;
        var target = Path.Combine(_versionsRoot, guid);
        if (File.Exists(Path.Combine(target, CompleteMarker)))
            return new InstalledClient(guid, target);

        _log?.Invoke($"Installing Roblox {version.Version} ({guid})", null);
        var manifestText = await DownloadStringAsync($"{SetupBase}{guid}-rbxPkgManifest.txt", ct);
        var packages = PackageManifest.Parse(manifestText).Where(p => !PackageMap.IsIgnored(p.Name)).ToList();

        foreach (var package in packages)
        {
            if (!PackageMap.TryGetTarget(package.Name, out _))
                throw new BootstrapperException(BootstrapperFailure.UnknownPackage, $"Unknown package '{package.Name}' in manifest");
        }

        Directory.CreateDirectory(_downloadsRoot);
        await DownloadAllAsync(guid, packages, progress, ct);

        progress?.Report(new BootstrapProgress(BootstrapStage.Extracting, 0));
        var staging = target + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true); // incomplete earlier attempt
        Directory.CreateDirectory(staging);

        for (var i = 0; i < packages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            PackageMap.TryGetTarget(packages[i].Name, out var relative);
            Extract(Path.Combine(_downloadsRoot, packages[i].Md5), staging, relative);
            progress?.Report(new BootstrapProgress(BootstrapStage.Extracting, (i + 1) / (double)packages.Count));
        }

        File.WriteAllText(Path.Combine(staging, "AppSettings.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n\t<ContentFolder>content</ContentFolder>\r\n\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n</Settings>\r\n");

        if (!File.Exists(Path.Combine(staging, Installation.RobloxInstallation.PlayerExeName)))
            throw new BootstrapperException(BootstrapperFailure.ExtractFailed, "RobloxPlayerBeta.exe missing after extraction");

        File.WriteAllText(Path.Combine(staging, CompleteMarker), DateTimeOffset.UtcNow.ToString("O"));
        Directory.Move(staging, target);
        _log?.Invoke($"Roblox {version.Version} installed", null);
        return new InstalledClient(guid, target);
    }

    /// <summary>Starts the client. The argument is passed through unchanged (protocol string or deep link).</summary>
    public Process Launch(InstalledClient client, LaunchRequest request)
    {
        var info = new ProcessStartInfo(client.PlayerExe)
        {
            WorkingDirectory = client.Directory,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(request.OpensAppOnly ? "--app" : request.ToDeepLink());
        try
        {
            return Process.Start(info) ?? throw new BootstrapperException(BootstrapperFailure.LaunchFailed, "Process.Start returned null");
        }
        catch (Exception ex) when (ex is not BootstrapperException)
        {
            throw new BootstrapperException(BootstrapperFailure.LaunchFailed, "Roblox could not be started", ex);
        }
    }

    /// <summary>Removes all versions except the given one, plus cached downloads.</summary>
    public void CleanupExcept(InstalledClient keep)
    {
        try
        {
            if (Directory.Exists(_versionsRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(_versionsRoot))
                {
                    if (string.Equals(dir, keep.Directory, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(dir, recursive: true); }
                    catch (IOException) { /* still in use by a running client */ }
                    catch (UnauthorizedAccessException) { }
                }
            }
            if (Directory.Exists(_downloadsRoot)) Directory.Delete(_downloadsRoot, recursive: true);
        }
        catch (Exception ex)
        {
            _log?.Invoke("Cleanup failed", ex);
        }
    }

    private async Task DownloadAllAsync(string guid, IReadOnlyList<PackageEntry> packages, IProgress<BootstrapProgress>? progress, CancellationToken ct)
    {
        var total = Math.Max(1, packages.Sum(p => p.CompressedSize));
        long done = 0;
        using var gate = new SemaphoreSlim(ParallelDownloads);
        var tasks = packages.Select(async package =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await DownloadPackageAsync(guid, package, ct);
                var now = Interlocked.Add(ref done, package.CompressedSize);
                progress?.Report(new BootstrapProgress(BootstrapStage.Downloading, now / (double)total));
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private async Task DownloadPackageAsync(string guid, PackageEntry package, CancellationToken ct)
    {
        var final = Path.Combine(_downloadsRoot, package.Md5);
        if (File.Exists(final) && await Md5OfFileAsync(final, ct) == package.Md5) return;

        var part = final + ".part";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using (var response = await _http.GetAsync($"{SetupBase}{guid}-{package.Name}", HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var file = File.Create(part);
                    await source.CopyToAsync(file, ct);
                }
                if (await Md5OfFileAsync(part, ct) != package.Md5)
                    throw new BootstrapperException(BootstrapperFailure.ChecksumMismatch, $"Checksum mismatch for {package.Name}");
                File.Move(part, final, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is HttpRequestException or IOException or BootstrapperException { Failure: BootstrapperFailure.ChecksumMismatch })
            {
                _log?.Invoke($"Download of {package.Name} failed (attempt {attempt})", ex);
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
            catch (HttpRequestException ex)
            {
                throw new BootstrapperException(BootstrapperFailure.DownloadFailed, $"Download of {package.Name} failed", ex);
            }
        }
    }

    internal static void Extract(string zipPath, string root, string relativeDirectory)
    {
        var rootFull = Path.GetFullPath(root);
        var baseDir = Path.GetFullPath(Path.Combine(rootFull, relativeDirectory));
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                if (name.Length == 0 || name.EndsWith(Path.DirectorySeparatorChar)) continue;
                var destination = Path.GetFullPath(Path.Combine(baseDir, name));
                if (!destination.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new BootstrapperException(BootstrapperFailure.ExtractFailed, $"Entry '{entry.FullName}' escapes the version folder");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new BootstrapperException(BootstrapperFailure.ExtractFailed, $"Extracting {Path.GetFileName(zipPath)} failed", ex);
        }
    }

    private async Task<string> DownloadStringAsync(string url, CancellationToken ct)
    {
        try
        {
            return await _http.GetStringAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BootstrapperException(BootstrapperFailure.DownloadFailed, $"Download of {url} failed", ex);
        }
    }

    private static async Task<string> Md5OfFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }
}
