namespace Bloom.Performance;

public enum CleanupCategory
{
    /// <summary>Roblox client logs (%LOCALAPPDATA%\Roblox\logs), older than a day.</summary>
    RobloxLogs,
    /// <summary>Roblox's temporary download cache (%TEMP%\Roblox). Only while Roblox is closed.</summary>
    RobloxCache,
    /// <summary>Packages bloom's bootstrapper downloaded.</summary>
    BloomDownloads,
    /// <summary>Roblox versions installed by bloom other than the current one.</summary>
    OldVersions,
}

public sealed record CleanupScan(CleanupCategory Category, string Path, long Bytes, int Files, bool Blocked);

public sealed record CleanupRoots(string RobloxLogs, string RobloxCache, string BloomDownloads, string VersionsRoot, string? CurrentVersionDirectory);

/// <summary>
/// Frees disk space in Roblox and bloom folders. Works only inside the fixed roots, never follows
/// links (junctions, symlinks) and skips files that are in use. <paramref name="blocked"/> says
/// when a category must not be touched (Roblox running, a download in progress).
/// </summary>
public sealed class CleanupService(Func<CleanupRoots> roots, Func<CleanupCategory, bool> blocked)
{
    private static readonly TimeSpan LogAge = TimeSpan.FromDays(1);

    public IReadOnlyList<CleanupScan> Scan()
    {
        var r = roots();
        return Enum.GetValues<CleanupCategory>().Select(c =>
        {
            var (path, files) = Targets(c, r);
            long bytes = 0;
            var count = 0;
            foreach (var file in files)
            {
                try { bytes += new FileInfo(file).Length; count++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return new CleanupScan(c, path, bytes, count, blocked(c));
        }).ToList();
    }

    /// <summary>Deletes the category. Returns the bytes freed.</summary>
    public long Clean(CleanupCategory category)
    {
        if (blocked(category)) return 0;
        var r = roots();
        var (path, files) = Targets(category, r);
        long freed = 0;
        foreach (var file in files.ToList())
        {
            try
            {
                var length = new FileInfo(file).Length;
                File.Delete(file);
                freed += length;
            }
            catch (IOException) { /* in use */ }
            catch (UnauthorizedAccessException) { }
        }
        if (category is CleanupCategory.OldVersions or CleanupCategory.RobloxCache or CleanupCategory.BloomDownloads)
            RemoveEmptyDirectories(path, keep: r.CurrentVersionDirectory);
        return freed;
    }

    private static (string Path, IEnumerable<string> Files) Targets(CleanupCategory category, CleanupRoots r) => category switch
    {
        CleanupCategory.RobloxLogs => (r.RobloxLogs, SafeFiles(r.RobloxLogs, recursive: false)
            .Where(f => DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > LogAge)),
        CleanupCategory.RobloxCache => (r.RobloxCache, SafeFiles(r.RobloxCache, recursive: true)),
        CleanupCategory.BloomDownloads => (r.BloomDownloads, SafeFiles(r.BloomDownloads, recursive: true)),
        _ => (r.VersionsRoot, SafeDirectories(r.VersionsRoot)
            .Where(d => r.CurrentVersionDirectory is null || !SamePath(d, r.CurrentVersionDirectory))
            .SelectMany(d => SafeFiles(d, recursive: true))),
    };

    /// <summary>Files below a root without descending into reparse points.</summary>
    internal static IEnumerable<string> SafeFiles(string root, bool recursive)
    {
        if (!Directory.Exists(root) || IsReparse(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = recursive ? Directory.GetDirectories(dir) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var f in files)
            {
                if (!IsReparse(f)) yield return f;
            }
            foreach (var d in dirs)
            {
                if (!IsReparse(d)) pending.Push(d);
            }
        }
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        if (!Directory.Exists(root)) return [];
        try { return Directory.GetDirectories(root).Where(d => !IsReparse(d)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static void RemoveEmptyDirectories(string root, string? keep)
    {
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (IsReparse(dir) || (keep is not null && SamePath(dir, keep))) continue;
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsReparse(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
