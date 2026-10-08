// LapCont — Platform — Hardware-preferred Media Foundation encoder with synchronous software fallback
// License: MIT
using System.Diagnostics;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;
namespace LapCont.Platform;

/// <summary>One worker owns MFT, ICodecAPI and optional asynchronous event generator. No unbounded event waits.</summary>
internal sealed class H264Encoder : IDisposable
{
    public IMFTransform Transform { get; }
    public string Path { get; }
    private EncoderControls? controls;
    private IMFMediaEventGenerator? events;
    private int inputs, outputs;
    private H264Encoder(IMFActivate activation, IMFMediaType input, IMFMediaType output, bool hardware, CancellationToken ct)
    {
        Transform=activation.ActivateObject<IMFTransform>(); Path=hardware ? "Media Foundation hardware" : "Media Foundation software fallback";
        try
        {
            using var attributes=Transform.Attributes;
            if(attributes.GetUInt32(TransformAttributeKeys.TransformAsync,out var asynchronous).Success && asynchronous!=0)
            {
                attributes.Set(TransformAttributeKeys.TransformAsyncUnlock,1u).CheckError(); events=Transform.QueryInterface<IMFMediaEventGenerator>();
            }
            controls=new EncoderControls(Transform);
            Transform.SetOutputType(0,output,0); Transform.SetInputType(0,input,0);
            Transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming,UIntPtr.Zero); Transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream,UIntPtr.Zero);
            if(events is not null) AwaitInput(ct);
        }
        catch { Dispose(); throw; }
    }
    public static H264Encoder Create(IMFMediaType input,IMFMediaType output,CancellationToken ct,bool preferHardware=true)
    {
        using var hardware=MFTEnumEx(TransformCategoryGuids.VideoEncoder,0x44,null,new RegisterTypeInfo { GuidMajorType=MediaTypeGuids.Video,GuidSubtype=VideoFormatGuids.H264 });
        foreach(var candidate in hardware.Where(_=>preferHardware)) { ct.ThrowIfCancellationRequested(); try { return new(candidate,input,output,true,ct); } catch(Exception e) when(e is SharpGen.Runtime.SharpGenException or System.Runtime.InteropServices.COMException or TimeoutException) { } }
        using var software=MFTEnumEx(TransformCategoryGuids.VideoEncoder,0x41,null,new RegisterTypeInfo { GuidMajorType=MediaTypeGuids.Video,GuidSubtype=VideoFormatGuids.H264 });
        return new(software.FirstOrDefault() ?? throw new InvalidOperationException("H264_UNAVAILABLE"),input,output,false,ct);
    }
    private void Poll()
    {
        if(events is null) return;
        for(var i=0;i<64;i++)
        {
            IMFMediaEvent next; try { next=events.GetEvent(1); } catch(SharpGen.Runtime.SharpGenException e) when(e.HResult==unchecked((int)0xC00D3E80)) { return; }
            using(next) { next.Status.CheckError(); if(next.EventType==MediaEventTypes.TransformNeedInput) inputs++; else if(next.EventType==MediaEventTypes.TransformHaveOutput) outputs++; }
        }
        throw new InvalidDataException("Encoder event budget exceeded");
    }
    private void AwaitInput(CancellationToken ct,Action? drain=null)
    {
        var wait=Stopwatch.StartNew(); while(inputs==0) { ct.ThrowIfCancellationRequested(); Poll(); if(outputs>0) drain?.Invoke(); Poll(); if(inputs>0) return; if(wait.Elapsed>TimeSpan.FromSeconds(2)) throw new TimeoutException("Encoder input unavailable"); Thread.Sleep(2); }
    }
    public void Feed(IMFSample sample,bool keyframe,CancellationToken ct,Action drain)
    {
        if(events is not null) { AwaitInput(ct,drain); inputs--; } if(keyframe) controls!.Keyframe(); Transform.ProcessInput(0,sample,0); Poll();
    }
    public bool CanOutput() { Poll(); if(events is null) return true; if(outputs==0) return false; outputs--; return true; }
    public void Dispose() { events?.Dispose(); controls?.Dispose(); Transform.Dispose(); }
}
