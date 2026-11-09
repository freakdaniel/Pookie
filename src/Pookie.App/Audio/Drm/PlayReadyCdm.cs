#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.Audio;

internal static unsafe class PlayReadyCdm
{
    private const string KeySystem = "com.microsoft.playready.recommendation";
    private static readonly Guid FactoryClass = new("b44392da-499b-446b-a4cb-005fead0e6d5");
    private static readonly Guid Factory4 = new("fbe256c1-43cf-4a9b-8cb8-ce8632a34186");
    private static readonly Guid CdmFactory = new("7d5abf16-4cbb-4e08-b977-9ba59049943e");
    private static readonly Guid GetService = new("fa993888-4383-415a-a930-dd472a8cf6f7");
    internal static readonly Guid PmpServer = new("0c111226-7b26-4d31-95bb-9c1b08ef7fc0");

    internal static ComReference CreateFactory()
    {
        Check(CoCreateInstance(in FactoryClass, 0, 1, in Factory4, out var factory), "CoCreateInstance (Media Engine)");
        return new(factory);
    }

    internal static ComReference Create(nint factory, string store)
    {
        nint cdmFactoryPtr;
        var iid = CdmFactory;
        fixed (char* keySystem = KeySystem)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, Guid*, nint*, int>)Slot(factory, 3))(
                factory, keySystem, &iid, &cdmFactoryPtr), "CreateContentDecryptionModuleFactory");
        using var cdmFactory = new ComReference(cdmFactoryPtr);
        using var configuration = ConfigureAccess();
        var configurationPtr = configuration.Value;
        nint accessPtr;
        fixed (char* keySystem = KeySystem)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, nint*, uint, nint*, int>)Slot(cdmFactoryPtr, 4))(
                cdmFactoryPtr, keySystem, &configurationPtr, 1, &accessPtr), "CreateContentDecryptionModuleAccess");
        using var access = new ComReference(accessPtr);
        using var properties = CreatePropertyStore();
        fixed (char* origin = Guid.NewGuid().ToString("B"), path = store)
        {
            SetProperty(properties.Value, new("1218a3e2-cfb0-4c98-90e5-5f5818d4b67e"), 2, new(31, (nint)origin)); // VT_LPWSTR
            SetProperty(properties.Value, new("77d993b9-ba61-4bb7-92c6-18c86a189c06"), 2, new(31, (nint)path));
        }
        nint cdm;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(accessPtr, 3))(
            accessPtr, properties.Value, &cdm), "CreateContentDecryptionModule");
        return new(cdm);
    }

    internal static ComReference Services(nint cdm) => Query(cdm, GetService, "IMFGetService (CDM)");

    // Keep the agile host alive until the CDM and all of its callbacks are released.
    internal static ComReference SetHost(nint cdm, nint services)
    {
        nint hostPtr;
        if (GetCdmService(services, PmpHost, &hostPtr) < 0)
        {
            Check(GetCdmService(services, typeof(IPmpHostApp).GUID, &hostPtr), "IMFGetService (PMP host app)");
            using var hostApp = new ComReference(hostPtr);
            Check(Call(cdm, 5, hostPtr), "SetPMPHostApp");
            return new(0);
        }

        using var host = new ComReference(hostPtr);
        Check(RoGetAgileReference(0, in PmpHost, hostPtr, out var agilePtr), "RoGetAgileReference (PMP host)");
        var agile = new ComReference(agilePtr);
        try
        {
            using var app = Export<IPmpHostApp>(new PlayReadyPmpHost(agilePtr));
            Check(Call(cdm, 5, app.Value), "SetPMPHostApp");
            return agile;
        }
        catch
        {
            agile.Dispose();
            throw;
        }
    }

    internal static ComReference CreateSession(nint cdm, CdmSessionCallbacks callbacks)
    {
        using var pointer = Export<ICdmSessionCallbacks>(callbacks);
        nint session;
        const uint temporarySession = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(cdm, 6))(
            cdm, temporarySession, pointer.Value, &session), "CreateSession");
        return new(session);
    }

    internal static void GenerateRequest(nint session, byte[] pssh)
    {
        fixed (byte* data = pssh)
        fixed (char* type = "cenc")
            Check(((delegate* unmanaged[Stdcall]<nint, char*, byte*, uint, int>)Slot(session, 7))(
                session, type, data, (uint)pssh.Length), "GenerateRequest");
    }

    internal static void Update(nint session, byte[] license)
    {
        fixed (byte* data = license)
            Check(((delegate* unmanaged[Stdcall]<nint, byte*, uint, int>)Slot(session, 8))(
                session, data, (uint)license.Length), "Update (PlayReady license)");
    }

    private static ComReference ConfigureAccess()
    {
        var configuration = CreatePropertyStore();
        var initData = Marshal.StringToBSTR("cenc");
        var videoType = Marshal.StringToBSTR("video/mp4; codecs=\"avc1.640028\"");
        try
        {
            using var video = CreatePropertyStore();
            const ushort vector = 0x1000, bstr = 8, ui4 = 19, variant = 12, unknown = 13;
            SetProperty(video.Value, new("289fb1fc-d9c4-4cc7-b2be-972b0e9b283a"), 4, new(bstr, videoType));
            var videoCapability = new PropVariant(unknown, video.Value);
            SetProperty(configuration.Value, new("497d231b-4eb9-4df0-b474-b9afeb0adf38"), 3, new(vector | bstr, new CountedArray(1, (nint)(&initData))));
            SetProperty(configuration.Value, new("7dc9c4a5-12be-497e-8bff-9b60b2dc5845"), 4, new(ui4, (nint)1)); // Required ID.
            SetProperty(configuration.Value, new("5d4df6ae-9af1-4e3d-955b-0e4bd22fedf0"), 5, new(ui4, (nint)2)); // Optional persistence.
            SetProperty(configuration.Value, new("980fbb84-297d-4ea7-895f-bcf28a462881"), 6, new(vector | variant, (CountedArray)default));
            SetProperty(configuration.Value, new("b172f83d-30dd-4c10-8006-ed53da4d3bdb"), 7, new(vector | variant, new CountedArray(1, (nint)(&videoCapability))));
            uint temporary = 0;
            SetProperty(configuration.Value, new("7623384f-00f5-4376-8698-3458db030ed5"), 9, new(vector | ui4, new CountedArray(1, (nint)(&temporary))));
            return configuration;
        }
        catch
        {
            configuration.Dispose();
            throw;
        }
        finally
        {
            Marshal.FreeBSTR(videoType);
            Marshal.FreeBSTR(initData);
        }
    }

    private static ComReference CreatePropertyStore()
    {
        Guid iid = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
        Check(PSCreateMemoryPropertyStore(in iid, out var store), "PSCreateMemoryPropertyStore");
        return new(store);
    }

    private static void SetProperty(nint store, Guid format, uint id, PropVariant value)
    {
        var key = new PropertyKey { Format = format, Id = id };
        Check(((delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropVariant*, int>)Slot(store, 6))(
            store, &key, &value), $"IPropertyStore.SetValue ({format})");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private ushort type, reserved1, reserved2, reserved3;
        private VariantValue value;

        internal PropVariant(ushort type, nint scalar) { this.type = type; value.Scalar = scalar; }
        internal PropVariant(ushort type, CountedArray array) { this.type = type; value.Array = array; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct VariantValue
    {
        [FieldOffset(0)] internal nint Scalar;
        [FieldOffset(0)] internal CountedArray Array;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CountedArray(uint count, nint elements)
    {
        private readonly uint size = count;
        private readonly nint pointer = elements;
    }
}

[GeneratedComClass]
internal sealed unsafe partial class CdmSessionCallbacks : ICdmSessionCallbacks, IAgileObject
{
    internal TaskCompletionSource<byte[]> Challenge { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int KeyMessage(int type, byte* message, uint length, nint url)
    {
        const int licenseRequest = 0;
        if (type != licenseRequest || message == null || length is 0 or > PlayReadyLicense.MaxMessageBytes)
            return InvalidArgument;
        var challenge = new ReadOnlySpan<byte>(message, (int)length).ToArray();
        if (!Challenge.TrySetResult(challenge)) System.Security.Cryptography.CryptographicOperations.ZeroMemory(challenge);
        return 0;
    }

    public int KeyStatusChanged() => 0;
}
#endif
