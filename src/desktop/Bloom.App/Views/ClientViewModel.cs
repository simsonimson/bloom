using System.Collections.ObjectModel;
using System.Diagnostics;
using Bloom.App.Services;
using Bloom.Core.Events;
using Bloom.Core.Settings;
using Bloom.Launcher.Bootstrapper;
using Bloom.Launcher.FastFlags;
using Bloom.Launcher.Installation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Windows.Storage.Pickers;

namespace Bloom.App.Views;

public sealed class ClientViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly LaunchCoordinator _launcher;
    private readonly RobloxBootstrapper _bootstrapper;
    private readonly FastFlagService _fastFlags;
    private readonly BackupService _backup;
    private readonly ILogger<ClientViewModel> _logger;
    private readonly IDisposable _subscription;
    private FastFlagAllowlist _allowlist = FastFlagAllowlist.Empty;
    private string _status = "";
    private string _progressText = "";
    private double _progress;
    private bool _busy;
    private string _flagsStatus = "";
    private string _newFlagName = "";
    private string _newFlagValue = "";
    private string _backupStatus = "";

    public ClientViewModel(AppSettings settings, LaunchCoordinator launcher, RobloxBootstrapper bootstrapper,
        FastFlagService fastFlags, BackupService backup, IEventBus events, ILogger<ClientViewModel> logger)
    {
        _settings = settings;
        _launcher = launcher;
        _bootstrapper = bootstrapper;
        _fastFlags = fastFlags;
        _backup = backup;
        _logger = logger;

        UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        AddFlagCommand = new RelayCommand(AddFlag);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _subscription = events.Subscribe<LaunchProgressChanged>(p => dispatcher.TryEnqueue(() => OnProgress(p)));
        RefreshStatus();
        LoadFlags();
        _ = LoadAllowlistAsync();
    }

    public IAsyncRelayCommand UpdateNowCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand AddFlagCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public ObservableCollection<FlagItem> Flags { get; } = new();
    public ObservableCollection<string> FlagSuggestions { get; } = new();

    public bool BootstrapperEnabled
    {
        get => _settings.BootstrapperEnabled;
        set
        {
            if (value == _settings.BootstrapperEnabled) return;
            try
            {
                _launcher.SetBootstrapperEnabled(value);
            }
            catch (Exception ex)
            {
                Status = Strings.Format("ClientToggleFailed", ex.Message);
            }
            OnPropertyChanged();
            RefreshStatus();
        }
    }

    public bool LogDetectionEnabled
    {
        get => _settings.LogDetectionEnabled;
        set
        {
            if (value == _settings.LogDetectionEnabled) return;
            _settings.LogDetectionEnabled = value;
            OnPropertyChanged();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string FlagsStatus { get => _flagsStatus; private set => SetProperty(ref _flagsStatus, value); }
    public string BackupStatus { get => _backupStatus; private set => SetProperty(ref _backupStatus, value); }

    public string NewFlagName
    {
        get => _newFlagName;
        set
        {
            if (!SetProperty(ref _newFlagName, value)) return;
            FlagSuggestions.Clear();
            if (value.Length < 2) return;
            foreach (var name in _allowlist.Names.Where(n => n.Contains(value, StringComparison.OrdinalIgnoreCase)).Order().Take(20))
                FlagSuggestions.Add(name);
        }
    }

    public string NewFlagValue { get => _newFlagValue; set => SetProperty(ref _newFlagValue, value); }

    private void RefreshStatus()
    {
        var installed = _bootstrapper.GetInstalled();
        var official = RobloxInstallation.FindOfficial(LaunchCoordinator.BloomExecutable, _settings.ProtocolBackup.GetValueOrDefault("roblox-player"));
        if (_settings.BootstrapperEnabled)
            Status = installed is null ? Strings.Get("ClientNotInstalledYet") : Strings.Format("ClientInstalled", installed.VersionGuid);
        else
            Status = official.IsPresent ? Strings.Format("ClientOfficial", Path.GetFileName(official.VersionDirectory ?? official.LauncherPath ?? "")) : Strings.Get("ClientOfficialMissing");
    }

    private void OnProgress(LaunchProgressChanged p)
    {
        Busy = p.Phase is LaunchPhase.Checking or LaunchPhase.Downloading or LaunchPhase.Extracting;
        Progress = p.Fraction * 100;
        ProgressText = p.Phase switch
        {
            LaunchPhase.Checking => Strings.Get("TrayChecking"),
            LaunchPhase.Downloading => Strings.Format("TrayDownloading", (int)(p.Fraction * 100)),
            LaunchPhase.Extracting => Strings.Format("TrayExtracting", (int)(p.Fraction * 100)),
            LaunchPhase.Starting => Strings.Get("TrayStarting"),
            LaunchPhase.Fallback => Strings.Get("LaunchFallback"),
            LaunchPhase.Failed => p.Message ?? Strings.Get("LaunchFailed"),
            _ => ProgressText,
        };
        if (p.Phase == LaunchPhase.Idle)
        {
            RefreshStatus();
            OnPropertyChanged(nameof(BootstrapperEnabled));
        }
    }

    private async Task UpdateNowAsync()
    {
        var client = await _launcher.UpdateClientAsync();
        if (client is not null)
        {
            try { _fastFlags.Apply(client.Directory); } catch (Exception ex) { _logger.LogWarning(ex, "Applying FastFlags failed"); }
            ProgressText = Strings.Format("ClientInstalled", client.VersionGuid);
        }
        RefreshStatus();
    }

    private void OpenFolder()
    {
        var dir = _settings.BootstrapperEnabled
            ? _bootstrapper.GetInstalled()?.Directory ?? _bootstrapper.VersionsRoot
            : RobloxInstallation.FindOfficial().VersionDirectory ?? RobloxInstallation.OfficialRoot;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open {Dir}", dir);
        }
    }

    // ------------------------------------------------------------------ FastFlags

    private async Task LoadAllowlistAsync()
    {
        FlagsStatus = Strings.Get("FlagsLoadingAllowlist");
        _allowlist = await _fastFlags.GetAllowlistAsync();
        FlagsStatus = !_allowlist.IsAvailable
            ? Strings.Get("FlagsAllowlistUnavailable")
            : Strings.Format(_allowlist.FromCache ? "FlagsAllowlistCached" : "FlagsAllowlistLoaded", _allowlist.Names.Count, _allowlist.FetchedAt.ToLocalTime().ToString("g"));
        LoadFlags();
    }

    private void LoadFlags()
    {
        Flags.Clear();
        foreach (var flag in _fastFlags.Load()) Flags.Add(new FlagItem(flag, _allowlist.StatusOf(flag.Name)));
    }

    private void AddFlag()
    {
        var name = NewFlagName.Trim();
        if (!FastFlagService.IsValidName(name))
        {
            FlagsStatus = Strings.Get("FlagsInvalidName");
            return;
        }
        if (_allowlist.IsAvailable && !_allowlist.Names.Contains(name))
        {
            FlagsStatus = Strings.Get("FlagsNotOnAllowlist");
            return;
        }
        if (FastFlagService.NormalizeValue(name, NewFlagValue) is not { } value)
        {
            FlagsStatus = Strings.Get("FlagsInvalidValue");
            return;
        }
        var flags = _fastFlags.Load().Where(f => f.Name != name).Append(new FastFlag(name, value)).ToList();
        SaveFlags(flags);
        NewFlagName = "";
        NewFlagValue = "";
    }

    public void RemoveFlag(FlagItem item) => SaveFlags(_fastFlags.Load().Where(f => f.Name != item.Name).ToList());

    private void SaveFlags(IReadOnlyList<FastFlag> flags)
    {
        _fastFlags.Save(flags);
        // Apply right away to the client bloom would start; the next launch re-applies anyway.
        var target = _settings.BootstrapperEnabled ? _bootstrapper.GetInstalled()?.Directory : RobloxInstallation.FindOfficial().VersionDirectory;
        if (target is not null)
        {
            try { _fastFlags.Apply(target, flags); } catch (Exception ex) { _logger.LogWarning(ex, "Applying FastFlags failed"); }
        }
        FlagsStatus = Strings.Get("FlagsSaved");
        LoadFlags();
    }

    // ------------------------------------------------------------------ Backup

    private async Task ExportAsync()
    {
        var picker = new FileSavePicker { SuggestedFileName = $"bloom-backup-{DateTime.Now:yyyy-MM-dd}" };
        picker.FileTypeChoices.Add("bloom backup", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await _backup.ExportAsync(file.Path);
            BackupStatus = Strings.Format("BackupExported", file.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed");
            BackupStatus = Strings.Get("BackupFailed");
        }
    }

    private async Task ImportAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var count = await _backup.ImportAsync(file.Path);
            BackupStatus = Strings.Format("BackupImported", count);
            LoadFlags();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed");
            BackupStatus = Strings.Get("BackupFailed");
        }
    }

    public void Dispose() => _subscription.Dispose();
}
