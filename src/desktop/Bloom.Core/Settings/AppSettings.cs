using System.Text.Json;
using Bloom.Core.Voice;

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
