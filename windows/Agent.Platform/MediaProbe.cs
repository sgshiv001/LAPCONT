// LapCont — Platform — Real Media Foundation camera and H.264 software path
// License: MIT
using System.Diagnostics;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace LapCont.Platform;

/// <summary>Finite camera probe on a worker thread. COM objects and live samples are disposed without writing media.</summary>
public static class MediaProbe
{
    public static Task<object> CaptureAsync(CancellationToken ct) => Task.Run(() => Capture(ct), ct);

    public static object Inventory()
    {
        MFStartup().CheckError();
        try
        {
            using var cameras = MFEnumVideoDeviceSources();
            using var hardware = MFTEnumEx(TransformCategoryGuids.VideoEncoder, 0x44, null,
                new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 });
            using var software = MFTEnumEx(TransformCategoryGuids.VideoEncoder, 0x41, null,
                new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 });
            return new { status = "enumerated", cameras = cameras.Select(c => c.GetString(CaptureDeviceAttributeKeys.FriendlyName)).ToArray(),
                h264_hardware_encoders = hardware.Count(), h264_software_encoders = software.Count(), capture = "not_started" };
        }
        finally { MFShutdown().CheckError(); }
    }

    private static object Capture(CancellationToken ct)
    {
        MFStartup().CheckError();
        var stage = "camera_enumeration";
        try
        {
            using var cameras = MFEnumVideoDeviceSources();
            var selected = cameras.FirstOrDefault(c => !c.GetString(CaptureDeviceAttributeKeys.FriendlyName).Contains("IR", StringComparison.OrdinalIgnoreCase)) ?? cameras.FirstOrDefault();
            if (selected is null) return new { status = "CAMERA_UNAVAILABLE" };
            stage = "camera_activation";
            using var source = selected.ActivateObject<IMFMediaSource>();
            try
            {
                using var attributes = MFCreateAttributes(2);
                // The H.264 input is NV12; the legacy processing option only converts YUV to RGB32.
                attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u).CheckError();
                // This probe owns source.Shutdown. Prevent the reader's disposal from shutting it down first.
                attributes.Set(SourceReaderAttributeKeys.DisconnectMediasourceOnShutdown, 1u).CheckError();
                using var reader = MFCreateSourceReaderFromMediaSource(source, attributes);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
                IMFMediaType? best = null; long score = long.MaxValue;
                try
                {
                    stage = "native_mode_enumeration";
                    for (var index = 0; index < 128; index++)
                    {
                        IMFMediaType mode;
                        try { mode = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, index); }
                        catch (SharpGen.Runtime.SharpGenException e) when (e.HResult == unchecked((int)0xC00D36B9)) { break; }
                        var size = mode.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                        var rate = mode.GetUInt64(MediaTypeAttributeKeys.FrameRate);
                        var width = (int)(size >> 32); var height = (int)(size & uint.MaxValue);
                        var fps = (rate & uint.MaxValue) == 0 ? 0 : (double)(rate >> 32) / (rate & uint.MaxValue);
                        var candidate = Math.Abs(width - 1280) + Math.Abs(height - 720) + (fps < 15 ? 10000 : Math.Abs(fps - 30));
                        if (width > 0 && height > 0 && width <= 1920 && height <= 1080 && candidate < score)
                        { best?.Dispose(); best = mode; score = (long)candidate; }
                        else mode.Dispose();
                    }
                    if (best is null) return new { status = "CAMERA_MODE_UNAVAILABLE" };
                    stage = "native_mode_selection";
                    reader.SetCurrentMediaType((int)SourceReaderIndex.FirstVideoStream, best);
                    using var input = MFCreateMediaType();
                    input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
                    input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12).CheckError();
                    input.Set(MediaTypeAttributeKeys.FrameSize, best.GetUInt64(MediaTypeAttributeKeys.FrameSize)).CheckError();
                    input.Set(MediaTypeAttributeKeys.FrameRate, best.GetUInt64(MediaTypeAttributeKeys.FrameRate)).CheckError();
                    input.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1).CheckError();
                    input.Set(MediaTypeAttributeKeys.InterlaceMode, 2u).CheckError();
                    stage = "nv12_conversion";
                    reader.SetCurrentMediaType((int)SourceReaderIndex.FirstVideoStream, input);
                    using var encoders = MFTEnumEx(TransformCategoryGuids.VideoEncoder, 0x41, null,
                        new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 });
                    var activation = encoders.FirstOrDefault();
                    if (activation is null) return new { status = "H264_SOFTWARE_UNAVAILABLE" };
                    stage = "encoder_activation";
                    using var encoder = activation.ActivateObject<IMFTransform>();
                    using var output = MFCreateMediaType();
                    output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
                    output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264).CheckError();
                    output.Set(MediaTypeAttributeKeys.AvgBitrate, 2_000_000u).CheckError();
                    output.Set(MediaTypeAttributeKeys.FrameSize, input.GetUInt64(MediaTypeAttributeKeys.FrameSize)).CheckError();
                    output.Set(MediaTypeAttributeKeys.FrameRate, input.GetUInt64(MediaTypeAttributeKeys.FrameRate)).CheckError();
                    output.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1).CheckError();
                    output.Set(MediaTypeAttributeKeys.InterlaceMode, 2u).CheckError();
                    stage = "encoder_type_selection";
                    encoder.SetOutputType(0, output, 0); encoder.SetInputType(0, input, 0);
                    encoder.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
                    encoder.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
                    var captured = 0; var encoded = 0; long encodedBytes = 0;
                    var clock = Stopwatch.StartNew();
                    while (clock.Elapsed < TimeSpan.FromSeconds(3) && captured < 90)
                    {
                        ct.ThrowIfCancellationRequested();
                        stage = "camera_read";
                        using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, (SourceReaderControlFlag)0, out _, out var flags, out var timestamp);
                        if (sample is null) continue;
                        sample.SampleTime = timestamp; sample.SampleDuration = 333333;
                        stage = "encoder_input_output";
                        encoder.ProcessInput(0, sample, 0); captured++;
                        while (TryOutput(encoder, out var bytes)) { encoded++; encodedBytes += bytes; }
                    }
                    encoder.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
                    encoder.ProcessMessage(TMessageType.MessageCommandDrain, UIntPtr.Zero);
                    while (TryOutput(encoder, out var bytes)) { encoded++; encodedBytes += bytes; }
                    var negotiated = input.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                    return new { status = encoded > 0 ? "live_capture_software_encode_verified" : "NO_ENCODED_OUTPUT",
                        width = (uint)(negotiated >> 32), height = (uint)negotiated, captured_samples = captured,
                        encoded_access_units = encoded, encoded_bytes_discarded = encodedBytes, media_saved = false,
                        hardware_encoding = "not_exercised", duration_seconds = clock.Elapsed.TotalSeconds };
                }
                finally { best?.Dispose(); }
            }
            finally
            {
                try { source.Shutdown(); }
                // A failed reader can already have shut down the source. Do not mask its original error.
                catch (SharpGen.Runtime.SharpGenException error) when (error.HResult == unchecked((int)0xC00D3E85)) { }
            }
        }
        catch (Exception error) { error.Data["probe_stage"] = stage; throw; }
        finally { MFShutdown().CheckError(); }
    }
    private static bool TryOutput(IMFTransform encoder, out int length)
    {
        var info = encoder.GetOutputStreamInfo(0);
        using var sample = MFCreateSample();
        // A transform's working buffer can exceed the encoded wire-payload limit (for example 720p NV12).
        // Honor its required size, with an independent allocation bound for the supported <=1080p probe.
        if (info.Size < 0 || info.Size > 8 * 1024 * 1024) throw new InvalidDataException("Encoder output buffer requirement exceeds the probe limit");
        using var buffer = MFCreateMemoryBuffer(Math.Max(info.Size, 1024));
        sample.AddBuffer(buffer);
        var data = new OutputDataBuffer { StreamID = 0, Sample = sample };
        var result = encoder.ProcessOutput((ProcessOutputFlags)0, 1, ref data, out _);
        data.Events?.Dispose();
        if (result.Code == unchecked((int)0xC00D6D72)) { length = 0; return false; } // MF_E_TRANSFORM_NEED_MORE_INPUT
        result.CheckError();
        length = data.Sample.TotalLength;
        if (data.Sample.NativePointer != sample.NativePointer) data.Sample.Dispose();
        return true;
    }
}
