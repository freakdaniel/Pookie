#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.Audio;

// A synchronous, in-place PCM effect. Media Engine's SetVolume only attenuates;
// the transform supplies the additional gain before the renderer's 0..1 control.
[GeneratedComClass]
internal sealed unsafe partial class PlayReadyVolumeTransform : IVolumeTransform, IAgileObject, IDisposable
{
    private const int TypeNotSet = unchecked((int)0xc00d6d60), NeedInput = unchecked((int)0xc00d6d72);
    private const int InvalidType = unchecked((int)0xc00d36b4), NoTypes = unchecked((int)0xc00d36b9);
    private readonly object gate = new();
    private nint inputType, outputType, sample;
    private bool floating = true;
    private float gain = 1;
    private int receivedSamples;
    internal float Gain { set => Volatile.Write(ref gain, value); }
    internal bool IsProcessing => Volatile.Read(ref receivedSamples) != 0;
    private static readonly Guid Major = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid Audio = new("73647561-0000-0010-8000-00aa00389b71"), Float = new("00000003-0000-0010-8000-00aa00389b71"), Pcm = new("00000001-0000-0010-8000-00aa00389b71");
    [StructLayout(LayoutKind.Sequential)] private struct InputInfo { public long Latency; public uint Flags, Size, Lookahead, Alignment; }
    [StructLayout(LayoutKind.Sequential)] private struct OutputInfo { public uint Flags, Size, Alignment; }
    [StructLayout(LayoutKind.Sequential)] private struct OutputBuffer { public uint Stream; public nint Sample; public uint Status; public nint Events; }
    public int GetStreamLimits(out uint a, out uint b, out uint c, out uint d) { a = b = c = d = 1; return 0; }
    public int GetStreamCount(out uint inputs, out uint outputs) { inputs = outputs = 1; return 0; }
    public int GetStreamIDs(uint a, nint b, uint c, nint d) => NotImplemented;
    public int GetInputStreamInfo(uint id, nint info) { if (id != 0 || info == 0) return InvalidArgument; *(InputInfo*)info = new() { Flags = 1 | 2 | 8 | 0x800 }; return 0; }
    public int GetOutputStreamInfo(uint id, nint info) { if (id != 0 || info == 0) return InvalidArgument; *(OutputInfo*)info = new() { Flags = 1 | 2 | 0x100 }; return 0; }
    public int GetAttributes(out nint value) { value = 0; return NotImplemented; }
    public int GetInputStreamAttributes(uint id, out nint value) { value = 0; return NotImplemented; }
    public int GetOutputStreamAttributes(uint id, out nint value) { value = 0; return NotImplemented; }
    public int DeleteInputStream(uint id) => NotImplemented;
    public int AddInputStreams(uint count, nint ids) => NotImplemented;
    public int GetInputAvailableType(uint id, uint index, out nint type)
    {
        type = 0;
        if (id != 0) return InvalidArgument;
        if (index > 1) return NoTypes;
        var hr = MFCreateMediaType(out type);
        if (hr < 0) return hr;
        try { SetAttribute(type, Major, Audio); SetAttribute(type, Subtype, index == 0 ? Float : Pcm);
            SetAttribute(type, new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), index == 0 ? 32u : 16u); return 0; }
        catch (COMException error) { Release(ref type); return error.HResult; }
    }
    public int GetOutputAvailableType(uint id, uint index, out nint type)
    { lock (gate) { type = 0; if (id != 0) return InvalidArgument; if (index != 0) return NoTypes; if (inputType == 0) return TypeNotSet; type = inputType; Marshal.AddRef(type); return 0; } }
    public int SetInputType(uint id, nint type, uint flags)
    {
        lock (gate)
        {
            if (id != 0 || flags > 1) return InvalidArgument;
            if (sample != 0) return unchecked((int)0xc00d36b2);
            if (type != 0 && !ValidType(type, out _)) return InvalidType;
            if (type != 0 && outputType != 0 && !SameType(type, outputType)) return InvalidType;
            if (flags == 0) { Release(ref inputType); inputType = type; if (type != 0) { Marshal.AddRef(type); ValidType(type, out floating); } }
            return 0;
        }
    }
    public int SetOutputType(uint id, nint type, uint flags)
    {
        lock (gate)
        {
            if (id != 0 || flags > 1) return InvalidArgument;
            if (type != 0 && (!ValidType(type, out _) || inputType != 0 && !SameType(type, inputType))) return InvalidType;
            if (flags == 0) { Release(ref outputType); outputType = type; if (type != 0) Marshal.AddRef(type); }
            return 0;
        }
    }
    public int GetInputCurrentType(uint id, out nint type) { lock (gate) { type = inputType; return CurrentType(id, ref type); } }
    public int GetOutputCurrentType(uint id, out nint type) { lock (gate) { type = outputType; return CurrentType(id, ref type); } }
    private static int CurrentType(uint id, ref nint type) { if (id != 0) { type = 0; return InvalidArgument; } if (type == 0) return TypeNotSet; Marshal.AddRef(type); return 0; }
    public int GetInputStatus(uint id, out uint flags) { lock (gate) { flags = sample == 0 ? 1u : 0; return id == 0 ? 0 : InvalidArgument; } }
    public int GetOutputStatus(out uint flags) { lock (gate) { flags = sample != 0 ? 1u : 0; return 0; } }
    public int SetOutputBounds(long lower, long upper) => NotImplemented;
    public int ProcessEvent(uint id, nint value) => NotImplemented;
    public int ProcessMessage(uint message, nuint parameter) { lock (gate) { if (message == 0) Release(ref sample); return 0; } }
    public int ProcessInput(uint id, nint value, uint flags)
    {
        lock (gate)
        {
            if (id != 0 || flags != 0 || value == 0) return InvalidArgument;
            if (inputType == 0 || outputType == 0) return TypeNotSet;
            if (sample != 0) return unchecked((int)0xc00d36b0);
            sample = value; Marshal.AddRef(sample); Volatile.Write(ref receivedSamples, 1); return 0;
        }
    }
    public int ProcessOutput(uint flags, uint count, nint buffers, out uint status)
    {
        lock (gate)
        {
            status = 0;
            if (flags != 0 || count != 1 || buffers == 0) return InvalidArgument;
            if (sample == 0) return NeedInput;
            try
            {
                var factor = Volatile.Read(ref gain);
                if (factor > 1)
                {
                    nint buffer;
                    Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(sample, 41))(sample, &buffer), "IMFSample.ConvertToContiguousBuffer");
                    using var owned = new ComReference(buffer);
                    byte* data; uint capacity, length;
                    Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &capacity, &length), "IMFMediaBuffer.Lock");
                    try
                    {
                        if (floating)
                        {
                            var samples = new Span<float>(data, checked((int)length / 4));
                            for (var i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i] * factor, -1, 1);
                        }
                        else
                        {
                            var samples = new Span<short>(data, checked((int)length / 2));
                            for (var i = 0; i < samples.Length; i++) samples[i] = (short)Math.Clamp(samples[i] * factor, short.MinValue, short.MaxValue);
                        }
                    }
                    finally { Check(Call(buffer, 4), "IMFMediaBuffer.Unlock"); }
                }
                var output = (OutputBuffer*)buffers;
                output->Sample = sample; output->Status = 0; output->Events = 0;
                sample = 0; // The Media Engine now owns the sample reference.
                return 0;
            }
            catch (Exception error) { Release(ref sample); return error.HResult; }
        }
    }
    private static bool ValidType(nint type, out bool floating)
    {
        floating = false;
        if (GetGuid(type, Major, out var major) < 0 || major != Audio || GetGuid(type, Subtype, out var subtype) < 0) return false;
        var bitsKey = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"); uint bits;
        if (((delegate* unmanaged[Stdcall]<nint, Guid*, uint*, int>)Slot(type, 7))(type, &bitsKey, &bits) < 0) return false;
        floating = subtype == Float;
        return floating ? bits == 32 : subtype == Pcm && bits == 16;
    }
    private static bool SameType(nint a, nint b)
    { uint flags; return ((delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)Slot(a, 35))(a, b, &flags) == 0; }
    private static int GetGuid(nint type, Guid key, out Guid value)
    { value = default; fixed (Guid* output = &value) return ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(type, 10))(type, &key, output); }
    private static void Release(ref nint value) { if (value != 0) { Marshal.Release(value); value = 0; } }
    public void Dispose() { lock (gate) { Release(ref sample); Release(ref inputType); Release(ref outputType); } }
    [LibraryImport("mfplat.dll")] private static partial int MFCreateMediaType(out nint type);
}

[GeneratedComInterface, Guid("bf94c121-5b05-4e6f-8000-ba598961414d")]
internal partial interface IVolumeTransform
{
    [PreserveSig] int GetStreamLimits(out uint a, out uint b, out uint c, out uint d);
    [PreserveSig] int GetStreamCount(out uint inputs, out uint outputs);
    [PreserveSig] int GetStreamIDs(uint a, nint b, uint c, nint d);
    [PreserveSig] int GetInputStreamInfo(uint id, nint info);
    [PreserveSig] int GetOutputStreamInfo(uint id, nint info);
    [PreserveSig] int GetAttributes(out nint attributes);
    [PreserveSig] int GetInputStreamAttributes(uint id, out nint attributes);
    [PreserveSig] int GetOutputStreamAttributes(uint id, out nint attributes);
    [PreserveSig] int DeleteInputStream(uint id);
    [PreserveSig] int AddInputStreams(uint count, nint ids);
    [PreserveSig] int GetInputAvailableType(uint id, uint index, out nint type);
    [PreserveSig] int GetOutputAvailableType(uint id, uint index, out nint type);
    [PreserveSig] int SetInputType(uint id, nint type, uint flags);
    [PreserveSig] int SetOutputType(uint id, nint type, uint flags);
    [PreserveSig] int GetInputCurrentType(uint id, out nint type);
    [PreserveSig] int GetOutputCurrentType(uint id, out nint type);
    [PreserveSig] int GetInputStatus(uint id, out uint flags);
    [PreserveSig] int GetOutputStatus(out uint flags);
    [PreserveSig] int SetOutputBounds(long lower, long upper);
    [PreserveSig] int ProcessEvent(uint id, nint value);
    [PreserveSig] int ProcessMessage(uint message, nuint parameter);
    [PreserveSig] int ProcessInput(uint id, nint value, uint flags);
    [PreserveSig] int ProcessOutput(uint flags, uint count, nint buffers, out uint status);
}
#endif
