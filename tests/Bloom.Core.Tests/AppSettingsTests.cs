using Bloom.Core.Events;
using Bloom.Core.Settings;

namespace Bloom.Core.Tests;

public class AppSettingsTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        public readonly Dictionary<string, string> Values = new();
        public bool TryGetRaw(string key, out string json) => Values.TryGetValue(key, out json!);
        public void SetRaw(string key, string json) => Values[key] = json;
    }

    [Fact]
    public void Defaults_when_nothing_is_stored()
    {
        var settings = new AppSettings(new MemoryStore());
        Assert.Equal(IntroMode.Full, settings.IntroMode);
        Assert.Equal(AppLanguage.System, settings.Language);
        Assert.True(settings.CloseToTray);
        Assert.False(settings.StartWithWindows);
        Assert.Equal("beta", settings.UpdateChannel);
    }

    [Fact]
    public void Round_trips_values()
    {
        var store = new MemoryStore();
        var settings = new AppSettings(store) { IntroMode = IntroMode.Short, CloseToTray = false, Language = AppLanguage.English };
        var reloaded = new AppSettings(store);
        Assert.Equal(IntroMode.Short, reloaded.IntroMode);
        Assert.False(reloaded.CloseToTray);
        Assert.Equal(AppLanguage.English, reloaded.Language);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("42")]
    [InlineData("null")]
    public void Broken_or_unknown_values_fall_back_to_default(string raw)
    {
        var store = new MemoryStore();
        store.Values[AppSettings.Keys.IntroMode] = raw;
        Assert.Equal(IntroMode.Full, new AppSettings(store).IntroMode);
    }

    [Fact]
    public void Publishes_setting_changed()
    {
        var bus = new EventBus();
        var changed = new List<string>();
        bus.Subscribe<SettingChanged>(e => changed.Add(e.Key));
        new AppSettings(new MemoryStore(), bus).IntroMode = IntroMode.Off;
        Assert.Equal([AppSettings.Keys.IntroMode], changed);
    }
}
