using System.Reflection;
using Bloom.Core.Settings;

namespace Bloom.App.Services;

/// <summary>IDs baked in at build time; a value in the settings overrides them (self-built versions).</summary>
public static class BuildInfo
{
    private static string? Metadata(string key)
    {
        var value = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? BuiltInDiscordAppId { get; } = Metadata("BloomDiscordAppId");
    public static string? BuiltInRobloxClientId { get; } = Metadata("BloomRobloxClientId");

    public static string? DiscordAppId(AppSettings settings) => settings.DiscordAppIdOverride ?? BuiltInDiscordAppId;
    public static string? RobloxClientId(AppSettings settings) => settings.RobloxClientIdOverride ?? BuiltInRobloxClientId;
}
