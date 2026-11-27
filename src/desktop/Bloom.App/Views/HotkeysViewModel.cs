using System.Collections.ObjectModel;
using Bloom.App.Services;
using Bloom.Core.Performance;
using Bloom.Core.Settings;
using Bloom.Voice;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Bloom.App.Views;

public sealed class HotkeyRow(HotkeyAction action) : ObservableObject
{
    private string _combo = "";
    private bool _conflict;
    private bool _capturing;

    public HotkeyAction Action { get; } = action;
    public string Title => Strings.Get($"Hotkey{Action}");
    public string Combo { get => _combo; set => SetProperty(ref _combo, value); }
    public bool Conflict { get => _conflict; set => SetProperty(ref _conflict, value); }
    public bool Capturing { get => _capturing; set => SetProperty(ref _capturing, value); }
}

public sealed class HotkeysViewModel : IDisposable
{
    private readonly AppSettings _settings;
    private readonly HotkeyService _hotkeys;
    private readonly VoiceService _voice;
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _capture;

    public HotkeysViewModel(AppSettings settings, HotkeyService hotkeys, VoiceService voice)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        _voice = voice;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        foreach (var action in Enum.GetValues<HotkeyAction>()) Rows.Add(new HotkeyRow(action));
        _hotkeys.ConflictsChanged += OnConflicts;
        Refresh();
    }

    public ObservableCollection<HotkeyRow> Rows { get; } = new();

    public async Task ChangeAsync(HotkeyRow row)
    {
        _capture?.Cancel();
        _capture = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        row.Capturing = true;
        row.Combo = Strings.Get("HotkeyPress");
        try
        {
            var (modifiers, key) = await _voice.CaptureComboAsync(_capture.Token);
            if (key == 0) return;
            if (modifiers == 0)
            {
                row.Combo = Strings.Get("HotkeyNeedsModifier");
                await Task.Delay(1500);
                return;
            }
            var all = _settings.Hotkeys;
            var binding = new HotkeyBinding((HotkeyModifiers)modifiers, key);
            // One combination per action: a duplicate moves to the new action.
            foreach (var other in all.Where(kv => kv.Value == binding).Select(kv => kv.Key).ToList()) all[other] = null;
            all[row.Action] = binding;
            _settings.Hotkeys = all;
        }
        finally
        {
            row.Capturing = false;
            Refresh();
        }
    }

    public void Clear(HotkeyRow row)
    {
        var all = _settings.Hotkeys;
        all[row.Action] = null;
        _settings.Hotkeys = all;
        Refresh();
    }

    public void Reset()
    {
        _settings.Hotkeys = AppSettings.DefaultHotkeys();
        Refresh();
    }

    private void OnConflicts() => _dispatcher.TryEnqueue(Refresh);

    private void Refresh()
    {
        var all = _settings.Hotkeys;
        foreach (var row in Rows)
        {
            if (row.Capturing) continue;
            row.Combo = HotkeyService.Describe(all.GetValueOrDefault(row.Action));
            row.Conflict = _hotkeys.Conflicts.Contains(row.Action);
        }
    }

    public void Dispose()
    {
        _capture?.Cancel();
        _hotkeys.ConflictsChanged -= OnConflicts;
    }
}
