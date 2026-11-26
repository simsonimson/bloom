using System.Collections.ObjectModel;
using Bloom.Activity;
using Bloom.App.Services;
using Bloom.Core.Roblox;
using Bloom.Launcher.Links;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bloom.App.Views;

public sealed class PlayViewModel : ObservableObject
{
    private readonly LaunchCoordinator _launcher;
    private readonly IFavoritesStore _favorites;
    private readonly ISessionStore _sessions;
    private readonly ExperienceResolver _resolver;
    private string _linkText = "";
    private string _message = "";

    public PlayViewModel(LaunchCoordinator launcher, IFavoritesStore favorites, ISessionStore sessions, ExperienceResolver resolver)
    {
        _launcher = launcher;
        _favorites = favorites;
        _sessions = sessions;
        _resolver = resolver;
        LaunchLinkCommand = new AsyncRelayCommand(LaunchLinkAsync);
        AddFavoriteCommand = new AsyncRelayCommand(AddFavoriteFromLinkAsync);
        Reload();
    }

    public ObservableCollection<FavoriteItem> Favorites { get; } = new();
    public ObservableCollection<RecentItem> Recent { get; } = new();
    public IAsyncRelayCommand LaunchLinkCommand { get; }
    public IAsyncRelayCommand AddFavoriteCommand { get; }

    public string LinkText
    {
        get => _linkText;
        set => SetProperty(ref _linkText, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool HasNoFavorites => Favorites.Count == 0;
    public bool HasNoRecent => Recent.Count == 0;

    public Task LaunchAsync(LaunchRequest request)
    {
        Message = Strings.Get("PlayStarting");
        return _launcher.LaunchAsync(request);
    }

    public Task LaunchFavoriteAsync(FavoriteItem item) => LaunchAsync(LaunchRequest.ForPlace(item.PlaceId));

    public Task LaunchRecentAsync(RecentItem item) =>
        item.Model.PlaceId is long place ? LaunchAsync(LaunchRequest.ForPlace(place)) : Task.CompletedTask;

    public void RemoveFavorite(FavoriteItem item)
    {
        _favorites.Remove(item.Model.Id);
        Reload();
    }

    public async Task AddFavoriteAsync(long placeId)
    {
        var info = await _resolver.ResolveAsync(placeId);
        _favorites.Add(placeId, info?.Name ?? $"Place {placeId}", info?.IconUrl);
        Reload();
    }

    private async Task LaunchLinkAsync()
    {
        if (!LinkParser.TryParse(LinkText, out var request))
        {
            Message = Strings.Get("PlayInvalidLink");
            return;
        }
        await LaunchAsync(request);
    }

    private async Task AddFavoriteFromLinkAsync()
    {
        if (!LinkParser.TryParse(LinkText, out var request) || request.PlaceId is not long place)
        {
            Message = Strings.Get("PlayInvalidLink");
            return;
        }
        Message = Strings.Get("PlayAddingFavorite");
        await AddFavoriteAsync(place);
        Message = "";
        LinkText = "";
    }

    private void Reload()
    {
        Favorites.Clear();
        foreach (var f in _favorites.All()) Favorites.Add(new FavoriteItem(f));
        Recent.Clear();
        foreach (var s in _sessions.Recent(20)) Recent.Add(new RecentItem(s));
        OnPropertyChanged(nameof(HasNoFavorites));
        OnPropertyChanged(nameof(HasNoRecent));
    }
}
