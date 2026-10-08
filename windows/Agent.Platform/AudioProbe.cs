// LapCont — Platform — WASAPI native format conversion and real libopus round trip
// License: MIT
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using OpusSharp.Core;

namespace LapCont.Platform;

/// <summary>Finite audio probes. Codec state is worker/callback-owned; raw audio remains transient and is discarded.</summary>
public static class AudioProbe
{
    /// <summary>Finite quiet generated tone through the real default WASAPI render endpoint. Audible output requires owner confirmation.</summary>
    public static Task<object> PlaybackAsync(CancellationToken ct, string? deviceName = null) => Task.Run(async () =>
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? selected = null;
        if (deviceName is null) selected = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        else
        {
            foreach (var candidate in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (selected is null && candidate.FriendlyName == deviceName) selected = candidate;
                else candidate.Dispose();
            }
        }
        using var device = selected ?? throw new InvalidOperationException("Requested speaker endpoint is unavailable");
        using var playback = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
        var tone = new SignalGenerator(48000, 1) { Frequency = 440, Gain = 0.05, Type = SignalGeneratorType.Sin };
        playback.Init(tone);
        playback.Play();
        try
        {
            await Task.Delay(1000, ct);
            if (playback.PlaybackState != PlaybackState.Playing) throw new InvalidDataException("WASAPI playback stopped before the finite probe completed");
        }
        finally { playback.Stop(); }
        return (object)new { status = "wasapi_playback_api_verified", input = "quiet_generated_440hz_tone", duration_seconds = 1,
            selected_device = device.FriendlyName, muted = device.AudioEndpointVolume.Mute,
            master_volume_percent = Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100),
            audible_output = "requires_owner_confirmation", media_saved = false };
    }, ct);

    public static unsafe object Synthetic()
    {
        using var encoder = new OpusEncoder(48000, 1, OpusPredefinedValues.OPUS_APPLICATION_VOIP);
        using var decoder = new OpusDecoder(48000, 1);
        encoder.Ctl(EncoderCTL.OPUS_SET_BITRATE, 64000);
        var packet = new byte[1275]; var length = encoder.Encode(new short[960], 960, packet, packet.Length);
        var samples = decoder.Decode(packet, length, new short[960], 960, false);
        if (samples != 960) throw new InvalidDataException("Opus did not return 960 samples");
        return new { status = "native_opus_roundtrip_verified", version = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)NativeOpus.opus_get_version_string()), input = "synthetic_silence", packet_bytes = length, decoded_samples = samples, media_saved = false };
    }
    public static Task<object> MicrophoneAsync(CancellationToken ct) => Task.Run(() => CaptureMicrophoneAsync(ct), ct);
    private static async Task<object> CaptureMicrophoneAsync(CancellationToken ct)
    {
        using var capture = new WasapiCapture();
        var format = capture.WaveFormat;
        var buffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(200), DiscardOnBufferOverflow = true, ReadFully = false };
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (samples.WaveFormat.Channels != 1) throw new NotSupportedException("Microphone channel count unsupported by this probe");
        samples = new WdlResamplingSampleProvider(samples, 48000);
        using var encoder = new OpusEncoder(48000, 1, OpusPredefinedValues.OPUS_APPLICATION_VOIP);
        using var decoder = new OpusDecoder(48000, 1);
        encoder.Ctl(EncoderCTL.OPUS_SET_BITRATE, 64000);
        var floats = new float[960]; var pcm = new short[960]; var packet = new byte[1275]; var decoded = new short[960];
        var packets = 0; var partial = 0;
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, e) =>
        {
            try
            {
                buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                while (buffer.BufferedDuration >= TimeSpan.FromMilliseconds(25))
                {
                    partial += samples.Read(floats, partial, 960 - partial);
                    if (partial != 960) break;
                    for (var i = 0; i < 960; i++) pcm[i] = (short)Math.Clamp(floats[i] * 32767, short.MinValue, short.MaxValue);
                    var size = encoder.Encode(pcm, 960, packet, packet.Length);
                    if (decoder.Decode(packet, size, decoded, 960, false) != 960) throw new InvalidDataException("Opus frame mismatch");
                    partial = 0; Interlocked.Increment(ref packets);
                }
            }
            catch (OpusException error) { failed.TrySetResult(error); }
            catch (InvalidDataException error) { failed.TrySetResult(error); }
        };
        capture.RecordingStopped += (_, e) => { if (e.Exception is not null) failed.TrySetResult(e.Exception); };
        capture.StartRecording();
        try
        {
            var end = Task.Delay(1500, ct); var result = await Task.WhenAny(end, failed.Task);
            if (result == failed.Task) throw await failed.Task;
            await end;
        }
        finally { capture.StopRecording(); }
        return new { status = packets > 0 ? "wasapi_capture_opus_verified" : "NO_AUDIO_FRAMES", native_format = format.ToString(),
            output_rate = 48000, output_channels = 1, opus_packets = packets, media_saved = false, playback = "not_exercised" };
    }
}
