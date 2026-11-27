using Bloom.Core.Events;
using Bloom.Core.Performance;
using Bloom.Core.Settings;
using Bloom.Platform.Windows.Input;
using Microsoft.Extensions.Logging;

namespace Bloom.App.Services;

/// <summary>System-wide bloom hotkeys (E6). Actions run on the UI thread through <see cref="Pressed"/>.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ILogger<HotkeyService> _logger;
    private readonly HotkeyManager _manager = new();
    private readonly IDisposable _subscription;

    public HotkeyService(AppSettings settings, IEventBus events, ILogger<HotkeyService> logger)
    {
        _settings = settings;
        _logger = logger;
        _manager.Pressed += id =>
        {
            if (Enum.IsDefined(typeof(HotkeyAction), id - 1)) events.Publish(new HotkeyPressed((HotkeyAction)(id - 1)));
        };
        _subscription = events.Subscribe<SettingChanged>(e => { if (e.Key == AppSettings.Keys.Hotkeys) _ = ApplyAsync(); });
    }

    /// <summary>Actions Windows refused because another program already uses the combination.</summary>
    public IReadOnlySet<HotkeyAction> Conflicts { get; private set; } = new HashSet<HotkeyAction>();

    public event Action? ConflictsChanged;

    public void Start()
    {
        _manager.Start();
        _ = ApplyAsync();
    }

    private async Task ApplyAsync()
    {
        var bindings = _settings.Hotkeys
            .Where(kv => kv.Value is { Key: > 0 } b && b.Modifiers != HotkeyModifiers.None)
            .ToDictionary(kv => (int)kv.Key + 1, kv => ((uint)kv.Value!.Modifiers, kv.Value.Key));
        var failed = await _manager.SetAsync(bindings);
        Conflicts = failed.Select(id => (HotkeyAction)(id - 1)).ToHashSet();
        if (Conflicts.Count > 0) _logger.LogInformation("Hotkeys in use by another program: {Actions}", string.Join(", ", Conflicts));
        ConflictsChanged?.Invoke();
    }

    public static string Describe(HotkeyBinding? binding)
    {
        if (binding is not { Key: > 0 }) return Strings.Get("HotkeyNone");
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add(Strings.Get("KeyCtrl"));
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add(Strings.Get("KeyShift"));
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VoiceText.KeyName(binding.Key));
        return string.Join(" + ", parts);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _manager.Dispose();
    }
}
