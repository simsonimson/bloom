using System.Collections.ObjectModel;
using System.Globalization;
using Bloom.Activity;
using Bloom.App.Services;
using Bloom.Core.Events;
using Bloom.Core.Performance;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Launcher.Bootstrapper;
using Bloom.Launcher.FastFlags;
using Bloom.Launcher.Installation;
using Bloom.Performance;
using Bloom.Platform.Windows.Hardware;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace Bloom.App.Views;

public static class ByteText
{
    public static string Format(long bytes)
    {
        var c = CultureInfo.CurrentCulture;
        return bytes switch
        {
            >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", c) + " GB",
            >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", c) + " MB",
            >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", c) + " KB",
            _ => bytes + " B",
        };
    }
}

public sealed class ProfileItem : ObservableObject
{
    private readonly Action<ProfileItem> _save;
    private GameProfile _model;

    public ProfileItem(GameProfile model, Action<ProfileItem> save)
    {
        _model = model;
        _save = save;
    }

    public GameProfile Model => _model;
    public string Name => _model.Name;

    /// <summary>0 = normal setting, 1..4 = GraphicsPreset + 1.</summary>
    public int PresetIndex
    {
        get => _model.Preset is { } p ? (int)p + 1 : 0;
        set => Update(_model with { Preset = value <= 0 ? null : (GraphicsPreset)(value - 1) });
    }

    /// <summary>0 = normal setting, 1 = on, 2 = off.</summary>
    public int PerformanceIndex
    {
        get => ToIndex(_model.PerformanceMode);
        set => Update(_model with { PerformanceMode = FromIndex(value) });
    }

    public int PttIndex
    {
        get => ToIndex(_model.PushToTalk);
        set => Update(_model with { PushToTalk = FromIndex(value) });
    }

    private static int ToIndex(bool? value) => value switch { true => 1, false => 2, _ => 0 };
    private static bool? FromIndex(int index) => index switch { 1 => true, 2 => false, _ => null };

    private void Update(GameProfile updated)
    {
        if (updated == _model) return;
        _model = updated;
        _save(this);
    }
}

public sealed class CleanupItem : ObservableObject
{
    private string _size = "…";
    private bool _blocked;

    public CleanupItem(CleanupCategory category) => Category = category;

    public CleanupCategory Category { get; }
    public string Title => Strings.Get($"Cleanup{Category}");
    public string Description => Strings.Get($"Cleanup{Category}Hint");
    public string Size { get => _size; set => SetProperty(ref _size, value); }
    public bool Blocked { get => _blocked; set { if (SetProperty(ref _blocked, value)) OnPropertyChanged(nameof(CanClean)); } }
    public bool CanClean => !Blocked;
}

public sealed class PerformanceViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly PerformanceModeService _mode;
    private readonly PerformanceMonitor _monitor;
    private readonly GameProfileStore _profiles;
    private readonly ActivityTracker _activity;
    private readonly ExperienceResolver _resolver;
    private readonly IFavoritesStore _favorites;
    private readonly CleanupService _cleanup;
    private readonly LaunchPreparation _preparation;
    private readonly RobloxBootstrapper _bootstrapper;
    private readonly FastFlagService _fastFlags;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly IDisposable _watch;
    private readonly DispatcherQueue _dispatcher;
    private readonly int? _refreshRate = DisplayInfo.PrimaryRefreshRate();
    private readonly PowerScheme? _bestPlan = PowerPlans.BestPerformance();
    private FastFlagAllowlist _allowlist = FastFlagAllowlist.Empty;
    private PerformanceSample? _sample;
    private string _cleanupStatus = "";

    public PerformanceViewModel(AppSettings settings, PerformanceModeService mode, PerformanceMonitor monitor, GameProfileStore profiles,
        ActivityTracker activity, ExperienceResolver resolver, IFavoritesStore favorites, CleanupService cleanup,
        LaunchPreparation preparation, RobloxBootstrapper bootstrapper, FastFlagService fastFlags, IEventBus events)
    {
        _settings = settings;
        _mode = mode;
        _monitor = monitor;
        _profiles = profiles;
        _activity = activity;
        _resolver = resolver;
        _favorites = favorites;
        _cleanup = cleanup;
        _preparation = preparation;
        _bootstrapper = bootstrapper;
        _fastFlags = fastFlags;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        AddCurrentGameCommand = new AsyncRelayCommand(AddCurrentGameAsync);
        AddFavoriteCommand = new AsyncRelayCommand(AddFavoriteAsync);
        RemoveProfileCommand = new RelayCommand<ProfileItem>(p => { if (p is not null) _profiles.Remove(p.Model.UniverseId); });
        RescanCommand = new AsyncRelayCommand(RescanAsync);

        foreach (var c in Enum.GetValues<CleanupCategory>()) CleanupItems.Add(new CleanupItem(c));
        foreach (var f in favorites.All()) FavoriteChoices.Add(new FavoriteItem(f));

        _watch = monitor.Watch();
        _sample = monitor.Latest;
        _subscriptions.Add(events.Subscribe<PerformanceSampled>(e => _dispatcher.TryEnqueue(() =>
        {
            _sample = e.Sample;
            OnPropertyChanged(nameof(HasSample));
            OnPropertyChanged(nameof(NoSample));
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(GpuText));
            OnPropertyChanged(nameof(RamText));
            OnPropertyChanged(nameof(GpuMemoryText));
            OnPropertyChanged(nameof(PingText));
        })));
        _subscriptions.Add(events.Subscribe<PerformanceModeChanged>(_ => _dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(ModeStatus)))));
        _subscriptions.Add(events.Subscribe<ActivityStateChanged>(_ => _dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(HasCurrentGame)))));
        _subscriptions.Add(events.Subscribe<SettingChanged>(e => _dispatcher.TryEnqueue(() =>
        {
            if (e.Key is AppSettings.Keys.PerfModeEnabled or AppSettings.Keys.PerfOverlay) OnPropertyChanged(string.Empty);
        })));
        _profiles.Changed += OnProfilesChanged;
        LoadProfiles();
        _ = LoadAllowlistAsync();
        _ = RescanAsync();
    }

    public IAsyncRelayCommand AddCurrentGameCommand { get; }
    public IAsyncRelayCommand AddFavoriteCommand { get; }
    public IRelayCommand<ProfileItem> RemoveProfileCommand { get; }
    public IAsyncRelayCommand RescanCommand { get; }
    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public ObservableCollection<FavoriteItem> FavoriteChoices { get; } = new();
    public ObservableCollection<CleanupItem> CleanupItems { get; } = new();
    public FavoriteItem? SelectedFavorite { get; set; }

    // ------------------------------------------------------------------ performance mode

    public bool ModeEnabled
    {
        get => _settings.PerfModeEnabled;
        set { if (value != _settings.PerfModeEnabled) { _settings.PerfModeEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeStatus)); } }
    }

    public int PriorityIndex
    {
        get => (int)_settings.PerfPriority;
        set { if (value >= 0 && value != (int)_settings.PerfPriority) { _settings.PerfPriority = (ProcessPriority)value; OnPropertyChanged(); } }
    }

    public bool PowerPlan
    {
        get => _settings.PerfPowerPlan;
        set { if (value != _settings.PerfPowerPlan) { _settings.PerfPowerPlan = value; OnPropertyChanged(); } }
    }

    public bool PowerPlanAvailable => _bestPlan is not null;

    public string PowerPlanText => _bestPlan is { } plan
        ? Strings.Format("PerfPowerPlanTarget", plan.Name)
        : Strings.Get("PerfPowerPlanMissing");

    public bool GpuHighPerformance
    {
        get => _settings.PerfGpuHighPerformance;
        set { if (value != _settings.PerfGpuHighPerformance) { _settings.PerfGpuHighPerformance = value; OnPropertyChanged(); } }
    }

    public string ModeStatus
    {
        get
        {
            if (_mode.Active)
            {
                var skipped = _mode.Skipped.Select(s => Strings.Get(s == "priority" ? "PerfSkippedPriority" : "PerfSkippedPowerPlan"));
                var text = Strings.Get("PerfModeActive");
                return _mode.Skipped.Count == 0 ? text : $"{text} {string.Join(" ", skipped)}";
            }
            if (_mode.Override is { } forced) return Strings.Get(forced ? "PerfModeProfileOn" : "PerfModeProfileOff");
            return Strings.Get(_settings.PerfModeEnabled ? "PerfModeWaiting" : "PerfModeOff");
        }
    }

    // ------------------------------------------------------------------ presets

    public int PresetIndex
    {
        get => (int)_settings.PerfPreset;
        set
        {
            if (value < 0 || value == (int)_settings.PerfPreset) return;
            _settings.PerfPreset = (GraphicsPreset)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CoverageText));
            ApplyFlagsNow();
        }
    }

    public double FpsCap
    {
        get => _settings.PerfFpsCap;
        set
        {
            var cap = double.IsNaN(value) ? 0 : (int)Math.Clamp(Math.Round(value), 0, GraphicsPresets.Unlimited);
            if (cap == _settings.PerfFpsCap) return;
            _settings.PerfFpsCap = cap;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CoverageText));
            ApplyFlagsNow();
        }
    }

    public string RefreshRateText => _refreshRate is { } hz ? Strings.Format("PerfRefreshRate", hz) : "";

    public string CoverageText
    {
        get
        {
            var preset = _settings.PerfPreset;
            if (preset == GraphicsPreset.Default && _settings.PerfFpsCap == 0) return Strings.Get("PerfPresetDefaultHint");
            if (!_allowlist.IsAvailable) return Strings.Get("PerfPresetAllowlistUnknown");
            var (allowed, total) = GraphicsPresets.Coverage(preset, _settings.PerfFpsCap, _refreshRate, _allowlist);
            return Strings.Format("PerfPresetCoverage", allowed, total);
        }
    }

    private async Task LoadAllowlistAsync()
    {
        _allowlist = await _fastFlags.GetAllowlistAsync();
        OnPropertyChanged(nameof(CoverageText));
    }

    private void ApplyFlagsNow()
    {
        var dir = _settings.BootstrapperEnabled ? _bootstrapper.GetInstalled()?.Directory : RobloxInstallation.FindOfficial().VersionDirectory;
        if (dir is not null) _ = _preparation.ApplyFlagsAsync(dir);
    }

    // ------------------------------------------------------------------ monitor

    public bool HasSample => _sample is not null;
    public bool NoSample => _sample is null;
    public string CpuText => _sample is { } s ? s.CpuPercent.ToString("0", CultureInfo.CurrentCulture) + " %" : "–";
    public string GpuText => _sample?.GpuPercent is { } g ? g.ToString("0", CultureInfo.CurrentCulture) + " %" : "–";
    public string RamText => _sample is { } s ? ByteText.Format(s.WorkingSetBytes) : "–";
    public string GpuMemoryText => _sample?.GpuMemoryBytes is { } m ? ByteText.Format(m) : "–";
    public string PingText => _sample?.PingMs is { } p ? $"{p} ms" : Strings.Get("PerfPingUnavailable");

    public bool Overlay
    {
        get => _settings.PerfOverlay;
        set { if (value != _settings.PerfOverlay) { _settings.PerfOverlay = value; OnPropertyChanged(); } }
    }

    public int OverlayCornerIndex
    {
        get => (int)_settings.PerfOverlayCorner;
        set { if (value >= 0 && value != (int)_settings.PerfOverlayCorner) { _settings.PerfOverlayCorner = (OverlayCorner)value; OnPropertyChanged(); } }
    }

    // ------------------------------------------------------------------ profiles

    public bool HasCurrentGame => _activity.State.Experience is not null;
    public bool NoProfiles => Profiles.Count == 0;

    private void OnProfilesChanged() => _dispatcher.TryEnqueue(LoadProfiles);

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _profiles.All()) Profiles.Add(new ProfileItem(p, item => _profiles.Save(item.Model)));
        OnPropertyChanged(nameof(NoProfiles));
    }

    private Task AddCurrentGameAsync()
    {
        if (_activity.State.Experience is { } e && _profiles.Find(e.UniverseId) is null)
            _profiles.Save(new GameProfile(e.UniverseId, e.Name, null, null, null));
        return Task.CompletedTask;
    }

    private async Task AddFavoriteAsync()
    {
        if (SelectedFavorite is not { } favorite) return;
        var info = await _resolver.ResolveAsync(favorite.PlaceId);
        if (info is not null && _profiles.Find(info.UniverseId) is null)
            _profiles.Save(new GameProfile(info.UniverseId, info.Name, null, null, null));
    }

    // ------------------------------------------------------------------ cleanup

    /// <summary>Set by the page: confirmation dialog before deleting.</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    public string CleanupStatus { get => _cleanupStatus; private set => SetProperty(ref _cleanupStatus, value); }

    private async Task RescanAsync()
    {
        var scans = await Task.Run(_cleanup.Scan);
        foreach (var scan in scans)
        {
            var item = CleanupItems.First(i => i.Category == scan.Category);
            item.Size = scan.Files == 0 ? Strings.Get("CleanupEmpty") : $"{ByteText.Format(scan.Bytes)} · {Strings.Format("CleanupFiles", scan.Files)}";
            item.Blocked = scan.Blocked || scan.Files == 0;
        }
    }

    public async Task CleanAsync(CleanupItem item)
    {
        if (Confirm is null || !await Confirm(Strings.Format("CleanupConfirm", item.Title))) return;
        var freed = await Task.Run(() => _cleanup.Clean(item.Category));
        CleanupStatus = Strings.Format("CleanupDone", ByteText.Format(freed));
        await RescanAsync();
    }

    public void Dispose()
    {
        _watch.Dispose();
        _profiles.Changed -= OnProfilesChanged;
        foreach (var s in _subscriptions) s.Dispose();
    }
}
