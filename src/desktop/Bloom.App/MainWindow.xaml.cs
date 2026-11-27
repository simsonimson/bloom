using Bloom.App.Views;
using Bloom.Core.Settings;
using Bloom.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;

namespace Bloom.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["overview"] = typeof(OverviewPage),
        ["play"] = typeof(PlayPage),
        ["client"] = typeof(ClientPage),
        ["voice"] = typeof(VoicePage),
        ["connections"] = typeof(ConnectionsPage),
        ["settings"] = typeof(SettingsPage),
    };

    private readonly AppSettings _settings;
    private bool _allowClose;
    private bool _sized;

    public MainWindow()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<AppSettings>();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = ColorHelper.FromArgb(0xFF, 0xF2, 0xF2, 0xF3);
        titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(0xFF, 0xA1, 0xA1, 0xA8);
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0xFF, 0x1C, 0x1C, 0x1F);
        titleBar.ButtonHoverForegroundColor = ColorHelper.FromArgb(0xFF, 0xF2, 0xF2, 0xF3);
        titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0xFF, 0x2A, 0x2A, 0x2E);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "bloom.ico"));
        AppWindow.Closing += OnClosing;

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    /// <summary>Shows the window (centred on first show) and fades the content in.</summary>
    public void ShowWithFade()
    {
        if (!_sized)
        {
            SizeAndCenter();
            _sized = true;
        }

        var wasVisible = AppWindow.IsVisible;
        AppWindow.Show();
        Activate();
        NativeWindow.BringToFront(WindowNative.GetWindowHandle(this));

        if (wasVisible && Root.Opacity >= 1) return;
        if (!NativeWindow.AnimationsEnabled())
        {
            Root.Opacity = 1;
            return;
        }
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(fade, Root);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var story = new Storyboard();
        story.Children.Add(fade);
        story.Completed += (_, _) => Root.Opacity = 1;
        story.Begin();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void SizeAndCenter()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = NativeWindow.WorkAreaFor(hwnd);

        var width = Math.Min((int)(1120 * scale), area.Width);
        var height = Math.Min((int)(720 * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Physical pixels: scale the 960 × 640 minimum from the design system.
            presenter.PreferredMinimumWidth = (int)(960 * scale);
            presenter.PreferredMinimumHeight = (int)(640 * scale);
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_settings.CloseToTray)
        {
            AppWindow.Hide();
        }
        else
        {
            App.Current.Quit();
        }
    }

    /// <summary>Selects a page by its navigation tag ("overview", "connections", …).</summary>
    public void NavigateTo(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag);
        if (item is not null) Nav.SelectedItem = item;
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page)
            && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new SuppressNavigationTransitionInfo());
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
