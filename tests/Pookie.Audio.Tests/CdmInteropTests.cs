using System.Runtime.InteropServices;
using Pookie.Audio;
using Xunit;

namespace Pookie.Audio.Tests;

public unsafe class CdmInteropTests
{
    // A synthetic CDM exercises both sides of the unmanaged boundary without
    // installing Widevine or obtaining real media keys in CI.
    [StructLayout(LayoutKind.Sequential)]
    private struct FakeObject { public nint Vtable, Host; public int Thread; }
    private static int destroyedOnThread;
    private static readonly nint table = CreateTable();

    [Fact]
    public void HostCallbacksBuffersTimersAndTeardownUseTheCdmThread()
    {
        var released = false;
        var instance = new NativeWidevine((nint)(delegate* unmanaged<int, byte*, uint, delegate* unmanaged<int, nint, nint>, nint, nint>)&Create,
            () => released = true);
        int cdmThread;
        using (instance)
        {
            var initialized = Next(instance, 1);
            Assert.Equal(0, initialized.Code);
            var timer = Next(instance, 3);
            cdmThread = timer.Code;
            Assert.NotEqual(Environment.CurrentManagedThreadId, cdmThread);
            GC.Collect(); GC.WaitForPendingFinalizers();
            instance.Begin(42, [1]);
            Assert.Equal(42u, Next(instance, 3).Promise);
            Assert.Equal(new byte[] { 2, 3 }, instance.Session);
            Assert.Equal(new byte[] { 4, 5, 6 }, Next(instance, 2).Payload);
            instance.Update(43, [7, 8]);
            Assert.Equal(43u, Next(instance, 3).Promise);
            Assert.Equal(0, Next(instance, 5).Code);
            var data = new byte[] { 9, 10, 11, 12 };
            var decoded = instance.Decrypt(data, new byte[16], new byte[8], [new(1, 3)]);
            Assert.Equal(data, decoded);
            Assert.NotSame(data, decoded);
            Assert.Throws<DrmPlaybackException>(() => instance.Decrypt([255], new byte[16], new byte[8], [new(0, 1)]));
            Assert.Throws<DrmPlaybackException>(() => instance.Decrypt(data, new byte[16], new byte[8], [new(0, 3)]));
        }
        Assert.True(released);
        Assert.Equal(cdmThread, destroyedOnThread);
        instance.Dispose();
        Assert.Throws<ObjectDisposedException>(() => instance.Begin(44, [1]));
    }

    private static CdmEvent Next(NativeWidevine instance, int kind)
    {
        CdmEvent? result = null;
        Assert.True(SpinWait.SpinUntil(() => instance.TryPoll(out result!), TimeSpan.FromSeconds(5)), "CDM callback timed out");
        Assert.Equal(kind, result!.Kind);
        return result;
    }

    private static nint CreateTable()
    {
        var methods = new nint[20];
        methods[CdmAbi.Initialize] = (nint)(delegate* unmanaged<nint, byte, byte, byte, void>)&Initialize;
        methods[CdmAbi.CreateSession] = (nint)(delegate* unmanaged<nint, uint, uint, uint, byte*, uint, void>)&Begin;
        methods[CdmAbi.UpdateSession] = (nint)(delegate* unmanaged<nint, uint, byte*, uint, byte*, uint, void>)&Update;
        methods[CdmAbi.TimerExpired] = (nint)(delegate* unmanaged<nint, nint, void>)&Timer;
        methods[CdmAbi.Decrypt] = (nint)(delegate* unmanaged<nint, CdmAbi.InputBuffer*, nint, uint>)&Decrypt;
        methods[CdmAbi.Destroy] = (nint)(delegate* unmanaged<nint, void>)&Destroy;
        return CdmAbi.Vtable(methods);
    }

    [UnmanagedCallersOnly]
    private static nint Create(int version, byte* system, uint length, delegate* unmanaged<int, nint, nint> getHost, nint data)
    {
        if (version != 10 || length != 18 || getHost(11, data) != 0) return 0;
        var obj = (FakeObject*)NativeMemory.AllocZeroed((nuint)sizeof(FakeObject));
        *obj = new() { Vtable = table, Host = getHost(10, data), Thread = Environment.CurrentManagedThreadId };
        return (nint)obj;
    }

    [UnmanagedCallersOnly]
    private static void Initialize(nint self, byte distinctive, byte persistent, byte hardware)
    {
        var host = ((FakeObject*)self)->Host;
        ((delegate* unmanaged<nint, byte, void>)CdmAbi.Method(host, 3))(host, 1);
        ((delegate* unmanaged<nint, long, nint, void>)CdmAbi.Method(host, 1))(host, 5, 123);
    }

    [UnmanagedCallersOnly]
    private static void Timer(nint self, nint context)
    {
        var host = ((FakeObject*)self)->Host;
        ((delegate* unmanaged<nint, uint, uint, void>)CdmAbi.Method(host, 4))(host, (uint)context, (uint)Environment.CurrentManagedThreadId);
    }

    [UnmanagedCallersOnly]
    private static void Begin(nint self, uint promise, uint sessionType, uint initType, byte* bytes, uint count)
    {
        var host = ((FakeObject*)self)->Host;
        byte* session = stackalloc byte[] { 2, 3 };
        byte* message = stackalloc byte[] { 4, 5, 6 };
        ((delegate* unmanaged<nint, uint, byte*, uint, void>)CdmAbi.Method(host, 5))(host, promise, session, 2);
        ((delegate* unmanaged<nint, byte*, uint, uint, byte*, uint, void>)CdmAbi.Method(host, 8))(host, session, 2, 0, message, 3);
        new Span<byte>(message, 3).Clear(); // Host must own a copy after return.
    }

    [UnmanagedCallersOnly]
    private static void Update(nint self, uint promise, byte* session, uint sessionSize, byte* response, uint responseSize)
    {
        var host = ((FakeObject*)self)->Host;
        ((delegate* unmanaged<nint, uint, void>)CdmAbi.Method(host, 6))(host, promise);
        var key = new CdmAbi.KeyInformation { Status = 0 };
        ((delegate* unmanaged<nint, byte*, uint, byte, CdmAbi.KeyInformation*, uint, void>)CdmAbi.Method(host, 9))(host, session, sessionSize, 1, &key, 1);
    }

    [UnmanagedCallersOnly]
    private static uint Decrypt(nint self, CdmAbi.InputBuffer* input, nint block)
    {
        if (input->Data[0] == 255) return 2;
        var obj = (FakeObject*)self;
        if (obj->Thread != Environment.CurrentManagedThreadId || input->EncryptionScheme != 1 || input->KeyIdSize != 16 || input->IvSize != 8 || input->SubsampleCount != 1)
            return 4;
        var buffer = ((delegate* unmanaged<nint, uint, nint>)CdmAbi.Method(obj->Host, 0))(obj->Host, input->DataSize);
        if (buffer == 0) return 4;
        var capacity = ((delegate* unmanaged<nint, uint>)CdmAbi.Method(buffer, 1))(buffer);
        var data = ((delegate* unmanaged<nint, nint>)CdmAbi.Method(buffer, 2))(buffer);
        new ReadOnlySpan<byte>(input->Data, (int)input->DataSize).CopyTo(new Span<byte>((void*)data, (int)capacity));
        ((delegate* unmanaged<nint, uint, void>)CdmAbi.Method(buffer, 3))(buffer, input->DataSize);
        ((delegate* unmanaged<nint, nint, void>)CdmAbi.Method(block, 0))(block, buffer);
        ((delegate* unmanaged<nint, long, void>)CdmAbi.Method(block, 2))(block, input->Timestamp);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void Destroy(nint self)
    {
        destroyedOnThread = Environment.CurrentManagedThreadId;
        NativeMemory.Free((void*)self);
    }
}
