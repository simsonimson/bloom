using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bloom.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    public HotkeysViewModel Hotkeys { get; }

    public SettingsPage()
    {
        ViewModel = ActivatorUtilities.CreateInstance<SettingsViewModel>(App.Services);
        Hotkeys = ActivatorUtilities.CreateInstance<HotkeysViewModel>(App.Services);
        InitializeComponent();
        Unloaded += (_, _) => Hotkeys.Dispose();
    }

    private void OnChangeHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HotkeyRow row }) _ = Hotkeys.ChangeAsync(row);
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HotkeyRow row }) Hotkeys.Clear(row);
    }

    private void OnResetHotkeys(object sender, RoutedEventArgs e) => Hotkeys.Reset();
}
