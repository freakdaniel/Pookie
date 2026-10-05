using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pookie.Audio;

// Chromium Host/CDM v10 ABI. Provenance and licensing: root LICENSING.md and REUSE.toml.
// CDM v10 uses single inheritance and passes all structs by pointer/reference.
// 32-bit C++ thiscall is deliberately excluded. Default unmanaged conventions
// cover the 64-bit System V, Windows and AArch64 instance-call conventions here.
internal static unsafe class CdmAbi
{
    public const int Version = 10;
    public const int Initialize = 0, CreateSession = 3, UpdateSession = 5, TimerExpired = 8, Decrypt = 9,
        PlatformChallengeResponse = 16, OutputProtectionStatus = 17, StorageId = 18, Destroy = 19;

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    internal struct InputBuffer
    {
        [FieldOffset(0)] public byte* Data;
        [FieldOffset(8)] public uint DataSize;
        [FieldOffset(12)] public uint EncryptionScheme;
        [FieldOffset(16)] public byte* KeyId;
        [FieldOffset(24)] public uint KeyIdSize;
        [FieldOffset(32)] public byte* Iv;
        [FieldOffset(40)] public uint IvSize;
        [FieldOffset(48)] public CencSubsample* Subsamples;
        [FieldOffset(56)] public uint SubsampleCount;
        [FieldOffset(64)] public uint CryptBlocks;
        [FieldOffset(68)] public uint SkipBlocks;
        [FieldOffset(72)] public long Timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyInformation
    {
        public nint KeyId;
        public uint KeyIdSize, Status, SystemCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HostObject { public nint Vtable, Managed; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferObject
    {
        public nint Vtable, Owner;
        public byte* Data;
        public uint Capacity, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlockObject { public nint Vtable, Buffer; public long Timestamp; }

    public static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    // The two zero prefix words reserve Itanium's offset-to-top/RTTI locations.
    // MSVC uses the address point directly. No RTTI casts cross this boundary.
    public static nint Vtable(ReadOnlySpan<nint> entries)
    {
        var memory = (nint*)NativeMemory.AllocZeroed((nuint)(entries.Length + 2), (nuint)sizeof(nint));
        entries.CopyTo(new Span<nint>(memory + 2, entries.Length));
        return (nint)(memory + 2);
    }

    public static readonly nint HostTable = Vtable([
        (nint)(delegate* unmanaged<nint, uint, nint>)&HostAllocate,
        (nint)(delegate* unmanaged<nint, long, nint, void>)&HostTimer,
        (nint)(delegate* unmanaged<nint, double>)&HostTime,
        (nint)(delegate* unmanaged<nint, byte, void>)&HostInitialized,
        (nint)(delegate* unmanaged<nint, uint, uint, void>)&HostKeyPromise,
        (nint)(delegate* unmanaged<nint, uint, byte*, uint, void>)&HostNewSession,
        (nint)(delegate* unmanaged<nint, uint, void>)&HostResolved,
        (nint)(delegate* unmanaged<nint, uint, uint, uint, byte*, uint, void>)&HostRejected,
        (nint)(delegate* unmanaged<nint, byte*, uint, uint, byte*, uint, void>)&HostMessage,
        (nint)(delegate* unmanaged<nint, byte*, uint, byte, KeyInformation*, uint, void>)&HostKeys,
        (nint)(delegate* unmanaged<nint, byte*, uint, double, void>)&HostExpiration,
        (nint)(delegate* unmanaged<nint, byte*, uint, void>)&HostClosed,
        (nint)(delegate* unmanaged<nint, byte*, uint, byte*, uint, void>)&HostPlatformChallenge,
        (nint)(delegate* unmanaged<nint, uint, void>)&HostProtectOutput,
        (nint)(delegate* unmanaged<nint, void>)&HostQueryOutput,
        (nint)(delegate* unmanaged<nint, uint, uint, void>)&HostDeferred,
        (nint)(delegate* unmanaged<nint, nint, nint>)&HostFile,
        (nint)(delegate* unmanaged<nint, uint, void>)&HostStorage,
        (nint)(delegate* unmanaged<nint, void>)&UnusedDestructor,
        (nint)(delegate* unmanaged<nint, void>)&UnusedDestructor]);

    public static readonly nint BufferTable = Vtable([
        (nint)(delegate* unmanaged<nint, void>)&BufferDestroy,
        (nint)(delegate* unmanaged<nint, uint>)&BufferCapacity,
        (nint)(delegate* unmanaged<nint, nint>)&BufferData,
        (nint)(delegate* unmanaged<nint, uint, void>)&BufferSetSize,
        (nint)(delegate* unmanaged<nint, uint>)&BufferSize,
        (nint)(delegate* unmanaged<nint, void>)&UnusedDestructor,
        (nint)(delegate* unmanaged<nint, void>)&UnusedDestructor]);

    public static readonly nint BlockTable = Vtable([
        (nint)(delegate* unmanaged<nint, nint, void>)&BlockSetBuffer,
        (nint)(delegate* unmanaged<nint, nint>)&BlockGetBuffer,
        (nint)(delegate* unmanaged<nint, long, void>)&BlockSetTimestamp,
        (nint)(delegate* unmanaged<nint, long>)&BlockGetTimestamp,
        (nint)(delegate* unmanaged<nint, void>)&UnusedDestructor,
        (nint)(delegate* unmanaged<nint, void>)&UnusedDestructor]);

    private static NativeWidevine Host(nint self) => (NativeWidevine)GCHandle.FromIntPtr(((HostObject*)self)->Managed).Target!;
    // Exceptions must never escape reverse P/Invoke into the C++ CDM.
    [UnmanagedCallersOnly] private static nint HostAllocate(nint self, uint size) { try { return Host(self).Allocate(size); } catch { return 0; } }
    [UnmanagedCallersOnly] private static void HostTimer(nint self, long delay, nint context) { try { Host(self).SetTimer(delay, context); } catch { } }
    [UnmanagedCallersOnly] private static double HostTime(nint self) => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    [UnmanagedCallersOnly] private static void HostInitialized(nint self, byte success) { try { Host(self).Emit(1, 0, success != 0 ? 0 : -1); } catch { } }
    [UnmanagedCallersOnly] private static void HostKeyPromise(nint self, uint promise, uint status) { try { Host(self).Emit(3, promise, (int)status); } catch { } }
    [UnmanagedCallersOnly] private static void HostNewSession(nint self, uint promise, byte* session, uint size)
    {
        try { Host(self).Session = size <= 4096 && session != null ? new ReadOnlySpan<byte>(session, (int)size).ToArray() : []; Host(self).Emit(3, promise, size > 0 && size <= 4096 ? 0 : -1); } catch { }
    }
    [UnmanagedCallersOnly] private static void HostResolved(nint self, uint promise) { try { Host(self).Emit(3, promise, 0); } catch { } }
    [UnmanagedCallersOnly] private static void HostRejected(nint self, uint promise, uint exception, uint code, byte* message, uint size) { try { Host(self).Emit(4, promise, (int)exception); } catch { } }
    [UnmanagedCallersOnly] private static void HostMessage(nint self, byte* session, uint sessionSize, uint type, byte* message, uint size)
    {
        try
        {
            if (size > 2 * 1024 * 1024 || message == null) { Host(self).Emit(4, 0, -4); return; }
            Host(self).Emit(2, 0, (int)type, new ReadOnlySpan<byte>(message, (int)size).ToArray());
        }
        catch { }
    }
    [UnmanagedCallersOnly] private static void HostKeys(nint self, byte* session, uint sessionSize, byte additional, KeyInformation* keys, uint count)
    {
        try { if (keys != null && count <= 256) for (var i = 0; i < count; i++) Host(self).Emit(5, 0, (int)keys[i].Status); } catch { }
    }
    [UnmanagedCallersOnly] private static void HostExpiration(nint self, byte* session, uint size, double time) { }
    [UnmanagedCallersOnly] private static void HostClosed(nint self, byte* session, uint size) { try { Host(self).Session = []; Host(self).Emit(6, 0, 0); } catch { } }
    [UnmanagedCallersOnly] private static void HostPlatformChallenge(nint self, byte* service, uint serviceSize, byte* challenge, uint challengeSize) { try { Host(self).RejectPlatformChallenge(); } catch { } }
    [UnmanagedCallersOnly] private static void HostProtectOutput(nint self, uint mask) { }
    [UnmanagedCallersOnly] private static void HostQueryOutput(nint self) { try { Host(self).ReportUnverifiedOutput(); } catch { } }
    [UnmanagedCallersOnly] private static void HostDeferred(nint self, uint stream, uint status) { }
    [UnmanagedCallersOnly] private static nint HostFile(nint self, nint client) => 0;
    [UnmanagedCallersOnly] private static void HostStorage(nint self, uint version) { try { Host(self).ReportNoStorage(version); } catch { } }
    [UnmanagedCallersOnly] private static void UnusedDestructor(nint self) { }

    [UnmanagedCallersOnly] private static void BufferDestroy(nint self) { try { Host(((BufferObject*)self)->Owner).FreeBuffer(self); } catch { } }
    [UnmanagedCallersOnly] private static uint BufferCapacity(nint self) => ((BufferObject*)self)->Capacity;
    [UnmanagedCallersOnly] private static nint BufferData(nint self) => (nint)((BufferObject*)self)->Data;
    [UnmanagedCallersOnly] private static void BufferSetSize(nint self, uint size) => ((BufferObject*)self)->Size = Math.Min(size, ((BufferObject*)self)->Capacity);
    [UnmanagedCallersOnly] private static uint BufferSize(nint self) => ((BufferObject*)self)->Size;
    [UnmanagedCallersOnly] private static void BlockSetBuffer(nint self, nint buffer) => ((BlockObject*)self)->Buffer = buffer;
    [UnmanagedCallersOnly] private static nint BlockGetBuffer(nint self) => ((BlockObject*)self)->Buffer;
    [UnmanagedCallersOnly] private static void BlockSetTimestamp(nint self, long timestamp) => ((BlockObject*)self)->Timestamp = timestamp;
    [UnmanagedCallersOnly] private static long BlockGetTimestamp(nint self) => ((BlockObject*)self)->Timestamp;

    [UnmanagedCallersOnly]
    public static nint GetHost(int version, nint data) => version == Version ? data : 0;
}
