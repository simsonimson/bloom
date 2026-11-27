using System.Text.RegularExpressions;

namespace Bloom.Activity;

public abstract record LogEvent;
public sealed record JoiningGame(string JobId, long PlaceId, string? ServerAddress) : LogEvent;
public sealed record JoiningUniverse(long UniverseId) : LogEvent;
public sealed record JoinedServer : LogEvent;
/// <summary>Public relay address of the game server (the "Joining game … at" address can be internal).</summary>
public sealed record UdmuxAddress(string Address) : LogEvent;
public sealed record Disconnected : LogEvent;
public sealed record Teleporting : LogEvent;
public sealed record LeavingGame : LogEvent;

/// <summary>
/// Parses Roblox client log lines (EXPERIMENTAL, Blueprint E1). The format is not documented
/// and may change with any Roblox update; unknown lines are ignored. Markers follow the ones
/// Bloxstrap has used for years.
/// </summary>
public static partial class RobloxLogParser
{
    public static LogEvent? Parse(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;

        if (line.Contains("! Joining game", StringComparison.Ordinal))
        {
            var m = JoiningGamePattern().Match(line);
            if (m.Success && long.TryParse(m.Groups[2].Value, out var placeId))
                return new JoiningGame(m.Groups[1].Value, placeId, m.Groups[3].Success ? m.Groups[3].Value : null);
            return null;
        }

        if (line.Contains("[FLog::GameJoinLoadTime] Report game_join_loadtime:", StringComparison.Ordinal))
        {
            var m = UniversePattern().Match(line);
            return m.Success && long.TryParse(m.Groups[1].Value, out var universe) ? new JoiningUniverse(universe) : null;
        }

        if (line.Contains("[FLog::Network] UDMUX Address = ", StringComparison.Ordinal))
        {
            var m = UdmuxPattern().Match(line);
            return m.Success ? new UdmuxAddress(m.Groups[1].Value) : null;
        }

        if (line.Contains("[FLog::Network] serverId:", StringComparison.Ordinal)) return new JoinedServer();
        if (line.Contains("[FLog::Network] Time to disconnect replication data:", StringComparison.Ordinal)) return new Disconnected();
        if (line.Contains("[FLog::SingleSurfaceApp] initiateTeleport", StringComparison.Ordinal)) return new Teleporting();
        if (line.Contains("[FLog::SingleSurfaceApp] leaveUGCGameInternal", StringComparison.Ordinal)) return new LeavingGame();
        return null;
    }

    [GeneratedRegex(@"! Joining game '([0-9a-fA-F\-]{36})' place ([0-9]+)(?: at ([0-9\.]+))?")]
    private static partial Regex JoiningGamePattern();

    [GeneratedRegex(@"UDMUX Address = ([0-9]{1,3}(?:\.[0-9]{1,3}){3}), Port")]
    private static partial Regex UdmuxPattern();

    [GeneratedRegex(@"universeid:([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UniversePattern();
}
