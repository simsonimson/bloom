using Bloom.App.Services;
using Bloom.Connect.Discord;
using Bloom.Connect.Roblox;
using Bloom.Core.Connect;
using Bloom.Core.Events;
using Bloom.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bloom.App.Views;

public sealed class ConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly DiscordPresenceService _discord;
    private readonly RobloxAccountService _account;
    private readonly List<IDisposable> _subscriptions = new();
    private string _discordAppId;
    private string _robloxClientId;

    public ConnectionsViewModel(AppSettings settings, DiscordPresenceService discord, RobloxAccountService account, IEventBus events)
    {
        _settings = settings;
        _discord = discord;
        _account = account;
        _discordAppId = settings.DiscordAppIdOverride ?? "";
        _robloxClientId = settings.RobloxClientIdOverride ?? "";

        SignInCommand = new RelayCommand(() => _account.BeginSignIn());
        CancelSignInCommand = new RelayCommand(_account.CancelSignIn);
        SignOutCommand = new AsyncRelayCommand(() => _account.SignOutAsync());
        OpenProfileCommand = new RelayCommand(() => { if (_account.Profile is { } p) AppServices.OpenBrowser(p.ProfileUrl); });
        SaveIdsCommand = new RelayCommand(SaveIds);

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _subscriptions.Add(events.Subscribe<DiscordStatusChanged>(_ => dispatcher.TryEnqueue(() => OnPropertyChanged(string.Empty))));
        _subscriptions.Add(events.Subscribe<RobloxAccountChanged>(_ => dispatcher.TryEnqueue(() =>
        {
            _robloxAvatar = null;
            OnPropertyChanged(string.Empty);
        })));
    }

    public IRelayCommand SignInCommand { get; }
    public IRelayCommand CancelSignInCommand { get; }
    public IAsyncRelayCommand SignOutCommand { get; }
    public IRelayCommand OpenProfileCommand { get; }
    public IRelayCommand SaveIdsCommand { get; }

    // ------------------------------------------------------------------ Discord

    public bool DiscordEnabled
    {
        get => _settings.DiscordEnabled;
        set { if (value != _settings.DiscordEnabled) { _settings.DiscordEnabled = value; OnPropertyChanged(); } }
    }

    public bool DiscordShowExperience
    {
        get => _settings.DiscordShowExperience;
        set { if (value != _settings.DiscordShowExperience) { _settings.DiscordShowExperience = value; OnPropertyChanged(); } }
    }

    public bool DiscordShowJoin
    {
        get => _settings.DiscordShowJoin;
        set { if (value != _settings.DiscordShowJoin) { _settings.DiscordShowJoin = value; OnPropertyChanged(); } }
    }

    public bool DiscordConnected => _discord.Status == DiscordStatus.Connected;

    public string DiscordStatusText => _discord.Status switch
    {
        DiscordStatus.Connected => Strings.Format("DiscordConnectedAs", _discord.User?.DisplayName ?? "Discord"),
        DiscordStatus.Disabled => Strings.Get("DiscordDisabled"),
        DiscordStatus.NotConfigured => Strings.Get("DiscordNotConfigured"),
        _ => Strings.Get("DiscordNotRunning"),
    };

    public string DiscordHandle => _discord.User is { } u ? "@" + u.Username : "";

    public BitmapImage? DiscordAvatar => _discord.User?.AvatarUrl is { } url ? new BitmapImage(new Uri(url)) : null;

    // ------------------------------------------------------------------ Roblox

    private BitmapImage? _robloxAvatar;

    public RobloxAccountState RobloxState => _account.State;
    public bool RobloxSignedIn => _account.State == RobloxAccountState.SignedIn;
    public bool RobloxCanSignIn => _account.State is RobloxAccountState.SignedOut or RobloxAccountState.Expired;
    public bool RobloxSigningIn => _account.State == RobloxAccountState.SigningIn;
    public bool RobloxExpired => _account.State == RobloxAccountState.Expired;
    public bool RobloxNotConfigured => _account.State == RobloxAccountState.NotConfigured;
    public bool RobloxOffline => _account.Offline && RobloxSignedIn;
    public string RobloxDisplayName => _account.Profile?.DisplayName ?? "";
    public string RobloxUsername => _account.Profile is { } p ? $"@{p.Username} · ID {p.UserId}" : "";

    public BitmapImage? RobloxAvatar => _robloxAvatar ??=
        _account.Profile?.PictureUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;

    // ------------------------------------------------------------------ IDs for self-built versions

    public string DiscordAppId { get => _discordAppId; set => SetProperty(ref _discordAppId, value); }
    public string RobloxClientId { get => _robloxClientId; set => SetProperty(ref _robloxClientId, value); }

    public string BuiltInIdsText => Strings.Format("ConnectionsBuiltInIds",
        BuildInfo.BuiltInDiscordAppId is null ? Strings.Get("ConnectionsNone") : Strings.Get("ConnectionsPresent"),
        BuildInfo.BuiltInRobloxClientId is null ? Strings.Get("ConnectionsNone") : Strings.Get("ConnectionsPresent"));

    private void SaveIds()
    {
        _settings.DiscordAppIdOverride = DiscordAppId;
        _settings.RobloxClientIdOverride = RobloxClientId;
    }

    public void Dispose()
    {
        foreach (var s in _subscriptions) s.Dispose();
    }
}
