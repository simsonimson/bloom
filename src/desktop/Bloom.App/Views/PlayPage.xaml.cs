using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class PlayPage : Page
{
    public PlayViewModel ViewModel { get; }

    public PlayPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<PlayViewModel>(App.Services);
        InitializeComponent();
    }

    private void OnLaunchFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FavoriteItem item }) _ = ViewModel.LaunchFavoriteAsync(item);
    }

    private void OnRemoveFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FavoriteItem item }) ViewModel.RemoveFavorite(item);
    }

    private void OnLaunchRecent(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecentItem item }) _ = ViewModel.LaunchRecentAsync(item);
    }

    private void OnFavoriteRecent(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecentItem { Model.PlaceId: long place } }) _ = ViewModel.AddFavoriteAsync(place);
    }
}
