// SPDX-FileCopyrightText: 2026 Daniel Freak
// SPDX-FileCopyrightText: 2025 The Chromium Authors
// SPDX-License-Identifier: BSD-3-Clause
#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.Audio;

// Adapted from Chromium's media/cdm/win/pmp_host_app_impl.cc.
[GeneratedComClass]
internal sealed unsafe partial class PlayReadyPmpHost(nint agileHost) : IPmpHostApp, IAgileObject
{
    private static readonly Guid StoreActivate = new("2df7b51e-797b-4d06-be71-d14a52cf8421");
    private static readonly Guid ClassName = new("77631a31-e5e7-4785-bf17-20f57b224802");
    private static readonly Guid ObjectStream = new("3e73735c-e6c0-481d-8260-ee5db1343b5f");

    public int LockProcess()
    {
        using var host = Resolve();
        return Call(host.Value, 3); // IMFPMPHost.LockProcess
    }

    public int UnlockProcess()
    {
        using var host = Resolve();
        return Call(host.Value, 4); // IMFPMPHost.UnlockProcess
    }

    public int ActivateClassById(char* id, nint stream, Guid* iid, nint* value)
    {
        *value = 0;
        using var host = Resolve();
        Check(MFCreateAttributes(out var attributesPtr, 2), "MFCreateAttributes (PMP)");
        using var attributes = new ComReference(attributesPtr);
        var key = ClassName;
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, char*, int>)Slot(attributesPtr, 25))(attributesPtr, &key, id),
            "IMFAttributes.SetString (PMP class)");
        if (stream != 0)
        {
            StreamStat info;
            Check(((delegate* unmanaged[Stdcall]<nint, StreamStat*, uint, int>)Slot(stream, 12))(stream, &info, 1), "IStream.Stat (PMP)");
            if (info.Size is < 0 or > 1024 * 1024) return InvalidArgument;
            var bytes = new byte[(int)info.Size];
            fixed (byte* data = bytes)
            {
                uint read;
                Check(((delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)Slot(stream, 3))(stream, data, (uint)bytes.Length, &read),
                    "IStream.Read (PMP)");
                key = ObjectStream;
                Check(((delegate* unmanaged[Stdcall]<nint, Guid*, byte*, uint, int>)Slot(attributesPtr, 26))(attributesPtr, &key, data, read),
                    "IMFAttributes.SetBlob (PMP object)");
            }
        }

        Check(CreateStreamOnHGlobal(0, 1, out var encodedPtr), "CreateStreamOnHGlobal");
        using var encoded = new ComReference(encodedPtr);
        Check(MFSerializeAttributesToStream(attributesPtr, 0, encodedPtr), "MFSerializeAttributesToStream");
        Check(((delegate* unmanaged[Stdcall]<nint, long, uint, long*, int>)Slot(encodedPtr, 5))(encodedPtr, 0, 0, null), "IStream.Seek (PMP)");
        var classId = StoreActivate;
        var activateId = Activate;
        nint activatePtr;
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint, Guid*, nint*, int>)Slot(host.Value, 5))(
            host.Value, &classId, encodedPtr, &activateId, &activatePtr), "IMFPMPHost.CreateObjectByCLSID");
        using var activate = new ComReference(activatePtr);
        return ActivateObject(activatePtr, iid, value);
    }

    internal static int ActivateObject(nint activate, Guid* iid, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(activate, 33))(activate, iid, value);

    private ComReference Resolve()
    {
        var iid = PmpHost;
        nint value;
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(agileHost, 3))(agileHost, &iid, &value), "IAgileReference.Resolve (PMP host)");
        return new(value);
    }
}
#endif
