// LapCont — Platform — Continuous real camera to bounded H.264 access units
// License: MIT
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace LapCont.Platform;

/// <summary>Worker-owned synchronous software MFT fallback. Cancellation shuts down its source; no captured media reaches disk.</summary>
public static class LiveCamera
{
    public static Task RunAsync(int width, int height, int bitrate, Action<byte[], bool> output, CancellationToken ct, Action<string>? selected = null, Func<bool>? requestKeyframe = null) => Task.Run(() => Run(width, height, bitrate, output, ct, selected, requestKeyframe), ct);
    private static void Run(int width, int height, int bitrate, Action<byte[], bool> emit, CancellationToken ct, Action<string>? encoderSelected, Func<bool>? requestKeyframe)
    {
        MFStartup().CheckError();
        try
        {
            using var cameras = MFEnumVideoDeviceSources();
            var selected = cameras.FirstOrDefault(c => !c.GetString(CaptureDeviceAttributeKeys.FriendlyName).Contains("IR", StringComparison.OrdinalIgnoreCase)) ?? cameras.FirstOrDefault()
                ?? throw new InvalidOperationException("CAMERA_UNAVAILABLE");
            using var source = selected.ActivateObject<IMFMediaSource>();
            using var interrupt = ct.Register(() => { try { source.Shutdown(); } catch (SharpGen.Runtime.SharpGenException) { } });
            try
            {
                using var attributes = MFCreateAttributes(2); attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u).CheckError(); attributes.Set(SourceReaderAttributeKeys.DisconnectMediasourceOnShutdown, 1u).CheckError();
                using var reader = MFCreateSourceReaderFromMediaSource(source, attributes);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false); reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
                IMFMediaType? native = null;
                try
                {
                    for (var i = 0; i < 128; i++)
                    {
                        IMFMediaType mode;
                        try { mode = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, i); }
                        catch (SharpGen.Runtime.SharpGenException e) when (e.HResult == unchecked((int)0xC00D36B9)) { break; }
                        var size = mode.GetUInt64(MediaTypeAttributeKeys.FrameSize); var fps = mode.GetUInt64(MediaTypeAttributeKeys.FrameRate);
                        if ((int)(size >> 32) == width && (int)(uint)size == height && (uint)fps != 0 && (fps >> 32) / (double)(uint)fps >= 29) { native = mode; break; }
                        mode.Dispose();
                    }
                    if (native is null) throw new InvalidOperationException("CAMERA_PRESET_UNAVAILABLE");
                    reader.SetCurrentMediaType((int)SourceReaderIndex.FirstVideoStream, native);
                    using var input = MFCreateMediaType(); SetType(input, width, height, VideoFormatGuids.NV12); reader.SetCurrentMediaType((int)SourceReaderIndex.FirstVideoStream, input);
                    using var encoded = MFCreateMediaType(); SetType(encoded, width, height, VideoFormatGuids.H264); encoded.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate).CheckError();
                    var encoder = H264Encoder.Create(input,encoded,ct); encoderSelected?.Invoke(encoder.Path);
                    var frameCount = 0;
                    var emitted = 0;
                    byte[] configuration=Array.Empty<byte>();
                    void Drain()
                    {
                        while(encoder.CanOutput() && TryOutput(encoder.Transform,out var bytes))
                        {
                            bytes=H264.AnnexB(bytes); var config=H264.Configuration(bytes); if(config.Length>0) configuration=config;
                            if(configuration.Length==0) { using var type=encoder.Transform.GetOutputCurrentType(0); try { configuration=H264.CodecHeader(type.GetBlob(MediaTypeAttributeKeys.MpegSequenceHeader)); } catch(SharpGen.Runtime.SharpGenException e) when(e.HResult==unchecked((int)0xC00D36E6)) { } }
                            var key=H264.Contains(bytes,5); if(key && !H264.Contains(bytes,7) && configuration.Length>0) bytes=configuration.Concat(bytes).ToArray();
                            if(bytes.Length>1048576) throw new InvalidDataException("Access unit exceeds wire bound"); emit(bytes,key); emitted++;
                        }
                    }
                    try { while (!ct.IsCancellationRequested)
                    {
                        using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, (SourceReaderControlFlag)0, out _, out var flags, out var timestamp);
                        if (sample is null) { if (flags.HasFlag(SourceReaderFlag.EndOfStream)) throw new IOException("Camera ended"); continue; }
                        sample.SampleTime = timestamp; sample.SampleDuration = 333333;
                        var recovery = requestKeyframe?.Invoke()==true;
                        try { encoder.Feed(sample,frameCount++ % 60==0 || recovery,ct,Drain); Drain(); }
                        catch(Exception e) when(!ct.IsCancellationRequested && emitted==0 && encoder.Path.Contains("hardware") && e is SharpGen.Runtime.SharpGenException or TimeoutException)
                        {
                            encoder.Dispose(); encoder=H264Encoder.Create(input,encoded,ct,false); encoderSelected?.Invoke(encoder.Path); frameCount=1; configuration=Array.Empty<byte>();
                            encoder.Feed(sample,true,ct,Drain); Drain();
                        }
                    } } finally { encoder.Dispose(); }
                }
                finally { native?.Dispose(); }
            }
            catch (SharpGen.Runtime.SharpGenException) when (ct.IsCancellationRequested) { }
            finally { try { source.Shutdown(); } catch (SharpGen.Runtime.SharpGenException e) when (e.HResult == unchecked((int)0xC00D3E85)) { } }
        }
        finally { MFShutdown().CheckError(); }
    }
    private static void SetType(IMFMediaType type, int w, int h, Guid subtype)
    {
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError(); type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
        type.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)w << 32) | (uint)h).CheckError(); type.Set(MediaTypeAttributeKeys.FrameRate, (30UL << 32) | 1).CheckError();
        type.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1).CheckError(); type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u).CheckError();
    }
    private static bool TryOutput(IMFTransform encoder, out byte[] bytes)
    {
        var info = encoder.GetOutputStreamInfo(0); if (info.Size is < 0 or > 8388608) throw new InvalidDataException("Encoder working-buffer bound exceeded");
        using var sample = MFCreateSample(); using var buffer = MFCreateMemoryBuffer(Math.Max(info.Size, 1024)); sample.AddBuffer(buffer);
        var data = new OutputDataBuffer { StreamID = 0, Sample = ((int)info.Flags & 0x100)!=0 ? null! : sample }; var result = encoder.ProcessOutput((ProcessOutputFlags)0, 1, ref data, out _); data.Events?.Dispose();
        if(result.Code==unchecked((int)0xC00D6D61)) { using var changed=encoder.GetOutputAvailableType(0,0); encoder.SetOutputType(0,changed,0); bytes=Array.Empty<byte>(); return false; }
        if (result.Code == unchecked((int)0xC00D6D72)) { bytes = Array.Empty<byte>(); return false; } result.CheckError();
        try
        {
            if (data.Sample.TotalLength is <= 0 or > 1048576) throw new InvalidDataException("Access unit exceeds wire bound");
            using var contiguous = data.Sample.ConvertToContiguousBuffer(); contiguous.Lock(out var pointer, out _, out var length);
            try { bytes = new byte[length]; Marshal.Copy(pointer, bytes, 0, length); } finally { contiguous.Unlock(); }
            return true;
        }
        finally { if (data.Sample.NativePointer != sample.NativePointer) data.Sample.Dispose(); }
    }
}

/// <summary>Stateless bounded Annex B parser used for SPS/PPS/IDR dependency management.</summary>
public static class H264
{
    public static byte[] AnnexB(byte[] bytes)
    {
        if(bytes.Length>=4 && bytes[0]==0 && bytes[1]==0 && (bytes[2]==1 || bytes[2]==0 && bytes[3]==1)) return bytes;
        using var output=new MemoryStream(); var offset=0;
        while(offset<bytes.Length) { if(bytes.Length-offset<4) throw new InvalidDataException("Invalid length-prefixed NAL"); var size=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset,4)); offset+=4; if(size<=0 || size>bytes.Length-offset) throw new InvalidDataException("Invalid NAL length"); output.Write(new byte[]{0,0,0,1}); output.Write(bytes,offset,size); offset+=size; }
        return output.ToArray();
    }
    public static byte[] CodecHeader(byte[] bytes)
    {
        if(bytes.Length<7 || bytes[0]!=1) return Configuration(bytes);
        using var output=new MemoryStream(); var offset=6; var count=bytes[5]&31;
        for(var group=0;group<2;group++) { for(var i=0;i<count;i++) { if(offset+2>bytes.Length) throw new InvalidDataException("Invalid codec header"); var size=System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset,2)); offset+=2; if(size==0 || size>bytes.Length-offset) throw new InvalidDataException("Invalid codec header"); output.Write(new byte[]{0,0,0,1}); output.Write(bytes,offset,size); offset+=size; } if(group==0) { if(offset>=bytes.Length) throw new InvalidDataException("Missing PPS"); count=bytes[offset++]; } }
        return Configuration(output.ToArray());
    }
    public static IEnumerable<byte[]> Nals(byte[] bytes)
    {
        var starts = new List<(int Position, int Header)>();
        for (var i = 0; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] != 0 || bytes[i + 1] != 0) continue;
            if (bytes[i + 2] == 1) { starts.Add((i, i + 3)); i += 2; }
            else if (bytes[i + 2] == 0 && bytes[i + 3] == 1) { starts.Add((i, i + 4)); i += 3; }
        }
        for (var i = 0; i < starts.Count; i++) { var s = starts[i]; var end = i + 1 < starts.Count ? starts[i + 1].Position : bytes.Length; if (s.Header < end) yield return bytes[s.Header..end]; }
    }
    public static bool Contains(byte[] bytes, int type) => Nals(bytes).Any(n => (n[0] & 31) == type);
    public static byte[] Configuration(byte[] bytes) => Nals(bytes).Where(n => (n[0] & 31) is 7 or 8).SelectMany(n => new byte[] { 0, 0, 0, 1 }.Concat(n)).ToArray();
}
