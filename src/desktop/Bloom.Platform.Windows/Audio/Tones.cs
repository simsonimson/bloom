using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Audio;

/// <summary>Short click tones for PTT on/off (Blueprint V9). Generated in memory, played asynchronously.</summary>
public static class Tones
{
    private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;
    private static readonly Lazy<IntPtr> OnTone = new(() => Build(880, 55));
    private static readonly Lazy<IntPtr> OffTone = new(() => Build(587, 55));

    public static void PlayOn() => Play(OnTone.Value);
    public static void PlayOff() => Play(OffTone.Value);

    private static void Play(IntPtr wav)
    {
        try { PlaySoundW(wav, IntPtr.Zero, SND_ASYNC | SND_NODEFAULT | SND_MEMORY); }
        catch { /* sound is optional */ }
    }

    /// <summary>Builds a 16-bit mono WAV in unmanaged memory that stays valid for the process lifetime.</summary>
    internal static byte[] BuildWav(double frequency, int milliseconds)
    {
        const int rate = 44100;
        var samples = rate * milliseconds / 1000;
        var data = new byte[44 + samples * 2];
        using (var w = new BinaryWriter(new MemoryStream(data)))
        {
            w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(samples * 2);
            var fade = rate / 200; // 5 ms fade in/out against clicks
            for (var i = 0; i < samples; i++)
            {
                var envelope = Math.Min(1.0, Math.Min(i, samples - 1 - i) / (double)fade);
                w.Write((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * 0.25 * envelope * short.MaxValue));
            }
        }
        return data;
    }

    private static IntPtr Build(double frequency, int milliseconds)
    {
        var wav = BuildWav(frequency, milliseconds);
        var ptr = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, ptr, wav.Length);
        return ptr;
    }

    [DllImport("winmm.dll")]
    private static extern bool PlaySoundW(IntPtr sound, IntPtr module, uint flags);
}
