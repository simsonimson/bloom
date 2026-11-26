using System.Collections.ObjectModel;
using Bloom.App.Services;
using Bloom.Core.Events;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Core.Voice;
using Bloom.Voice;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Bloom.App.Views;

public sealed class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly Bloom.Activity.ActivityTracker _activity;
    private readonly IFavoritesStore _favorites;
    private readonly ISessionStore _sessions;
    private readonly IDisposable _subscription;
    private readonly IDisposable _pttSubscription;
    private readonly AppSettings _settings;
    private PttState _ptt;
    private readonly DispatcherQueueTimer _timer;
    private ActivityState _state;

    public OverviewViewModel(Bloom.Activity.ActivityTracker activity, IFavoritesStore favorites, ISessionStore sessions, IEventBus events,
        AppSettings settings, VoiceService voice)
    {
        _settings = settings;
        _ptt = voice.Snapshot.State;
        _activity = activity;
        _favorites = favorites;
        _sessions = sessions;
        _state = activity.State;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _subscription = events.Subscribe<ActivityStateChanged>(e => dispatcher.TryEnqueue(() =>
        {
            _state = e.State;
            OnPropertyChanged(string.Empty);
            if (!e.State.RobloxRunning) Reload();
        }));
        _pttSubscription = events.Subscribe<PttStateChanged>(e => dispatcher.TryEnqueue(() =>
        {
            _ptt = e.State;
            OnPropertyChanged(nameof(PttStatus));
            OnPropertyChanged(nameof(PttLive));
        }));
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => OnPropertyChanged(nameof(RobloxStatus));
        _timer.Start();
        Reload();
    }

    public ObservableCollection<FavoriteItem> QuickLaunch { get; } = new();
    public ObservableCollection<RecentItem> Recent { get; } = new();

    public bool RobloxRunning => _state.RobloxRunning;

    public string RobloxStatus
    {
        get
        {
            if (!_state.RobloxRunning) return Strings.Get("StatusNotRunning");
            var elapsed = _state.SessionStartedAt is { } start ? DateTimeOffset.Now - start : TimeSpan.Zero;
            var time = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            var where = _state.Experience?.Name ?? (_state.PlaceId is long p ? $"Place {p}" : Strings.Get("StatusInApp"));
            return $"{where} · {time}";
        }
    }

    public string PttStatus => !_settings.PttEnabled
        ? Strings.Get("PttStateDisabled")
        : _ptt == PttState.Off ? Strings.Get("PttStateWaiting") : Strings.Get(VoiceText.StateKey(_ptt));

    public bool PttLive => _ptt is PttState.Transmitting or PttState.Open;

    public bool HasQuickLaunch => QuickLaunch.Count > 0;
    public bool HasNoQuickLaunch => QuickLaunch.Count == 0;
    public bool HasRecent => Recent.Count > 0;
    public bool HasNoRecent => Recent.Count == 0;

    private void Reload()
    {
        QuickLaunch.Clear();
        foreach (var f in _favorites.All().Take(4)) QuickLaunch.Add(new FavoriteItem(f));
        Recent.Clear();
        foreach (var s in _sessions.Recent(5)) Recent.Add(new RecentItem(s));
        OnPropertyChanged(nameof(HasQuickLaunch));
        OnPropertyChanged(nameof(HasNoQuickLaunch));
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(HasNoRecent));
    }

    public void Dispose()
    {
        _timer.Stop();
        _subscription.Dispose();
        _pttSubscription.Dispose();
    }
}
