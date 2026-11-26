using Bloom.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class VoicePage : Page
{
    public VoiceViewModel ViewModel { get; }

    public VoicePage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<VoiceViewModel>(App.Services);
        ViewModel.ConfirmSystemWide = ConfirmSystemWideAsync;
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private async Task<bool> ConfirmSystemWideAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("PttConfirmTitle"),
            Content = new TextBlock { Text = Strings.Get("PttConfirmText"), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            PrimaryButtonText = Strings.Get("PttConfirmYes"),
            CloseButtonText = Strings.Get("PttConfirmNo"),
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
