using System.Diagnostics;
using Bloom.App.Services;
using Bloom.Core;
using Bloom.Core.Settings;
using Bloom.Platform.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Bloom.App.Views;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly UpdateService _updates;
    private readonly BloomPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;
    private string _updateStatus = "";
    private bool _canInstall;

    public SettingsViewModel(AppSettings settings, UpdateService updates, BloomPaths paths, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _updates = updates;
        _paths = paths;
        _logger = logger;
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync);
        OpenLogsCommand = new RelayCommand(OpenLogs);

        // The registry is the source of truth for autostart (the user may have changed it in Task Manager).
        try { _settings.StartWithWindows = Autostart.IsEnabled(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not read autostart state"); }
    }

    public IAsyncRelayCommand CheckUpdatesCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }
    public IRelayCommand OpenLogsCommand { get; }

    public int IntroIndex
    {
        get => (int)_settings.IntroMode;
        set
        {
            if (value < 0 || value == (int)_settings.IntroMode) return;
            _settings.IntroMode = (IntroMode)value;
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (value == _settings.StartWithWindows) return;
            try
            {
                Autostart.Set(value, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "bloom.exe"));
                _settings.StartWithWindows = value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not change autostart");
            }
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (value == _settings.CloseToTray) return;
            _settings.CloseToTray = value;
            OnPropertyChanged();
        }
    }

    public int LanguageIndex
    {
        get => (int)_settings.Language;
        set
        {
            if (value < 0 || value == (int)_settings.Language) return;
            _settings.Language = (AppLanguage)value;
            OnPropertyChanged();
        }
    }

    public string VersionText => Strings.Format("SettingsVersion", _updates.CurrentVersion);

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public bool CanInstall
    {
        get => _canInstall;
        private set => SetProperty(ref _canInstall, value);
    }

    private async Task CheckUpdatesAsync()
    {
        if (!_updates.IsInstalled)
        {
            UpdateStatus = Strings.Get("UpdateNotInstalled");
            return;
        }
        UpdateStatus = Strings.Get("UpdateChecking");
        try
        {
            var version = await _updates.CheckAsync();
            CanInstall = version is not null;
            UpdateStatus = version is null ? Strings.Get("UpdateNone") : Strings.Format("UpdateAvailable", version);
        }
        catch
        {
            UpdateStatus = Strings.Get("UpdateFailed");
        }
    }

    private async Task InstallUpdateAsync()
    {
        CanInstall = false;
        try
        {
            await _updates.DownloadAndRestartAsync(p =>
                App.Current.Dispatch(() => UpdateStatus = Strings.Format("UpdateDownloading", p)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed");
            UpdateStatus = Strings.Get("UpdateFailed");
        }
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(_paths.Logs);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.Logs}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open log folder");
        }
    }
}
