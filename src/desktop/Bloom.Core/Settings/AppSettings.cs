using System.Text.Json;
using Bloom.Core.Voice;
using Bloom.Core.Performance;

namespace Bloom.Core.Settings;

/// <summary>Key/value storage for settings. Values are stored as JSON.</summary>
public interface ISettingsStore
{
    bool TryGetRaw(string key, out string json);
    void SetRaw(string key, string json);
}

public enum IntroMode
{
    Full = 0,
    Short = 1,
    Off = 2,
}

public enum AppLanguage
{
    System = 0,
    German = 1,
    English = 2,
}

public sealed record SettingChanged(string Key);

/// <summary>Typed access to all app settings. Unknown or broken values fall back to defaults.</summary>
public sealed class AppSettings
{
    public static class Keys
    {
        public const string IntroMode = "app.introMode";
        public const string Language = "app.language";
        public const string StartWithWindows = "app.startWithWindows";
        public const string CloseToTray = "app.closeToTray";
        public const string OnboardingCompleted = "app.onboardingCompleted";
        public const string UpdateChannel = "app.updateChannel";
        public const string BootstrapperEnabled = "launcher.bootstrapperEnabled";
        public const string BootstrapperFailures = "launcher.bootstrapperFailures";
        public const string ProtocolBackup = "launcher.protocolBackup";
        public const string LogDetectionEnabled = "activity.logDetectionEnabled";
        public const string PttEnabled = "voice.enabled";
        public const string PttMode = "voice.mode";
        public const string PttKey = "voice.key";
        public const string PttMethod = "voice.method";
        public const string PttForegroundOnly = "voice.foregroundOnly";
        public const string PttDeviceId = "voice.deviceId";
        public const string PttOnRobloxExit = "voice.onRobloxExit";
        public const string PttButton = "voice.buttonPosition";
        public const string PttSounds = "voice.sounds";
        public const string PttSystemWideConfirmed = "voice.systemWideConfirmed";

        public const string DiscordEnabled = "discord.enabled";
        public const string DiscordShowExperience = "discord.showExperience";
        public const string DiscordShowJoin = "discord.showJoin";
        public const string DiscordAppId = "discord.appId";
        public const string RobloxClientId = "roblox.oauthClientId";

        public const string PerfModeEnabled = "perf.modeEnabled";
        public const string PerfPriority = "perf.priority";
        public const string PerfPowerPlan = "perf.powerPlan";
        public const string PerfGpuHighPerformance = "perf.gpuHighPerformance";
        public const string PerfRestorePowerScheme = "perf.restorePowerScheme";
        public const string PerfGpuBackup = "perf.gpuPreferenceBackup";
        public const string PerfPreset = "perf.preset";
        public const string PerfFpsCap = "perf.fpsCap";
        public const string PerfOverlay = "perf.overlay";
        public const string PerfOverlayCorner = "perf.overlayCorner";
        public const string ServerGeolocation = "server.geolocation";
        public const string Hotkeys = "hotkeys";

        public static bool IsPerformanceKey(string key) => key.StartsWith("perf.", StringComparison.Ordinal);

        public static bool IsDiscordKey(string key) => key.StartsWith("discord.", StringComparison.Ordinal);

        public static bool IsVoiceKey(string key) => key.StartsWith("voice.", StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ISettingsStore _store;
    private readonly Events.IEventBus? _events;

    public AppSettings(ISettingsStore store, Events.IEventBus? events = null)
    {
        _store = store;
        _events = events;
    }

    public IntroMode IntroMode
    {
        get => Get(Keys.IntroMode, IntroMode.Full);
        set => Set(Keys.IntroMode, value);
    }

    public AppLanguage Language
    {
        get => Get(Keys.Language, AppLanguage.System);
        set => Set(Keys.Language, value);
    }

    public bool StartWithWindows
    {
        get => Get(Keys.StartWithWindows, false);
        set => Set(Keys.StartWithWindows, value);
    }

    public bool CloseToTray
    {
        get => Get(Keys.CloseToTray, true);
        set => Set(Keys.CloseToTray, value);
    }

    public bool OnboardingCompleted
    {
        get => Get(Keys.OnboardingCompleted, false);
        set => Set(Keys.OnboardingCompleted, value);
    }

    public string UpdateChannel
    {
        get => Get(Keys.UpdateChannel, "beta");
        set => Set(Keys.UpdateChannel, value);
    }

    /// <summary>bloom downloads and starts Roblox itself (opt-in, ADR-002).</summary>
    public bool BootstrapperEnabled
    {
        get => Get(Keys.BootstrapperEnabled, false);
        set => Set(Keys.BootstrapperEnabled, value);
    }

    /// <summary>Consecutive bootstrapper failures; at 3 the bootstrapper pauses itself.</summary>
    public int BootstrapperFailures
    {
        get => Get(Keys.BootstrapperFailures, 0);
        set => Set(Keys.BootstrapperFailures, value);
    }

    /// <summary>Protocol handlers that existed before bloom registered itself (scheme → command).</summary>
    public Dictionary<string, string?> ProtocolBackup
    {
        get => Get(Keys.ProtocolBackup, new Dictionary<string, string?>());
        set => Set(Keys.ProtocolBackup, value);
    }

    /// <summary>Experimental experience detection from Roblox logs (Blueprint E1).</summary>
    public bool LogDetectionEnabled
    {
        get => Get(Keys.LogDetectionEnabled, true);
        set => Set(Keys.LogDetectionEnabled, value);
    }

    public bool PttEnabled
    {
        get => Get(Keys.PttEnabled, false);
        set => Set(Keys.PttEnabled, value);
    }

    public PttMode PttMode
    {
        get => Get(Keys.PttMode, PttMode.Hold);
        set => Set(Keys.PttMode, value);
    }

    /// <summary>Virtual-key code of the PTT key or mouse button; 0 = not set.</summary>
    public int PttKey
    {
        get => Get(Keys.PttKey, 0);
        set => Set(Keys.PttKey, value);
    }

    public PttMethod PttMethod
    {
        get => Get(Keys.PttMethod, PttMethod.SystemWide);
        set => Set(Keys.PttMethod, value);
    }

    public bool PttForegroundOnly
    {
        get => Get(Keys.PttForegroundOnly, true);
        set => Set(Keys.PttForegroundOnly, value);
    }

    /// <summary>Preferred microphone; null = the one Roblox uses, otherwise the Windows default.</summary>
    public string? PttDeviceId
    {
        get => Get<string?>(Keys.PttDeviceId, null);
        set => Set(Keys.PttDeviceId, value);
    }

    public RobloxExitBehavior PttOnRobloxExit
    {
        get => Get(Keys.PttOnRobloxExit, RobloxExitBehavior.Restore);
        set => Set(Keys.PttOnRobloxExit, value);
    }

    public ButtonPosition? PttButton
    {
        get => Get<ButtonPosition?>(Keys.PttButton, null);
        set => Set(Keys.PttButton, value);
    }

    public bool PttSounds
    {
        get => Get(Keys.PttSounds, false);
        set => Set(Keys.PttSounds, value);
    }

    /// <summary>The user confirmed that the system-wide method also mutes Discord and other apps.</summary>
    public bool PttSystemWideConfirmed
    {
        get => Get(Keys.PttSystemWideConfirmed, false);
        set => Set(Keys.PttSystemWideConfirmed, value);
    }

    public bool DiscordEnabled
    {
        get => Get(Keys.DiscordEnabled, true);
        set => Set(Keys.DiscordEnabled, value);
    }

    /// <summary>Show the experience name and icon instead of just "Roblox".</summary>
    public bool DiscordShowExperience
    {
        get => Get(Keys.DiscordShowExperience, true);
        set => Set(Keys.DiscordShowExperience, value);
    }

    /// <summary>Adds a "Join" button with the current server (off by default: anyone who sees the status can join).</summary>
    public bool DiscordShowJoin
    {
        get => Get(Keys.DiscordShowJoin, false);
        set => Set(Keys.DiscordShowJoin, value);
    }

    /// <summary>Override for self-built versions; empty = the ID baked into the release build.</summary>
    public string? DiscordAppIdOverride
    {
        get => Get<string?>(Keys.DiscordAppId, null);
        set => Set(Keys.DiscordAppId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Override for self-built versions; empty = the ID baked into the release build.</summary>
    public string? RobloxClientIdOverride
    {
        get => Get<string?>(Keys.RobloxClientId, null);
        set => Set(Keys.RobloxClientId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Raise priority, power plan and GPU preference while Roblox runs (restored afterwards).</summary>
    public bool PerfModeEnabled
    {
        get => Get(Keys.PerfModeEnabled, false);
        set => Set(Keys.PerfModeEnabled, value);
    }

    public ProcessPriority PerfPriority
    {
        get => Get(Keys.PerfPriority, ProcessPriority.AboveNormal);
        set => Set(Keys.PerfPriority, value);
    }

    public bool PerfPowerPlan
    {
        get => Get(Keys.PerfPowerPlan, true);
        set => Set(Keys.PerfPowerPlan, value);
    }

    public bool PerfGpuHighPerformance
    {
        get => Get(Keys.PerfGpuHighPerformance, true);
        set => Set(Keys.PerfGpuHighPerformance, value);
    }

    /// <summary>Power scheme to restore if bloom ended while the performance mode was active.</summary>
    public Guid? PerfRestorePowerScheme
    {
        get => Get<Guid?>(Keys.PerfRestorePowerScheme, null);
        set => Set(Keys.PerfRestorePowerScheme, value);
    }

    /// <summary>Roblox executable path → previous GPU preference value (null = there was none).</summary>
    public Dictionary<string, string?> PerfGpuBackup
    {
        get => Get(Keys.PerfGpuBackup, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        set => Set(Keys.PerfGpuBackup, value);
    }

    public GraphicsPreset PerfPreset
    {
        get => Get(Keys.PerfPreset, GraphicsPreset.Default);
        set => Set(Keys.PerfPreset, value);
    }

    /// <summary>Frame rate limit; 0 = whatever the preset says.</summary>
    public int PerfFpsCap
    {
        get => Get(Keys.PerfFpsCap, 0);
        set => Set(Keys.PerfFpsCap, value);
    }

    public bool PerfOverlay
    {
        get => Get(Keys.PerfOverlay, false);
        set => Set(Keys.PerfOverlay, value);
    }

    public OverlayCorner PerfOverlayCorner
    {
        get => Get(Keys.PerfOverlayCorner, OverlayCorner.TopRight);
        set => Set(Keys.PerfOverlayCorner, value);
    }

    /// <summary>Opt-in: the server IP is sent to ipinfo.io to show its location (E4).</summary>
    public bool ServerGeolocation
    {
        get => Get(Keys.ServerGeolocation, false);
        set => Set(Keys.ServerGeolocation, value);
    }

    public Dictionary<HotkeyAction, HotkeyBinding?> Hotkeys
    {
        get => Get(Keys.Hotkeys, DefaultHotkeys());
        set => Set(Keys.Hotkeys, value);
    }

    public static Dictionary<HotkeyAction, HotkeyBinding?> DefaultHotkeys() => new()
    {
        [HotkeyAction.Rejoin] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x52),               // Ctrl+Alt+R
        [HotkeyAction.CopyServerLink] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4C),       // Ctrl+Alt+L
        [HotkeyAction.ToggleOverlay] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4F),        // Ctrl+Alt+O
        [HotkeyAction.TogglePerformanceMode] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x50), // Ctrl+Alt+P
        [HotkeyAction.TogglePushToTalk] = null,
        [HotkeyAction.ShowBloom] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x42),            // Ctrl+Alt+B
    };

    public PttConfig PttConfig => new(PttEnabled, PttMode, PttKey, PttMethod, PttForegroundOnly, PttDeviceId, PttOnRobloxExit, PttButton, PttSounds);

    public T Get<T>(string key, T fallback)
    {
        if (!_store.TryGetRaw(key, out var json)) return fallback;
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, Json);
            if (value is null) return fallback;
            if (typeof(T).IsEnum && !Enum.IsDefined(typeof(T), value)) return fallback;
            return value;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public void Set<T>(string key, T value)
    {
        _store.SetRaw(key, JsonSerializer.Serialize(value, Json));
        _events?.Publish(new SettingChanged(key));
    }
}
