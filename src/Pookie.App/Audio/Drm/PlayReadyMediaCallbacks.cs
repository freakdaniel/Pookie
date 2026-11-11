// SPDX-FileCopyrightText: Microsoft Corporation
// SPDX-FileCopyrightText: 2026 Daniel Freak
// SPDX-License-Identifier: BSD-3-Clause AND MIT
#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Pookie.Logging;
using Windows.Foundation.Collections;
using Windows.Media.Protection;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.Audio;

// Protected source and protection manager follow Microsoft's MediaEngineEMEUWPSample.
[GeneratedComClass]
internal sealed unsafe partial class PlayReadyMediaSource(nint source, nint cdm) : IProtectedMediaSource, ITrustedMediaInput, IAgileObject, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<(uint Stream, Guid Interface), ComReference> authorities = new();
    private ComReference? trustedInput;
    private bool closed;

    public int GetEvent(uint flags, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(source, 3))(source, flags, value);

    public int BeginGetEvent(nint callback, nint state) => Call(source, 4, callback, state);
    public int EndGetEvent(nint result, nint* value) => Call(source, 5, result, (nint)value);
    public int QueueEvent(uint type, Guid* extendedType, int status, nint value) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, Guid*, int, nint, int>)Slot(source, 6))(source, type, extendedType, status, value);

    public int GetCharacteristics(uint* value) => Call(source, 7, (nint)value);
    public int CreatePresentationDescriptor(nint* descriptor) => Call(source, 8, (nint)descriptor);
    public int Start(nint descriptor, Guid* format, nint position) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint, int>)Slot(source, 9))(source, descriptor, format, position);

    public int Stop() => Call(source, 10);
    public int Pause() => Call(source, 11);
    public int Shutdown()
    {
        lock (gate)
        {
            if (closed) return 0;
            closed = true;
            foreach (var authority in authorities.Values) authority.Dispose();
            authorities.Clear();
            trustedInput?.Dispose();
        }
        return Call(source, 12);
    }

    public int GetInputTrustAuthority(uint stream, Guid* iid, nint* value)
    {
        *value = 0;
        lock (gate)
        {
            if (closed) return unchecked((int)0xC00D3E85); // MF_E_SHUTDOWN
            var key = (stream, *iid);
            if (!authorities.TryGetValue(key, out var authority))
            {
                if (trustedInput == null)
                {
                    nint input;
                    Check(((delegate* unmanaged[Stdcall]<nint, byte*, uint, nint*, int>)Slot(cdm, 8))(cdm, null, 0, &input), "CreateTrustedInput");
                    trustedInput = new(input);
                }
                var trusted = trustedInput.Value;
                nint created;
                Check(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Slot(trusted, 3))(
                    trusted, stream, iid, &created), "GetInputTrustAuthority");
                authority = new(created);
                authorities.Add(key, authority);
            }
            Marshal.AddRef(authority.Value);
            *value = authority.Value;
            return 0;
        }
    }

    public void Dispose()
    {
        var result = Shutdown();
        if (result < 0) AppLog.For("Pookie.Audio.PlayReady").Debug("IMFMediaSource.Shutdown (0x{HResult:X8})", result);
    }
}

[GeneratedComClass]
internal sealed unsafe partial class PlayReadySourceExtension(nint source) : IMediaEngineExtension, IAgileObject
{
    public int CanPlayType(int audioOnly, nint mime, int* result)
    {
        *result = 0; // MF_MEDIA_ENGINE_CANPLAY_NOT_SUPPORTED
        return 0;
    }

    public int BeginCreateObject(nint url, nint stream, int type, nint* cancel, nint callback, nint state)
    {
        if (cancel != null) *cancel = 0;
        if (type != 0) return unchecked((int)0xC00D36BB); // MF_E_UNEXPECTED
        Check(MFCreateAsyncResult(source, callback, state, out var result), "MFCreateAsyncResult (source)");
        using var operation = new ComReference(result);
        Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(result, 5))(result, 0), "IMFAsyncResult.SetStatus");
        return Call(callback, 4, result); // IMFAsyncCallback.Invoke
    }

    public int CancelObjectCreation(nint cancel) => NotImplemented;

    public int EndCreateObject(nint result, nint* value)
    {
        Check(Call(result, 4), "IMFAsyncResult.GetStatus");
        return Call(result, 6, (nint)value); // IMFAsyncResult.GetObject
    }
}

[GeneratedComClass]
internal sealed unsafe partial class PlayReadyProtectionManager : IContentProtectionManager, IMediaProtectionProperties, IAgileObject
{
    private readonly nint cdm;
    private readonly PropertySet properties = new();

    internal PlayReadyProtectionManager(nint cdm, nint services)
    {
        this.cdm = cdm;
        nint serverPtr;
        Check(GetCdmService(services, PlayReadyCdm.PmpServer, &serverPtr), "IMFGetService (PMP server)");
        using var server = new ComReference(serverPtr);
        properties["Windows.Media.Protection.MediaProtectionPMPServer"] = MediaProtectionPMPServer.FromAbi(serverPtr);
    }

    public int BeginEnableContent(nint activate, nint topology, nint callback, nint state)
    {
        Check(MFCreateAsyncResult(0, callback, state, out var result), "MFCreateAsyncResult (content enabler)");
        using var operation = new ComReference(result);
        var unknown = Unknown;
        nint value;
        Check(PlayReadyPmpHost.ActivateObject(activate, &unknown, &value), "IMFActivate.ActivateObject (content enabler)");
        using var activated = new ComReference(value);
        // Some activation requests implement only IUnknown.
        var iid = new Guid("d3c4ef59-49ce-4381-9071-d5bcd044c770");
        Marshal.QueryInterface(value, in iid, out var enablerPtr);
        using var enabler = new ComReference(enablerPtr);
        return Call(cdm, 3, enablerPtr, result); // IMFContentDecryptionModule.SetContentEnabler
    }

    public int EndEnableContent(nint result) => Call(result, 4); // IMFAsyncResult.GetStatus

    public int GetProperties(nint* value)
    {
        *value = WinRT.MarshalInterface<IPropertySet>.FromManaged(properties);
        return 0;
    }

    public int GetIids(uint* count, Guid** iids)
    {
        *iids = (Guid*)Marshal.AllocCoTaskMem(sizeof(Guid));
        **iids = typeof(IMediaProtectionProperties).GUID;
        *count = 1;
        return 0;
    }

    public int GetRuntimeClassName(nint* value) { *value = 0; return 0; }
    public int GetTrustLevel(int* value) { *value = 0; return 0; }
    public int AddServiceRequested(nint handler, long* token) => NotImplemented;
    public int RemoveServiceRequested(long token) => 0;
    public int AddRebootNeeded(nint handler, long* token) => NotImplemented;
    public int RemoveRebootNeeded(long token) => 0;
    public int AddComponentLoadFailed(nint handler, long* token) => NotImplemented;
    public int RemoveComponentLoadFailed(long token) => 0;
}

[GeneratedComClass]
internal sealed partial class PlayReadyEngineEvents : IMediaEngineNotify, IAgileObject
{
    private Exception? failure;
    internal Exception? Failure => Volatile.Read(ref failure);

    public int EventNotify(uint eventCode, nuint first, uint second)
    {
        const uint error = 5, loadedMetadata = 10;
        if (eventCode == error)
            Volatile.Write(ref failure, new COMException($"Media Foundation: protected playback error {first} (0x{second:X8}).", unchecked((int)second)));
        if (eventCode == loadedMetadata) AppLog.For("Pookie.Audio.PlayReady").Debug("NATIVE_MF_ENGINE_METADATA");
        return 0;
    }
}
#endif
