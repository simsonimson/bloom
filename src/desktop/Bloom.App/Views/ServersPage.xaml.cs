using Bloom.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class ServersPage : Page
{
    public ServersViewModel ViewModel { get; }

    public ServersPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<ServersViewModel>(App.Services);
        ViewModel.ConfirmRestart = ConfirmRestartAsync;
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void OnJoin(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServerRow row }) _ = ViewModel.JoinAsync(row);
    }

    private async Task<bool> ConfirmRestartAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("ServerRestartTitle"),
            Content = new TextBlock { Text = Strings.Get("ServerRestartText"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Strings.Get("ServerRestartYes"),
            CloseButtonText = Strings.Get("PttConfirmNo"),
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
