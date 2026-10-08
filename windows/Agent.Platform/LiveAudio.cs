// LapCont — Platform — Continuous WASAPI Opus capture and bounded talk playback
// License: MIT
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpusSharp.Core;

namespace LapCont.Platform;

/// <summary>One callback owns capture conversion/encoder; cancellation and errors always release hardware.</summary>
public static class LiveAudio
{
    public static async Task CaptureAsync(Action<byte[]> emit, CancellationToken ct)
    {
        using var capture = new WasapiCapture();
        var buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromMilliseconds(200), DiscardOnBufferOverflow = false, ReadFully = false };
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples) { LeftVolume = .5f, RightVolume = .5f };
        if (samples.WaveFormat.Channels != 1) throw new NotSupportedException("Unsupported microphone channels");
        samples = new WdlResamplingSampleProvider(samples, 48000);
        using var encoder = new OpusEncoder(48000, 1, OpusPredefinedValues.OPUS_APPLICATION_VOIP); encoder.Ctl(EncoderCTL.OPUS_SET_BITRATE, 64000);
        var floats = new float[960]; var pcm = new short[960]; var packet = new byte[1275]; var partial = 0;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, e) =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                while (buffer.BufferedDuration >= TimeSpan.FromMilliseconds(25))
                {
                    partial += samples.Read(floats, partial, 960 - partial); if (partial != 960) break;
                    for (var i = 0; i < 960; i++) pcm[i] = (short)Math.Clamp(floats[i] * 32767, short.MinValue, short.MaxValue);
                    var size = encoder.Encode(pcm, 960, packet, packet.Length); emit(packet[..size]); partial = 0;
                }
            }
            catch (Exception error) { stopped.TrySetException(error); capture.StopRecording(); }
        };
        capture.RecordingStopped += (_, e) => { if (e.Exception is not null) stopped.TrySetException(e.Exception); else stopped.TrySetResult(); };
        capture.StartRecording();
        try { await stopped.Task.WaitAsync(ct); }
        finally { capture.StopRecording(); try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { } }
    }
}

/// <summary>Serialized Opus decoder/playback. Buffer holds at most 160 ms; Stop disposes the chosen WASAPI route.</summary>
public sealed class TalkPlayer : IDisposable
{
    private readonly OpusDecoder decoder = new(48000, 1);
    private readonly BufferedWaveProvider buffer = new(new WaveFormat(48000, 16, 1)) { BufferDuration = TimeSpan.FromMilliseconds(160), DiscardOnBufferOverflow = false };
    private readonly MMDevice device;
    private readonly WasapiOut output;
    public TalkPlayer(string? name)
    {
        using var enumerator = new MMDeviceEnumerator();
        device = name is null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault(d => d.FriendlyName == name) ?? throw new InvalidOperationException("SPEAKER_UNAVAILABLE");
        output = new(device, AudioClientShareMode.Shared, false, 80); output.Init(buffer); output.Play();
    }
    public void Packet(byte[] bytes)
    {
        if (bytes.Length is < 1 or > 1275) throw new InvalidDataException("Invalid Opus packet");
        var pcm = new short[960]; if (decoder.Decode(bytes, bytes.Length, pcm, 960, false) != 960) throw new InvalidDataException("Invalid Opus duration");
        var raw = new byte[1920]; Buffer.BlockCopy(pcm, 0, raw, 0, raw.Length);
        // Network bursts can outpace WASAPI briefly. Discard stale queued speech to
        // retain the latency bound instead of throwing and closing both IPC channels.
        if (buffer.BufferedBytes + raw.Length > buffer.BufferLength) buffer.ClearBuffer();
        buffer.AddSamples(raw, 0, raw.Length);
    }
    public void Dispose() { output.Stop(); output.Dispose(); device.Dispose(); decoder.Dispose(); }
}
