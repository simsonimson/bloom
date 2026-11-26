using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows.Audio;

public enum MicTestPhase { Recording, Playing, Done }

/// <summary>
/// Records a few seconds from a microphone (shared-mode WASAPI) and plays them back on the
/// default output device (Blueprint V8). Nothing is written to disk.
/// </summary>
public static class MicTest
{
    private const int SharedMode = 0;
    private const uint AutoConvertPcm = 0x80000000;
    private const uint SrcDefaultQuality = 0x08000000;
    private const uint BufferFlagsSilent = 0x2;
    private const long OneSecond = 10_000_000; // REFERENCE_TIME units (100 ns)
    private const uint ClsCtxAll = 0x17;

    public static Task RunAsync(string? captureDeviceId, TimeSpan duration, Action<MicTestPhase, double> progress, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Run(captureDeviceId, duration, progress, ct);
                tcs.SetResult();
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(ct);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }) { IsBackground = true, Name = "MicTest" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return tcs.Task;
    }

    private static void Run(string? captureDeviceId, TimeSpan duration, Action<MicTestPhase, double> progress, CancellationToken ct)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDevice capture;
        if (captureDeviceId is not null) Hr.Check(enumerator.GetDevice(captureDeviceId, out capture), "GetDevice");
        else Hr.Check(enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Console, out capture), "GetDefaultAudioEndpoint(capture)");

        var captureClient = ActivateClient(capture);
        Hr.Check(captureClient.GetMixFormat(out var format), "GetMixFormat");
        try
        {
            var blockAlign = Marshal.ReadInt16(format, 12);
            var sampleRate = Marshal.ReadInt32(format, 4);
            var recorded = Record(captureClient, format, blockAlign, sampleRate, duration, progress, ct);

            Hr.Check(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var render), "GetDefaultAudioEndpoint(render)");
            Play(ActivateClient(render), format, blockAlign, recorded, progress, ct);
            progress(MicTestPhase.Done, 1);
        }
        finally
        {
            Marshal.FreeCoTaskMem(format);
        }
    }

    private static IAudioClient ActivateClient(IMMDevice device)
    {
        var iid = typeof(IAudioClient).GUID;
        Hr.Check(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var client), "Activate(IAudioClient)");
        return (IAudioClient)client;
    }

    private static byte[] Record(IAudioClient client, IntPtr format, int blockAlign, int sampleRate, TimeSpan duration,
        Action<MicTestPhase, double> progress, CancellationToken ct)
    {
        Hr.Check(client.Initialize(SharedMode, 0, OneSecond, 0, format, IntPtr.Zero), "Initialize(capture)");
        var iid = typeof(IAudioCaptureClient).GUID;
        Hr.Check(client.GetService(ref iid, out var service), "GetService(capture)");
        var capture = (IAudioCaptureClient)service;

        var totalFrames = (long)(sampleRate * duration.TotalSeconds);
        using var buffer = new MemoryStream((int)(totalFrames * blockAlign));
        long frames = 0;
        Hr.Check(client.Start(), "Start(capture)");
        try
        {
            while (frames < totalFrames)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(10);
                while (capture.GetNextPacketSize(out var packet) >= 0 && packet > 0)
                {
                    Hr.Check(capture.GetBuffer(out var data, out var count, out var flags, out _, out _), "GetBuffer(capture)");
                    var bytes = (int)count * blockAlign;
                    var chunk = new byte[bytes];
                    if ((flags & BufferFlagsSilent) == 0) Marshal.Copy(data, chunk, 0, bytes);
                    buffer.Write(chunk);
                    capture.ReleaseBuffer(count);
                    frames += count;
                }
                progress(MicTestPhase.Recording, Math.Min(1, (double)frames / totalFrames));
            }
        }
        finally
        {
            client.Stop();
        }
        return buffer.ToArray();
    }

    private static void Play(IAudioClient client, IntPtr format, int blockAlign, byte[] audio,
        Action<MicTestPhase, double> progress, CancellationToken ct)
    {
        // AUTOCONVERTPCM lets the shared engine convert the microphone format to the speaker format.
        Hr.Check(client.Initialize(SharedMode, AutoConvertPcm | SrcDefaultQuality, OneSecond, 0, format, IntPtr.Zero), "Initialize(render)");
        Hr.Check(client.GetBufferSize(out var bufferFrames), "GetBufferSize");
        var iid = typeof(IAudioRenderClient).GUID;
        Hr.Check(client.GetService(ref iid, out var service), "GetService(render)");
        var render = (IAudioRenderClient)service;

        var totalFrames = audio.Length / blockAlign;
        var written = 0;
        void Fill()
        {
            Hr.Check(client.GetCurrentPadding(out var padding), "GetCurrentPadding");
            var free = (int)(bufferFrames - padding);
            var count = Math.Min(free, totalFrames - written);
            if (count <= 0) return;
            Hr.Check(render.GetBuffer((uint)count, out var data), "GetBuffer(render)");
            Marshal.Copy(audio, written * blockAlign, data, count * blockAlign);
            render.ReleaseBuffer((uint)count, 0);
            written += count;
        }

        Fill();
        Hr.Check(client.Start(), "Start(render)");
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(10);
                Fill();
                client.GetCurrentPadding(out var padding);
                progress(MicTestPhase.Playing, totalFrames == 0 ? 1 : Math.Min(1, (double)(written - padding) / totalFrames));
                if (written >= totalFrames && padding == 0) break;
            }
        }
        finally
        {
            client.Stop();
        }
    }
}
