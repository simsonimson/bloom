using Bloom.Core.Settings;
using Bloom.Platform.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using WinRT.Interop;

namespace Bloom.App;

/// <summary>
/// Borderless startup intro (Blueprint 9.3). Plays the pre-rendered bloom3d video, fades to black
/// while the petals blow away, then raises <see cref="Finished"/>. Never shown again after start.
/// </summary>
public sealed partial class IntroWindow : Window
{
    private const double SourceAspect = 960.0 / 760.0;
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan StillDuration = TimeSpan.FromMilliseconds(600);

    private readonly IntroMode _mode;
    private readonly bool _reducedMotion;
    private readonly DispatcherQueue _dispatcher;
    private MediaPlayer? _player;
    private DispatcherQueueTimer? _timer;
    private bool _shown;
    private bool _ending;

    public event EventHandler? Finished;

    public IntroWindow(IntroMode mode, bool reducedMotion)
    {
        InitializeComponent();
        _mode = mode;
        _reducedMotion = reducedMotion;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "bloom.ico"));

        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        Root.PointerPressed += (_, _) => Skip();
    }

    public void Start()
    {
        SizeAndCenter();

        if (_reducedMotion)
        {
            ShowStill();
            return;
        }

        var file = _mode == IntroMode.Short ? "intro-short.mp4" : "intro-full.mp4";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "intro", file);
        if (!File.Exists(path))
        {
            ShowStill();
            return;
        }

        _player = new MediaPlayer
        {
            Source = MediaSource.CreateFromUri(new Uri(path)),
            AutoPlay = false,
            IsLoopingEnabled = false,
            Volume = 0,
        };
        _player.MediaOpened += (_, _) => _dispatcher.TryEnqueue(OnOpened);
        _player.MediaFailed += (_, _) => _dispatcher.TryEnqueue(ShowStill);
        Player.SetMediaPlayer(_player);

        // If decoding never starts, fall back to the still instead of a black window.
        var timeout = _dispatcher.CreateTimer();
        timeout.Interval = OpenTimeout;
        timeout.IsRepeating = false;
        timeout.Tick += (_, _) => { if (!_shown) ShowStill(); };
        timeout.Start();
    }

    private void OnOpened()
    {
        if (_shown || _player is null) return;
        _shown = true;
        // The window becomes visible only once the first frame can be shown: no black flash.
        Activate();
        Player.Focus(FocusState.Programmatic);
        _player.Play();

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += (_, _) =>
        {
            var session = _player.PlaybackSession;
            var end = session.NaturalDuration;
            if (end > TimeSpan.Zero && session.Position >= end - FadeOut) End();
        };
        _timer.Start();
    }

    private void ShowStill()
    {
        if (_ending) return;
        _shown = true;
        _player?.Pause();
        Player.Visibility = Visibility.Collapsed;
        Still.Visibility = Visibility.Visible;
        Activate();
        var timer = _dispatcher.CreateTimer();
        timer.Interval = StillDuration;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => End();
        timer.Start();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Escape or VirtualKey.Space or VirtualKey.Enter) Skip();
    }

    private void Skip() => End(TimeSpan.FromMilliseconds(200));

    private void End() => End(FadeOut);

    private void End(TimeSpan fade)
    {
        if (_ending) return;
        _ending = true;
        _timer?.Stop();

        var animation = new DoubleAnimation { To = 0, Duration = fade };
        Storyboard.SetTarget(animation, Root);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var story = new Storyboard();
        story.Children.Add(animation);
        story.Completed += (_, _) =>
        {
            _player?.Pause();
            Player.SetMediaPlayer(null);
            _player?.Dispose();
            _player = null;
            Finished?.Invoke(this, EventArgs.Empty);
        };
        story.Begin();
    }

    private void SizeAndCenter()
    {
        var area = NativeWindow.WorkAreaFor(WindowNative.GetWindowHandle(this));
        var height = (int)(area.Height * 0.7);
        var width = (int)(height * SourceAspect);
        if (width > area.Width * 0.9)
        {
            width = (int)(area.Width * 0.9);
            height = (int)(width / SourceAspect);
        }
        AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height));
    }
}
