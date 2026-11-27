using Bloom.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceViewModel ViewModel { get; }

    public PerformancePage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<PerformanceViewModel>(App.Services);
        ViewModel.Confirm = ConfirmAsync;
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void OnRemoveProfile(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProfileItem item }) ViewModel.RemoveProfileCommand.Execute(item);
    }

    private void OnClean(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CleanupItem item }) _ = ViewModel.CleanAsync(item);
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("CleanupConfirmTitle"),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Strings.Get("CleanupConfirmYes"),
            CloseButtonText = Strings.Get("PttConfirmNo"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
