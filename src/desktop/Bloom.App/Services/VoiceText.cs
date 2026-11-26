using Bloom.Core.Settings;
using Bloom.Core.Voice;
using Bloom.Platform.Windows.Input;

namespace Bloom.App.Services;

/// <summary>Display texts and checks shared by the Voice page, the overview and the tray.</summary>
public static class VoiceText
{
    public static string StateKey(PttState state) => state switch
    {
        PttState.Ready => "PttStateReady",
        PttState.Transmitting => "PttStateTransmitting",
        PttState.Open => "PttStateOpen",
        PttState.Error => "PttStateError",
        _ => "PttStateOff",
    };

    public static string FailureKey(MuteFailure failure) => failure switch
    {
        MuteFailure.NotCalibrated => "PttFailNotCalibrated",
        MuteFailure.RobloxWindowNotFound => "PttFailNoWindow",
        MuteFailure.CouldNotFocus => "PttFailFocus",
        _ => "PttFailMouseLocked",
    };

    public static string KeyName(int vk) => vk switch
    {
        0 => Strings.Get("PttNoKey"),
        0x01 => Strings.Get("KeyMouseLeft"),
        0x02 => Strings.Get("KeyMouseRight"),
        0x04 => Strings.Get("KeyMouseMiddle"),
        0x05 => Strings.Get("KeyMouse4"),
        0x06 => Strings.Get("KeyMouse5"),
        _ => WindowControl.KeyboardKeyName(vk) ?? $"0x{vk:X2}",
    };

    /// <summary>Keys Roblox games commonly use: movement, jump, chat, camera.</summary>
    public static bool IsConflictKey(int vk) => vk is 0x57 or 0x41 or 0x53 or 0x44 or 0x20 or 0x10 or 0x11 or 0x0D or 0x09 or 0x02 or 0xBF or 0x1B;

    /// <summary>PTT can be switched on directly (tray) only when the setup on the Voice page is done.</summary>
    public static bool ReadyToEnable(AppSettings settings) => settings.PttMethod switch
    {
        PttMethod.SystemWide => settings.PttKey != 0 && settings.PttSystemWideConfirmed,
        PttMethod.RobloxButton => settings.PttButton is not null,
        _ => settings.PttKey != 0,
    };
}
