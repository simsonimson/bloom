using Bloom.Core.Events;
using Bloom.Core.Performance;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Performance;

namespace Bloom.App.Services;

/// <summary>Applies a game profile's live settings (performance mode, push-to-talk) while that game runs.</summary>
public sealed class ProfileCoordinator : IDisposable
{
    private readonly GameProfileStore _profiles;
    private readonly PerformanceModeService _performance;
    private readonly VoiceCoordinator _voice;
    private readonly AppSettings _settings;
    private readonly IDisposable _subscription;
    private long? _universe;

    public ProfileCoordinator(GameProfileStore profiles, PerformanceModeService performance, VoiceCoordinator voice, AppSettings settings, IEventBus events)
    {
        _profiles = profiles;
        _performance = performance;
        _voice = voice;
        _settings = settings;
        _subscription = events.Subscribe<ActivityStateChanged>(e => Apply(e.State));
        _profiles.Changed += () => Apply(_universe);
    }

    public GameProfile? Active { get; private set; }

    private void Apply(ActivityState state) => Apply(state.Experience?.UniverseId ?? state.UniverseId);

    private void Apply(long? universe)
    {
        _universe = universe;
        Active = universe is long u ? _profiles.Find(u) : null;
        _performance.Override = Active?.PerformanceMode;
        // Switching push-to-talk on needs the setup from the Voice page (key, consent for system-wide).
        _voice.PttOverride = Active?.PushToTalk is true && !VoiceText.ReadyToEnable(_settings) ? null : Active?.PushToTalk;
    }

    public void Dispose() => _subscription.Dispose();
}
