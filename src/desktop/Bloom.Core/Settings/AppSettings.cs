using System.Text.Json;

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
