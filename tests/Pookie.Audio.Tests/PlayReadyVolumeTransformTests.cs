#if WINDOWS
using System.Runtime.InteropServices;
using Xunit;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.Audio.Tests;

public sealed unsafe class PlayReadyVolumeTransformTests
{
    [Theory]
    [InlineData(1f, .25f, .25f)]
    [InlineData(1.5f, .25f, .375f)]
    [InlineData(2f, .25f, .5f)]
    [InlineData(2f, .75f, 1f)]
    public void NativeTransformAmplifiesPcmAndPreservesSampleTiming(float gain, float input, float expected)
    {
        Check(CoInitializeEx(0, 0), "CoInitializeEx");
        Check(MFStartup(0x20070, 0), "MFStartup");
        try
        {
            Check(MFCreateMediaType(out var type), "MFCreateMediaType");
            using var mediaType = new ComReference(type);
            SetAttribute(type, new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), new Guid("73647561-0000-0010-8000-00aa00389b71"));
            SetAttribute(type, new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), new Guid("00000003-0000-0010-8000-00aa00389b71"));
            SetAttribute(type, new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), 32u);
            SetAttribute(type, new("37e48bf5-645e-4c5b-89de-ada9e29b696a"), 2u);
            SetAttribute(type, new("5faeeae7-0290-4c31-9e8a-c534f68d9dba"), 48000u);
            using var effect = new PlayReadyVolumeTransform { Gain = gain };
            using var exported = Export<IVolumeTransform>(effect);
            Check(effect.SetInputType(0, type, 0), "SetInputType");
            Check(effect.SetOutputType(0, type, 0), "SetOutputType");
            Check(MFCreateSample(out var sample), "MFCreateSample");
            using var original = new ComReference(sample);
            Check(MFCreateMemoryBuffer(16, out var buffer), "MFCreateMemoryBuffer");
            using var ownedBuffer = new ComReference(buffer);
            byte* data; uint capacity, length;
            Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &capacity, &length), "Lock");
            new Span<float>(data, 4).Fill(input);
            Check(Call(buffer, 4), "Unlock");
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(buffer, 6))(buffer, 16), "SetCurrentLength");
            Check(Call(sample, 42, buffer), "AddBuffer");
            Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(sample, 36))(sample, 123456), "SetSampleTime");
            // Exercise the generated COM ABI used by the native Media Engine.
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, uint, int>)Slot(exported.Value, 24))(exported.Value, 0, sample, 0), "ProcessInput");
            Assert.True(effect.IsProcessing);
            Output output = default; uint status;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, Output*, uint*, int>)Slot(exported.Value, 25))(
                exported.Value, 0, 1, &output, &status), "ProcessOutput");
            using var result = new ComReference(output.Sample);
            long time;
            Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Slot(output.Sample, 35))(output.Sample, &time), "GetSampleTime");
            Assert.Equal(123456, time);
            Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &capacity, &length), "Lock");
            try { foreach (var value in new ReadOnlySpan<float>(data, 4)) Assert.Equal(expected, value, 5); }
            finally { Check(Call(buffer, 4), "Unlock"); }
        }
        finally { try { Check(MFShutdown(), "MFShutdown"); } finally { CoUninitialize(); } }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Output { public uint Stream; public nint Sample; public uint Status; public nint Events; }
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out nint type);
    [DllImport("mfplat.dll")] private static extern int MFCreateSample(out nint sample);
    [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(uint length, out nint buffer);
}
#endif
