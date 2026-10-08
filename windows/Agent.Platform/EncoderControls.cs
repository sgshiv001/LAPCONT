// LapCont — Platform — Native ICodecAPI latency/GOP controls (Windows SDK ABI)
// License: MIT
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;
namespace LapCont.Platform;

/// <summary>Worker-owned ICodecAPI reference. Only supported uint/bool VARIANT controls; no COM automation on UI.</summary>
internal sealed unsafe class EncoderControls : IDisposable
{
    private IntPtr pointer;
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public uint Value; }
    private static readonly Guid LowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    private static readonly Guid Gop = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    private static readonly Guid BFrames = new("8d390aac-dc5c-4200-b57f-814d04babab2");
    private static readonly Guid ForceKeyframe = new("398c1b98-8353-475a-9ef2-8f265d260345");
    public EncoderControls(IMFTransform encoder)
    {
        var iid = new Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"); Marshal.ThrowExceptionForHR(Marshal.QueryInterface(encoder.NativePointer, ref iid, out pointer));
        try { Set(LowLatency, 11, 0xffff); Set(Gop, 19, 60); Set(BFrames, 19, 0); } catch { Dispose(); throw; }
    }
    private void Set(Guid property, ushort type, uint value)
    {
        var variant = new Variant { Type = type, Value = value }; var table = *(IntPtr**)pointer;
        var call = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, Variant*, int>)table[9];
        Marshal.ThrowExceptionForHR(call(pointer, &property, &variant));
    }
    public void Keyframe() => Set(ForceKeyframe, 19, 1);
    public void Dispose() { if (pointer != IntPtr.Zero) { Marshal.Release(pointer); pointer = IntPtr.Zero; } }
}
