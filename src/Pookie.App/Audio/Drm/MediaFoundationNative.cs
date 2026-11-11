#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Pookie.Audio;

// Win32 ABI from mfapi.h, mfobjects.h, mfidl.h, mfmediaengine.h and
// mfcontentdecryptionmodule.h. COM interfaces below are implemented by Pookie.
internal static unsafe partial class MediaFoundationNative
{
    internal const int NotImplemented = unchecked((int)0x80004001);
    internal const int InvalidArgument = unchecked((int)0x80070057);
    internal const int InvalidStreamOperation = unchecked((int)0x80030001);
    internal const int StreamAccessDenied = unchecked((int)0x80030005);
    internal const int StreamReadFault = unchecked((int)0x8003001E);

    internal static readonly Guid Unknown = new("00000000-0000-0000-c000-000000000046");
    internal static readonly Guid Source = new("279a808d-aec7-40c8-9c6b-a6b492c78a66");
    internal static readonly Guid Activate = new("7fee9e9a-4a89-47a6-899c-b6a53a70fb67");
    internal static readonly Guid PmpHost = new("f70ca1a9-fdc7-4782-b994-adffb1c98606");
    private static readonly Guid Service = new("15320c45-ff80-484a-9dcb-0df894e69a01");

    internal static void Check(int hr, string operation)
    {
        if (hr < 0) throw new COMException($"Media Foundation: {operation} (0x{hr:X8}).", hr);
    }

    // Vtable slots include the three IUnknown methods. Keep the slot with
    // its named interface operation at the call site.
    internal static void* Slot(nint instance, int index) => (*(void***)instance)[index];
    internal static int Call(nint instance, int slot) => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(instance, slot))(instance);
    internal static int Call(nint instance, int slot, nint argument) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(instance, slot))(instance, argument);
    internal static int Call(nint instance, int slot, nint first, nint second) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(instance, slot))(instance, first, second);

    internal static ComReference Query(nint instance, Guid id, string operation)
    {
        Check(Marshal.QueryInterface(instance, in id, out var value), operation);
        return new(value);
    }

    internal static int GetCdmService(nint services, Guid iid, nint* value)
    {
        var service = Service;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)Slot(services, 3))(services, &service, &iid, value);
    }

    internal static ComReference Export<T>(T value) where T : class => new((nint)ComInterfaceMarshaller<T>.ConvertToUnmanaged(value));

    internal static void SetAttribute(nint attributes, Guid key, uint value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)Slot(attributes, 21))(attributes, &key, value), "IMFAttributes.SetUINT32");

    internal static void SetAttribute(nint attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(attributes, 24))(attributes, &key, &value), "IMFAttributes.SetGUID");

    internal static void SetAttribute(nint attributes, Guid key, nint value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint, int>)Slot(attributes, 27))(attributes, &key, value), "IMFAttributes.SetUnknown");

    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint mode);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint value);

    [LibraryImport("ole32.dll")]
    internal static partial int CreateStreamOnHGlobal(nint memory, int deleteOnRelease, out nint stream);

    [LibraryImport("combase.dll")]
    internal static partial int RoGetAgileReference(int options, in Guid iid, nint value, out nint reference);

    [LibraryImport("mfplat.dll")]
    internal static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    internal static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    internal static partial int MFCreateAttributes(out nint attributes, uint initialSize);

    [LibraryImport("mfplat.dll")]
    internal static partial int MFCreateAsyncResult(nint value, nint callback, nint state, out nint result);

    [LibraryImport("mfplat.dll")]
    internal static partial int MFCreateMFByteStreamOnStream(nint stream, out nint byteStream);

    [LibraryImport("mfplat.dll")]
    internal static partial int MFCreateSourceResolver(out nint resolver);

    [LibraryImport("mfplat.dll")]
    internal static partial int MFSerializeAttributesToStream(nint attributes, uint options, nint stream);

    [LibraryImport("propsys.dll")]
    internal static partial int PSCreateMemoryPropertyStore(in Guid iid, out nint store);
}

internal sealed class ComReference(nint value) : IDisposable
{
    internal nint Value { get; private set; } = value;

    internal void TakeFrom(ComReference reference) => Value = reference.Detach();

    internal nint Detach()
    {
        var pointer = Value;
        Value = 0;
        return pointer;
    }

    public void Dispose()
    {
        var pointer = Detach();
        if (pointer != 0) Marshal.Release(pointer);
    }
}

[GeneratedComInterface, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
internal partial interface IAgileObject;

[GeneratedComInterface, Guid("0000000c-0000-0000-c000-000000000046")]
internal unsafe partial interface IEncryptedMediaStream
{
    [PreserveSig] int Read(void* buffer, uint count, uint* read);
    [PreserveSig] int Write(void* buffer, uint count, uint* written);
    [PreserveSig] int Seek(long move, uint origin, long* position);
    [PreserveSig] int SetSize(ulong size);
    [PreserveSig] int CopyTo(nint stream, ulong count, ulong* read, ulong* written);
    [PreserveSig] int Commit(uint flags);
    [PreserveSig] int Revert();
    [PreserveSig] int LockRegion(ulong offset, ulong count, uint flags);
    [PreserveSig] int UnlockRegion(ulong offset, ulong count, uint flags);
    [PreserveSig] int Stat(StreamStat* stat, uint flags);
    [PreserveSig] int Clone(nint* stream);
}

// STATSTG from objidl.h. The name remains null for the virtual stream.
[StructLayout(LayoutKind.Sequential)]
internal struct StreamStat
{
    internal nint Name;
    internal uint Type;
    internal long Size;
    internal System.Runtime.InteropServices.ComTypes.FILETIME Modified, Created, Accessed;
    internal uint Mode, Locks;
    internal Guid Class;
    internal uint State, Reserved;
}

[GeneratedComInterface, Guid("3f96ee40-ad81-4096-8470-59a4b770f89a")]
internal unsafe partial interface ICdmSessionCallbacks
{
    [PreserveSig] int KeyMessage(int type, byte* message, uint length, nint url);
    [PreserveSig] int KeyStatusChanged();
}

[GeneratedComInterface, Guid("84d2054a-3aa1-4728-a3b0-440a418cf49c")]
internal unsafe partial interface IPmpHostApp
{
    [PreserveSig] int LockProcess();
    [PreserveSig] int UnlockProcess();
    [PreserveSig] int ActivateClassById(char* id, nint stream, Guid* iid, nint* value);
}

[GeneratedComInterface, Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66")]
public unsafe partial interface IProtectedMediaSource
{
    // IMFMediaEventGenerator slots precede IMFMediaSource slots.
    [PreserveSig] int GetEvent(uint flags, nint* value);
    [PreserveSig] int BeginGetEvent(nint callback, nint state);
    [PreserveSig] int EndGetEvent(nint result, nint* value);
    [PreserveSig] int QueueEvent(uint type, Guid* extendedType, int status, nint value);
    [PreserveSig] int GetCharacteristics(uint* value);
    [PreserveSig] int CreatePresentationDescriptor(nint* descriptor);
    [PreserveSig] int Start(nint descriptor, Guid* format, nint position);
    [PreserveSig] int Stop();
    [PreserveSig] int Pause();
    [PreserveSig] int Shutdown();
}

[GeneratedComInterface, Guid("542612c4-a1b8-4632-b521-de11ea64a0b0")]
public unsafe partial interface ITrustedMediaInput
{
    [PreserveSig] int GetInputTrustAuthority(uint stream, Guid* iid, nint* value);
}

[GeneratedComInterface, Guid("2f69d622-20b5-41e9-afdf-89ced1dda04e")]
internal unsafe partial interface IMediaEngineExtension
{
    [PreserveSig] int CanPlayType(int audioOnly, nint mime, int* result);
    [PreserveSig] int BeginCreateObject(nint url, nint stream, int type, nint* cancel, nint callback, nint state);
    [PreserveSig] int CancelObjectCreation(nint cancel);
    [PreserveSig] int EndCreateObject(nint result, nint* value);
}

[GeneratedComInterface, Guid("acf92459-6a61-42bd-b57c-b43e51203cb0")]
internal partial interface IContentProtectionManager
{
    [PreserveSig] int BeginEnableContent(nint activate, nint topology, nint callback, nint state);
    [PreserveSig] int EndEnableContent(nint result);
}

[GeneratedComInterface, Guid("45694947-c741-434b-a79e-474c12d93d2f")]
internal unsafe partial interface IMediaProtectionProperties
{
    // IInspectable slots, followed by the WinRT IMediaProtectionManager slots.
    [PreserveSig] int GetIids(uint* count, Guid** iids);
    [PreserveSig] int GetRuntimeClassName(nint* value);
    [PreserveSig] int GetTrustLevel(int* value);
    [PreserveSig] int AddServiceRequested(nint handler, long* token);
    [PreserveSig] int RemoveServiceRequested(long token);
    [PreserveSig] int AddRebootNeeded(nint handler, long* token);
    [PreserveSig] int RemoveRebootNeeded(long token);
    [PreserveSig] int AddComponentLoadFailed(nint handler, long* token);
    [PreserveSig] int RemoveComponentLoadFailed(long token);
    [PreserveSig] int GetProperties(nint* value);
}

[GeneratedComInterface, Guid("fee7c112-e776-42b5-9bbf-0048524e2bd5")]
internal partial interface IMediaEngineNotify
{
    [PreserveSig] int EventNotify(uint eventCode, nuint first, uint second);
}
#endif
