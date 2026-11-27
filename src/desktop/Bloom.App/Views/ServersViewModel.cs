using System.Collections.ObjectModel;
using System.Globalization;
using Bloom.Activity;
using Bloom.App.Services;
using Bloom.Core.Events;
using Bloom.Core.Performance;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Launcher.Links;
using Bloom.Performance;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Bloom.App.Views;

public sealed class ServerRow(PublicServer server, bool current)
{
    public PublicServer Model { get; } = server;
    public string Players => $"{Model.Playing} / {Model.MaxPlayers}";
    public double Fill => Model.MaxPlayers == 0 ? 0 : 100.0 * Model.Playing / Model.MaxPlayers;
    public string Ping => Model.Ping is { } p ? $"{p} ms" : "–";
    public string Fps => Model.Fps is { } f ? f.ToString("0", CultureInfo.CurrentCulture) : "–";
    public string Id => Model.Id[..8] + "…";
    public bool IsCurrent { get; } = current;
    public bool CanJoin => !IsCurrent;
}

public sealed class ServersViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ActivityTracker _activity;
    private readonly RobloxControl _control;
    private readonly ServerLocator _locator;
    private readonly ServerBrowser _browser;
    private readonly PerformanceMonitor _monitor;
    private readonly ILogger<ServersViewModel> _logger;
    private readonly DispatcherQueue _dispatcher;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly IDisposable _watch;
    private ActivityState _state;
    private ServerLocation? _location;
    private int? _ping;
    private string _status = "";
    private string _placeText = "";
    private string? _cursor;
    private long? _loadedPlace;
    private bool _loading;

    public ServersViewModel(AppSettings settings, ActivityTracker activity, RobloxControl control, ServerLocator locator, ServerBrowser browser,
        PerformanceMonitor monitor, IEventBus events, ILogger<ServersViewModel> logger)
    {
        _settings = settings;
        _activity = activity;
        _control = control;
        _locator = locator;
        _browser = browser;
        _monitor = monitor;
        _logger = logger;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _state = activity.State;
        _placeText = _state.PlaceId?.ToString() ?? "";

        RejoinCommand = new AsyncRelayCommand(() => RejoinAsync(true));
        NewServerCommand = new AsyncRelayCommand(() => RejoinAsync(false));
        CopyLinkCommand = new RelayCommand(CopyLink);
        LoadCommand = new AsyncRelayCommand(() => LoadAsync(more: false));
        MoreCommand = new AsyncRelayCommand(() => LoadAsync(more: true));

        _watch = monitor.Watch();
        _ping = monitor.Latest?.PingMs;
        _subscriptions.Add(events.Subscribe<ActivityStateChanged>(e => _dispatcher.TryEnqueue(() =>
        {
            var serverChanged = e.State.ServerAddress != _state.ServerAddress;
            _state = e.State;
            if (serverChanged) _location = null;
            if (string.IsNullOrEmpty(PlaceText) && e.State.PlaceId is long p) PlaceText = p.ToString();
            OnPropertyChanged(string.Empty);
            _ = LocateAsync();
        })));
        _subscriptions.Add(events.Subscribe<PerformanceSampled>(e => _dispatcher.TryEnqueue(() =>
        {
            _ping = e.Sample?.PingMs;
            OnPropertyChanged(nameof(PingText));
        })));
        _ = LocateAsync();
    }

    public IAsyncRelayCommand RejoinCommand { get; }
    public IAsyncRelayCommand NewServerCommand { get; }
    public IRelayCommand CopyLinkCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand MoreCommand { get; }
    public ObservableCollection<ServerRow> Servers { get; } = new();

    // ------------------------------------------------------------------ current server

    public bool InGame => _state.PlaceId is not null && _state.JobId is not null;
    public bool NotInGame => !InGame;
    public string GameName => _state.Experience?.Name ?? (_state.PlaceId is long p ? $"Place {p}" : "");
    public string ServerId => _state.JobId ?? "–";
    public string Address => _state.ServerAddress ?? Strings.Get("ServerAddressUnknown");
    public string PingText => _ping is { } p ? $"{p} ms" : Strings.Get("PerfPingUnavailable");

    public string LocationText => !_settings.ServerGeolocation
        ? Strings.Get("ServerLocationOff")
        : _location?.Display is { Length: > 0 } text ? text : Strings.Get("ServerLocationUnknown");

    public bool Geolocation
    {
        get => _settings.ServerGeolocation;
        set
        {
            if (value == _settings.ServerGeolocation) return;
            _settings.ServerGeolocation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LocationText));
            _ = LocateAsync();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private async Task LocateAsync()
    {
        if (!_settings.ServerGeolocation || _state.ServerAddress is not { } address) return;
        _location = await _locator.LocateAsync(address);
        OnPropertyChanged(nameof(LocationText));
    }

    private async Task RejoinAsync(bool sameServer)
    {
        Status = Strings.Get("ServerRejoining");
        if (!await _control.RejoinAsync(sameServer)) Status = Strings.Get("ServerNoGame");
        else Status = "";
    }

    private void CopyLink()
    {
        Status = Strings.Get(_control.CopyServerLink() ? "ServerLinkCopied" : "ServerNoGame");
    }

    // ------------------------------------------------------------------ browser

    public string PlaceText { get => _placeText; set => SetProperty(ref _placeText, value); }
    public int SortIndex { get; set; }
    public bool HasMore => _cursor is not null && !_loading;
    public bool Loading { get => _loading; private set { if (SetProperty(ref _loading, value)) OnPropertyChanged(nameof(HasMore)); } }
    private string _browserStatus = "";
    public string BrowserStatus { get => _browserStatus; private set => SetProperty(ref _browserStatus, value); }

    private async Task LoadAsync(bool more)
    {
        if (Loading) return;
        long place;
        if (more && _loadedPlace is long loaded)
        {
            place = loaded;
        }
        else if (LinkParser.TryParse(PlaceText, out var request) && request.PlaceId is long parsed)
        {
            place = parsed;
            Servers.Clear();
            _cursor = null;
        }
        else
        {
            BrowserStatus = Strings.Get("PlayInvalidLink");
            return;
        }

        Loading = true;
        BrowserStatus = Strings.Get("ServerBrowserLoading");
        try
        {
            var page = await _browser.GetAsync(place, SortIndex == 0 ? ServerSort.FewestPlayers : ServerSort.MostPlayers, more ? _cursor : null);
            _loadedPlace = place;
            _cursor = page.NextCursor;
            foreach (var server in page.Servers)
                Servers.Add(new ServerRow(server, string.Equals(server.Id, _state.JobId, StringComparison.OrdinalIgnoreCase)));
            BrowserStatus = Servers.Count == 0 ? Strings.Get("ServerBrowserEmpty") : Strings.Format("ServerBrowserCount", Servers.Count);
        }
        catch (RateLimitedException)
        {
            BrowserStatus = Strings.Get("ServerBrowserRateLimited");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Server list failed");
            BrowserStatus = Strings.Get("ServerBrowserFailed");
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>Set by the page: confirmation when Roblox has to be closed first.</summary>
    public Func<Task<bool>>? ConfirmRestart { get; set; }

    public async Task JoinAsync(ServerRow row)
    {
        if (_loadedPlace is not long place) return;
        if (_activity.State.RobloxRunning && (ConfirmRestart is null || !await ConfirmRestart())) return;
        BrowserStatus = Strings.Get("ServerRejoining");
        await _control.JoinServerAsync(place, row.Model.Id);
        BrowserStatus = "";
    }

    public void Dispose()
    {
        _watch.Dispose();
        foreach (var s in _subscriptions) s.Dispose();
    }
}
