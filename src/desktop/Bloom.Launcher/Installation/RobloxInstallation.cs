using Microsoft.Win32;

namespace Bloom.Launcher.Installation;

/// <summary>The official Roblox installation (used as fallback and for FastFlags when the bootstrapper is off).</summary>
public sealed record OfficialInstallation(string? HandlerCommand, string? LauncherPath, string? VersionDirectory)
{
    public string? PlayerExe => VersionDirectory is null ? null : Path.Combine(VersionDirectory, RobloxInstallation.PlayerExeName);
    public bool IsPresent => LauncherPath is not null || VersionDirectory is not null;
}

public static class RobloxInstallation
{
    public const string PlayerExeName = "RobloxPlayerBeta.exe";
    public const string PlayerProcessName = "RobloxPlayerBeta";

    public static string OfficialRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

    public static string OfficialLogs => Path.Combine(OfficialRoot, "logs");

    /// <summary>
    /// Detects the official installation from the roblox-player protocol handler and the
    /// version folders under %LOCALAPPDATA%\Roblox\Versions (inofficial but stable layout).
    /// </summary>
    /// <param name="bloomExecutable">Handlers pointing to bloom are ignored (bloom owns the protocol).</param>
    /// <param name="backedUpHandler">The official handler saved before bloom registered itself.</param>
    public static OfficialInstallation FindOfficial(string? bloomExecutable = null, string? backedUpHandler = null)
    {
        var handler = ReadHandler("roblox-player");
        if (handler is not null && bloomExecutable is not null
            && handler.Contains(Path.GetFileName(bloomExecutable), StringComparison.OrdinalIgnoreCase))
        {
            handler = backedUpHandler;
        }

        var launcher = ExecutableFromCommand(handler);
        if (launcher is not null && !File.Exists(launcher)) launcher = null;

        var versionDir = LatestVersionDirectory(Path.Combine(OfficialRoot, "Versions"));
        return new OfficialInstallation(handler, launcher, versionDir);
    }

    public static string? LatestVersionDirectory(string versionsRoot)
    {
        if (!Directory.Exists(versionsRoot)) return null;
        return new DirectoryInfo(versionsRoot)
            .EnumerateDirectories("version-*")
            .Where(d => File.Exists(Path.Combine(d.FullName, PlayerExeName)))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d.FullName, PlayerExeName)))
            .Select(d => d.FullName)
            .FirstOrDefault();
    }

    public static string? ReadHandler(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}\shell\open\command")
                        ?? Registry.ClassesRoot.OpenSubKey($@"{scheme}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    /// <summary>Extracts the executable from a command like "C:\x\y.exe" %1.</summary>
    public static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }
        var exe = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? trimmed[..(exe + 4)] : trimmed.Split(' ')[0];
    }

    /// <summary>Builds the process arguments for a handler command, replacing %1 with the protocol argument.</summary>
    public static string ArgumentsFromCommand(string command, string argument)
    {
        var exe = ExecutableFromCommand(command) ?? "";
        var rest = command.Trim();
        rest = rest.StartsWith('"') ? rest[(exe.Length + 2)..] : rest[Math.Min(rest.Length, exe.Length)..];
        rest = rest.Trim();
        return rest.Contains("%1") ? rest.Replace("%1", argument) : $"{rest} \"{argument}\"".Trim();
    }
}
