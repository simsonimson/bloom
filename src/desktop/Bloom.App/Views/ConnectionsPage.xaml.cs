using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class ConnectionsPage : Page
{
    public ConnectionsViewModel ViewModel { get; }

    public ConnectionsPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<ConnectionsViewModel>(App.Services);
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Dispose();
    }
}
