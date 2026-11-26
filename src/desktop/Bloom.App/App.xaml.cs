using Bloom.App.Services;
using Bloom.Core;
using Bloom.Core.Settings;
using Bloom.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Bloom.App;

public partial class App : Application
{
    private readonly string[] _args;
    private readonly DispatcherQueue _dispatcher;
    private ILogger<App>? _logger;
    private MainWindow? _main;
    private IntroWindow? _intro;
    private TrayIcon? _tray;
    private bool _exiting;

    public static IServiceProvider Services { get; private set; } = null!;
    public static new App Current => (App)Application.Current;

    public App(string[] args)
    {
        _args = args;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // bloom lives in the tray: closing the last window must not end the process.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            _logger?.LogCritical(e.Exception, "Unhandled UI exception");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger?.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = AppServices.Build(new BloomPaths());
        _logger = Services.GetRequiredService<ILogger<App>>();
        var settings = Services.GetRequiredService<AppSettings>();
        _logger.LogInformation("bloom starting, args: {Args}", string.Join(' ', _args));

        ApplyLanguage(settings.Language);
        AppInstance.GetCurrent().Activated += OnRedirectedActivation;

        _main = new MainWindow();
        CreateTray();

        var startInTray = _args.Contains(Autostart.TrayArgument);
        if (startInTray)
        {
            _logger.LogInformation("Started in tray");
            return;
        }

        if (settings.IntroMode == IntroMode.Off)
        {
            _main.ShowWithFade();
            return;
        }

        _intro = new IntroWindow(settings.IntroMode, reducedMotion: !NativeWindow.AnimationsEnabled());
        _intro.Finished += (_, _) =>
        {
            _main.ShowWithFade();
            _intro.Close();
            _intro = null;
        };
        _intro.Start();
    }

    /// <summary>Shows the main window (tray click, second start, notification).</summary>
    public void ShowMainWindow()
    {
        if (_main is null || _exiting) return;
        if (_intro is not null) return; // intro will show it when done
        _main.ShowWithFade();
    }

    /// <summary>Runs an action on the UI thread.</summary>
    public void Dispatch(Action action) => _dispatcher.TryEnqueue(() => action());

    public void Quit()
    {
        if (_exiting) return;
        _exiting = true;
        _logger?.LogInformation("bloom exiting");
        _tray?.Dispose();
        _main?.CloseForExit();
        _intro?.Close();
        Serilog.Log.CloseAndFlush();
        Exit();
    }

    private void CreateTray()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "bloom.ico");
            _tray = new TrayIcon(iconPath, "bloom")
            {
                MenuProvider = () =>
                [
                    new TrayMenuItem(Strings.Get("TrayOpen"), ShowMainWindow),
                    null,
                    new TrayMenuItem(Strings.Get("TrayQuit"), Quit),
                ],
            };
            _tray.Activated += (_, _) => ShowMainWindow();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Tray icon could not be created");
        }
    }

    private void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        // Raised on a background thread when a second bloom.exe was started.
        _dispatcher.TryEnqueue(ShowMainWindow);
    }

    private void ApplyLanguage(AppLanguage language)
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language switch
            {
                AppLanguage.German => "de-DE",
                AppLanguage.English => "en-US",
                _ => "",
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Language override failed");
        }
    }
}
