using Bloom.App.Services;
using Bloom.Core;
using Bloom.Core.Events;
using Bloom.Core.Settings;
using Bloom.Core.Voice;
using Bloom.Voice;
using Bloom.Core.Performance;
using Bloom.Connect.Roblox;
using Bloom.Launcher.Links;
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
    public static IntPtr MainWindowHandle { get; private set; }
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
        _logger.LogInformation("bloom starting, args: {Args}", string.Join(' ', _args.Select(RobloxOAuth.Redact)));

        ApplyLanguage(settings.Language);
        AppInstance.GetCurrent().Activated += OnRedirectedActivation;

        _main = new MainWindow();
        MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_main);
        CreateTray();
        SubscribeLaunchFeedback();
        SubscribeVoiceFeedback();
        SubscribeHotkeys();
        AppServices.StartBackground(Services);

        // Returning from the Roblox sign-in page in a fresh bloom process: the sign-in that started
        // it is gone, so show the page and let the user start again.
        if (_args.FirstOrDefault(RobloxOAuth.IsCallback) is { } callback)
        {
            HandleOAuthCallback(callback);
            return;
        }

        // Started by the browser through roblox-player:/roblox: — launch without any window.
        if (LinkParser.FindProtocolArgument(_args) is { } protocol && LinkParser.TryParse(protocol, out var request))
        {
            _ = Services.GetRequiredService<LaunchCoordinator>().LaunchAsync(request);
            return;
        }

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
        AppServices.CleanExit(Services);
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
                MenuProvider = BuildTrayMenu,
            };
            _tray.Activated += (_, _) => ShowMainWindow();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Tray icon could not be created");
        }
    }

    private IReadOnlyList<TrayMenuItem?> BuildTrayMenu()
    {
        var settings = Services.GetRequiredService<AppSettings>();
        var voice = Services.GetRequiredService<VoiceService>();
        var items = new List<TrayMenuItem?>
        {
            new(Strings.Get("TrayOpen"), ShowMainWindow),
            null,
            new(Strings.Get(settings.PttEnabled ? "TrayPttOff" : "TrayPttOn"), () =>
            {
                // Setup (key, system-wide confirmation) happens on the Voice page.
                if (!settings.PttEnabled && !VoiceText.ReadyToEnable(settings)) ShowMainWindow();
                else settings.PttEnabled = !settings.PttEnabled;
            }),
        };
        if (settings.PttEnabled && settings.PttMethod == PttMethod.RobloxButton)
            items.Add(new(Strings.Get("TrayToggleRobloxMic"), voice.ToggleFromUi));
        items.Add(null);
        items.Add(new(Strings.Get("TrayQuit"), Quit));
        return items;
    }

    private void SubscribeHotkeys()
    {
        var events = Services.GetRequiredService<IEventBus>();
        var settings = Services.GetRequiredService<AppSettings>();
        var control = Services.GetRequiredService<RobloxControl>();
        events.Subscribe<HotkeyPressed>(e => Dispatch(async () =>
        {
            try
            {
            switch (e.Action)
            {
                case HotkeyAction.Rejoin:
                    if (!await control.RejoinAsync(sameServer: true)) _tray?.ShowNotification("bloom", Strings.Get("ServerNoGame"));
                    break;
                case HotkeyAction.CopyServerLink:
                    _tray?.ShowNotification("bloom", Strings.Get(control.CopyServerLink() ? "ServerLinkCopied" : "ServerNoGame"));
                    break;
                case HotkeyAction.ToggleOverlay:
                    settings.PerfOverlay = !settings.PerfOverlay;
                    break;
                case HotkeyAction.TogglePerformanceMode:
                    settings.PerfModeEnabled = !settings.PerfModeEnabled;
                    _tray?.ShowNotification("bloom", Strings.Get(settings.PerfModeEnabled ? "PerfModeOnNotice" : "PerfModeOffNotice"));
                    break;
                case HotkeyAction.TogglePushToTalk:
                    if (!settings.PttEnabled && !VoiceText.ReadyToEnable(settings)) ShowMainWindow();
                    else settings.PttEnabled = !settings.PttEnabled;
                    break;
                case HotkeyAction.ShowBloom:
                    ShowMainWindow();
                    break;
            }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Hotkey action {Action} failed", e.Action);
            }
        }));
    }

    private void SubscribeVoiceFeedback()
    {
        var events = Services.GetRequiredService<IEventBus>();
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        events.Subscribe<PttStateChanged>(e => Dispatch(() =>
        {
            if (_tray is null) return;
            var icon = e.State switch
            {
                PttState.Ready => "bloom-ptt-ready.ico",
                PttState.Transmitting or PttState.Open => "bloom-ptt-live.ico",
                PttState.Error => "bloom-ptt-error.ico",
                _ => "bloom.ico",
            };
            try { _tray.SetIcon(Path.Combine(assets, icon)); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Tray icon change failed"); }
            _tray.SetTooltip(e.State == PttState.Off ? "bloom" : $"bloom · {Strings.Get(VoiceText.StateKey(e.State))}");
            if (e.State == PttState.Error) _tray.ShowNotification("bloom", Strings.Get("PttErrorNotice"));
        }));
        events.Subscribe<PttActionFailed>(e => Dispatch(() =>
            _tray?.ShowNotification("bloom", Strings.Get(VoiceText.FailureKey(e.Reason)))));
    }

    private void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        // Raised on a background thread when a second bloom.exe was started (e.g. by the browser).
        string? commandLine = null;
        if (e.Kind == ExtendedActivationKind.Launch && e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
            commandLine = launch.Arguments;

        if (FindOAuthCallback(commandLine) is { } callback)
        {
            _dispatcher.TryEnqueue(() => HandleOAuthCallback(callback));
            return;
        }

        if (LinkParser.FindProtocolArgument(commandLine) is { } protocol && LinkParser.TryParse(protocol, out var request))
        {
            _dispatcher.TryEnqueue(() => _ = Services.GetRequiredService<LaunchCoordinator>().LaunchAsync(request));
            return;
        }
        _dispatcher.TryEnqueue(ShowMainWindow);
    }

    private static string? FindOAuthCallback(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return null;
        var start = commandLine.IndexOf(RobloxOAuth.RedirectUri, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var end = commandLine.IndexOfAny(['"', ' '], start);
        return end < 0 ? commandLine[start..] : commandLine[start..end];
    }

    private async void HandleOAuthCallback(string callback)
    {
        ShowMainWindow();
        _main?.NavigateTo("connections");
        var result = await Services.GetRequiredService<RobloxAccountService>().CompleteSignInAsync(callback);
        _logger?.LogInformation("Roblox sign-in callback: {Result}", result);
        if (result != CallbackResult.Ok)
        {
            _tray?.ShowNotification("bloom", Strings.Get(result switch
            {
                CallbackResult.Denied => "RobloxSignInDenied",
                CallbackResult.StateMismatch => "RobloxSignInExpired",
                _ => "RobloxSignInFailed",
            }));
        }
    }

    private void SubscribeLaunchFeedback()
    {
        var events = Services.GetRequiredService<IEventBus>();
        events.Subscribe<LaunchProgressChanged>(p => Dispatch(() =>
        {
            if (_tray is null) return;
            var text = p.Phase switch
            {
                LaunchPhase.Checking => Strings.Get("TrayChecking"),
                LaunchPhase.Downloading => Strings.Format("TrayDownloading", (int)(p.Fraction * 100)),
                LaunchPhase.Extracting => Strings.Format("TrayExtracting", (int)(p.Fraction * 100)),
                LaunchPhase.Starting => Strings.Get("TrayStarting"),
                _ => "bloom",
            };
            _tray.SetTooltip(text);
            if (p.Phase == LaunchPhase.Failed) _tray.ShowNotification("bloom", p.Message ?? Strings.Get("LaunchFailed"));
            if (p.Phase == LaunchPhase.Fallback) _tray.ShowNotification("bloom", Strings.Get("LaunchFallback"));
        }));
        events.Subscribe<BootstrapperPaused>(_ => Dispatch(() =>
            _tray?.ShowNotification("bloom", Strings.Get("BootstrapperPausedNotice"))));
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
