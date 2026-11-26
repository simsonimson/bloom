using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class ClientPage : Page
{
    public ClientViewModel ViewModel { get; }

    public ClientPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<ClientViewModel>(App.Services);
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void OnRemoveFlag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FlagItem item }) ViewModel.RemoveFlag(item);
    }
}
