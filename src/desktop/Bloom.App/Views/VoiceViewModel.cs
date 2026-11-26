using System.Collections.ObjectModel;
using Bloom.App.Services;
using Bloom.Core.Settings;
using Bloom.Core.Voice;
using Bloom.Platform.Windows.Audio;
using Bloom.Voice;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Bloom.App.Views;

public sealed class DeviceItem(string id) : ObservableObject
{
    private string _name = "";
    private string _badges = "";
    private double _level;

    public string Id { get; } = id;
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Badges { get => _badges; set => SetProperty(ref _badges, value); }
    public double Level { get => _level; set => SetProperty(ref _level, value); }
}

public sealed record DeviceChoice(string? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class VoiceViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly VoiceService _voice;
    private readonly ILogger<VoiceViewModel> _logger;
    private readonly DispatcherQueueTimer _timer;
    private VoiceSnapshot _snapshot = VoiceSnapshot.Empty;
    private CancellationTokenSource? _keyCapture;
    private CancellationTokenSource? _calibration;
    private CancellationTokenSource? _micTest;
    private bool _capturingKey;
    private bool _calibrating;
    private string _calibrationText = "";
    private bool _micTestRunning;
    private double _micTestProgress;
    private string _micTestStatus = "";
    private bool _micAccessDenied;
    private DeviceChoice? _selectedDevice;
    private bool _updatingChoices;

    public VoiceViewModel(AppSettings settings, VoiceService voice, ILogger<VoiceViewModel> logger)
    {
        _settings = settings;
        _voice = voice;
        _logger = logger;
        SetKeyCommand = new AsyncRelayCommand(SetKeyAsync);
        CalibrateCommand = new AsyncRelayCommand(CalibrateAsync);
        ToggleRobloxMicCommand = new RelayCommand(_voice.ToggleFromUi);
        CorrectStateCommand = new RelayCommand(_voice.CorrectAssumedState);
        MicTestCommand = new AsyncRelayCommand(RunMicTestAsync);

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    /// <summary>Set by the page: shows the "also affects Discord" confirmation.</summary>
    public Func<Task<bool>>? ConfirmSystemWide { get; set; }

    public IAsyncRelayCommand SetKeyCommand { get; }
    public IAsyncRelayCommand CalibrateCommand { get; }
    public IRelayCommand ToggleRobloxMicCommand { get; }
    public IRelayCommand CorrectStateCommand { get; }
    public IAsyncRelayCommand MicTestCommand { get; }
    public ObservableCollection<DeviceItem> Devices { get; } = new();
    public ObservableCollection<DeviceChoice> DeviceChoices { get; } = new();

    // ------------------------------------------------------------------ push-to-talk

    public bool PttEnabled
    {
        get => _settings.PttEnabled;
        set
        {
            if (value == _settings.PttEnabled) return;
            if (value) _ = EnableAsync();
            else _settings.PttEnabled = false;
            OnPropertyChanged();
        }
    }

    private async Task EnableAsync()
    {
        if (_settings.PttMethod == PttMethod.SystemWide && !_settings.PttSystemWideConfirmed)
        {
            if (ConfirmSystemWide is null || !await ConfirmSystemWide())
            {
                OnPropertyChanged(nameof(PttEnabled));
                return;
            }
            _settings.PttSystemWideConfirmed = true;
        }
        _settings.PttEnabled = true;
        OnPropertyChanged(nameof(PttEnabled));
        OnPropertyChanged(nameof(SetupHint));
    }

    public int ModeIndex
    {
        get => IsButtonMethod ? 1 : (int)_settings.PttMode;
        set
        {
            if (value < 0 || IsButtonMethod || value == (int)_settings.PttMode) return;
            _settings.PttMode = (PttMode)value;
            OnPropertyChanged();
        }
    }

    public bool ModeSelectable => !IsButtonMethod;

    public string KeyText => CapturingKey ? Strings.Get("PttPressKey") : VoiceText.KeyName(_settings.PttKey);
    public bool KeyConflict => VoiceText.IsConflictKey(_settings.PttKey);

    public bool CapturingKey
    {
        get => _capturingKey;
        private set
        {
            if (!SetProperty(ref _capturingKey, value)) return;
            OnPropertyChanged(nameof(KeyText));
        }
    }

    public bool ForegroundOnly
    {
        get => _settings.PttForegroundOnly;
        set { if (value != _settings.PttForegroundOnly) { _settings.PttForegroundOnly = value; OnPropertyChanged(); } }
    }

    public bool Sounds
    {
        get => _settings.PttSounds;
        set { if (value != _settings.PttSounds) { _settings.PttSounds = value; OnPropertyChanged(); } }
    }

    /// <summary>Shown when PTT is on but the setup is incomplete.</summary>
    public string SetupHint
    {
        get
        {
            if (!_settings.PttEnabled) return "";
            if (IsButtonMethod && _settings.PttButton is null) return Strings.Get("PttHintCalibrate");
            if (!IsButtonMethod && _settings.PttKey == 0) return Strings.Get("PttHintKey");
            return "";
        }
    }

    public bool HasSetupHint => SetupHint.Length > 0;

    private async Task SetKeyAsync()
    {
        _keyCapture?.Cancel();
        _keyCapture = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CapturingKey = true;
        try
        {
            var vk = await _voice.CaptureKeyAsync(_keyCapture.Token);
            if (vk != 0) _settings.PttKey = vk;
        }
        finally
        {
            CapturingKey = false;
            OnPropertyChanged(nameof(KeyConflict));
            OnPropertyChanged(nameof(SetupHint));
            OnPropertyChanged(nameof(HasSetupHint));
        }
    }

    // ------------------------------------------------------------------ method

    public int MethodIndex
    {
        get => (int)_settings.PttMethod;
        set
        {
            if (value < 0 || value == (int)_settings.PttMethod) return;
            _ = ChangeMethodAsync((PttMethod)value);
        }
    }

    private async Task ChangeMethodAsync(PttMethod method)
    {
        if (method == PttMethod.SystemWide && _settings.PttEnabled && !_settings.PttSystemWideConfirmed)
        {
            if (ConfirmSystemWide is null || !await ConfirmSystemWide())
            {
                OnPropertyChanged(nameof(MethodIndex));
                return;
            }
            _settings.PttSystemWideConfirmed = true;
        }
        _settings.PttMethod = method;
        OnPropertyChanged(nameof(MethodIndex));
        OnPropertyChanged(nameof(IsButtonMethod));
        OnPropertyChanged(nameof(IsAudioMethod));
        OnPropertyChanged(nameof(IsSystemWide));
        OnPropertyChanged(nameof(ModeSelectable));
        OnPropertyChanged(nameof(ModeIndex));
        OnPropertyChanged(nameof(SetupHint));
        OnPropertyChanged(nameof(HasSetupHint));
    }

    public bool IsButtonMethod => _settings.PttMethod == PttMethod.RobloxButton;
    public bool IsAudioMethod => !IsButtonMethod;
    public bool IsSystemWide => _settings.PttMethod == PttMethod.SystemWide;

    public string CalibrationText
    {
        get => _calibrationText.Length > 0 ? _calibrationText
            : Strings.Get(_settings.PttButton is null ? "PttButtonNotSet" : "PttButtonSet");
        private set => SetProperty(ref _calibrationText, value);
    }

    public bool Calibrating
    {
        get => _calibrating;
        private set => SetProperty(ref _calibrating, value);
    }

    private async Task CalibrateAsync()
    {
        _calibration?.Cancel();
        _calibration = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Calibrating = true;
        CalibrationText = Strings.Get("PttCalibrating");
        try
        {
            var position = await _voice.CalibrateAsync(_calibration.Token);
            if (position is not null) _settings.PttButton = position;
            CalibrationText = Strings.Get(position is null ? "PttCalibrationTimeout" : "PttCalibrationDone");
        }
        finally
        {
            Calibrating = false;
            OnPropertyChanged(nameof(SetupHint));
            OnPropertyChanged(nameof(HasSetupHint));
        }
    }

    // ------------------------------------------------------------------ live state

    public string StateText
    {
        get
        {
            if (!_settings.PttEnabled) return Strings.Get("PttStateDisabled");
            if (_snapshot.State == PttState.Off) return Strings.Get(_snapshot.RobloxRunning ? "PttStateStarting" : "PttStateWaiting");
            var text = Strings.Get(VoiceText.StateKey(_snapshot.State));
            return IsButtonMethod && _snapshot.State is PttState.Ready or PttState.Open ? text + " " + Strings.Get("PttAssumed") : text;
        }
    }

    public Brush StateBrush => (Brush)Application.Current.Resources[_snapshot.State switch
    {
        PttState.Transmitting or PttState.Open => "BloomSuccessBrush",
        PttState.Error => "BloomWarningBrush",
        PttState.Ready => "BloomTextBrush",
        _ => "BloomTextMutedBrush",
    }];

    public bool IsAssumedState => IsButtonMethod && _snapshot.State is PttState.Ready or PttState.Open;
    public double LiveLevel { get; private set; }
    public string? InputError => _snapshot.InputError;
    public bool HasInputError => !string.IsNullOrEmpty(_snapshot.InputError);

    // ------------------------------------------------------------------ devices

    public DeviceChoice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_updatingChoices || value is null || value == _selectedDevice) return;
            _selectedDevice = value;
            _settings.PttDeviceId = value.Id;
            OnPropertyChanged();
        }
    }

    public bool NoDevices => _snapshot.Devices.Count == 0;
    public bool HasDevices => !NoDevices;

    /// <summary>The chosen microphone differs from the one Roblox records from.</summary>
    public bool DeviceMismatch =>
        _settings.PttDeviceId is { } chosen
        && _snapshot.Devices.Any(d => d.UsedByRoblox)
        && !_snapshot.Devices.Any(d => d.UsedByRoblox && string.Equals(d.Id, chosen, StringComparison.OrdinalIgnoreCase));

    public int ExitBehaviorIndex
    {
        get => (int)_settings.PttOnRobloxExit;
        set { if (value >= 0 && value != (int)_settings.PttOnRobloxExit) { _settings.PttOnRobloxExit = (RobloxExitBehavior)value; OnPropertyChanged(); } }
    }

    // ------------------------------------------------------------------ mic test

    public bool MicTestRunning { get => _micTestRunning; private set => SetProperty(ref _micTestRunning, value); }
    public double MicTestProgress { get => _micTestProgress; private set => SetProperty(ref _micTestProgress, value); }
    public string MicTestStatus { get => _micTestStatus; private set => SetProperty(ref _micTestStatus, value); }
    public bool MicAccessDenied { get => _micAccessDenied; private set => SetProperty(ref _micAccessDenied, value); }

    private async Task RunMicTestAsync()
    {
        if (MicTestRunning)
        {
            _micTest?.Cancel();
            return;
        }
        _micTest = new CancellationTokenSource();
        MicTestRunning = true;
        MicAccessDenied = false;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var device = _settings.PttDeviceId ?? _snapshot.ControlledDeviceId ?? _snapshot.Devices.FirstOrDefault(d => d.UsedByRoblox)?.Id;
        try
        {
            await _voice.RunMicTestAsync(device, (phase, fraction) => dispatcher.TryEnqueue(() =>
            {
                MicTestProgress = fraction * 100;
                MicTestStatus = Strings.Get(phase switch
                {
                    MicTestPhase.Recording => "MicTestRecording",
                    MicTestPhase.Playing => "MicTestPlaying",
                    _ => "MicTestDone",
                });
            }), _micTest.Token);
            MicTestStatus = Strings.Get(_snapshot.State == PttState.Ready && IsAudioMethod ? "MicTestDoneMuted" : "MicTestDone");
        }
        catch (OperationCanceledException)
        {
            MicTestStatus = "";
        }
        catch (AudioException ex) when (ex.AccessDenied)
        {
            MicAccessDenied = true;
            MicTestStatus = Strings.Get("MicAccessDenied");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mic test failed");
            MicTestStatus = Strings.Get("MicTestFailed");
        }
        finally
        {
            MicTestRunning = false;
            MicTestProgress = 0;
        }
    }

    // ------------------------------------------------------------------ refresh

    private void Refresh()
    {
        _snapshot = _voice.Snapshot;

        // Device rows: reconcile by ID so the list does not flicker.
        var current = _snapshot.Devices;
        foreach (var stale in Devices.Where(d => current.All(c => c.Id != d.Id)).ToList()) Devices.Remove(stale);
        foreach (var device in current)
        {
            var item = Devices.FirstOrDefault(d => d.Id == device.Id);
            if (item is null) Devices.Add(item = new DeviceItem(device.Id));
            item.Name = device.Name;
            item.Level = Math.Sqrt(device.Level) * 100; // perceptual scale
            item.Badges = string.Join(" · ", new[]
            {
                device.IsDefault ? Strings.Get("DeviceDefault") : null,
                device.UsedByRoblox ? Strings.Get("DeviceUsedByRoblox") : null,
                string.Equals(device.Id, _snapshot.ControlledDeviceId, StringComparison.OrdinalIgnoreCase) ? Strings.Get("DeviceControlled") : null,
            }.Where(b => b is not null));
        }

        // Choices only change when the set of devices changes.
        var wanted = new[] { new DeviceChoice(null, Strings.Get("DeviceAutomatic")) }
            .Concat(current.Select(d => new DeviceChoice(d.Id, d.Name))).ToList();
        if (!wanted.SequenceEqual(DeviceChoices))
        {
            _updatingChoices = true;
            DeviceChoices.Clear();
            foreach (var choice in wanted) DeviceChoices.Add(choice);
            _updatingChoices = false;
        }
        var selected = DeviceChoices.FirstOrDefault(c => string.Equals(c.Id, _settings.PttDeviceId, StringComparison.OrdinalIgnoreCase)) ?? DeviceChoices[0];
        if (selected != _selectedDevice)
        {
            _selectedDevice = selected;
            OnPropertyChanged(nameof(SelectedDevice));
        }

        var live = current.FirstOrDefault(d => string.Equals(d.Id, _snapshot.ControlledDeviceId, StringComparison.OrdinalIgnoreCase))
                   ?? current.FirstOrDefault(d => d.UsedByRoblox)
                   ?? current.FirstOrDefault(d => d.IsDefault);
        LiveLevel = live is null ? 0 : Math.Sqrt(live.Level) * 100;

        OnPropertyChanged(nameof(LiveLevel));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(IsAssumedState));
        OnPropertyChanged(nameof(NoDevices));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(DeviceMismatch));
        OnPropertyChanged(nameof(InputError));
        OnPropertyChanged(nameof(HasInputError));
        if (!_calibrating && _calibrationText.Length > 0 && _settings.PttButton is not null && _calibrationText == Strings.Get("PttCalibrating"))
            CalibrationText = "";
    }

    public void Dispose()
    {
        _timer.Stop();
        _keyCapture?.Cancel();
        _calibration?.Cancel();
        _micTest?.Cancel();
    }
}
