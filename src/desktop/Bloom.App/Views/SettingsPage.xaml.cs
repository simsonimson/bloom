using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<SettingsViewModel>(App.Services);
        InitializeComponent();
    }
}
