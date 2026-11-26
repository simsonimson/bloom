using Bloom.App.Services;
using Bloom.Launcher.Links;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class OverviewPage : Page
{
    public OverviewViewModel ViewModel { get; }

    public OverviewPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<OverviewViewModel>(App.Services);
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void OnLaunchFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FavoriteItem item })
            _ = App.Services.GetRequiredService<LaunchCoordinator>().LaunchAsync(LaunchRequest.ForPlace(item.PlaceId));
    }
}
