#if WINDOWS
using System.Runtime.InteropServices;
using Pookie.Audio;
using static Pookie.Audio.MediaFoundationNative;

namespace Pookie.App.Diagnostics;

internal static unsafe class AotWindowsMediaCheck
{
    // No CDM, account or output device: resolve a local WAV and call the
    // generated protected-source wrapper through the same ABI as Media Engine.
    internal static void Run(string wave)
    {
        Check(CoInitializeEx(0, 0), "CoInitializeEx");
        try
        {
            Check(MFStartup(0x20070, 0), "MFStartup");
            try
            {
                Check(MFCreateSourceResolver(out var resolverPointer), "MFCreateSourceResolver");
                using var resolver = new ComReference(resolverPointer);
                nint resolved; int type;
                fixed (char* url = Path.GetFullPath(wave))
                    Check(((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, int*, nint*, int>)Slot(resolverPointer, 3))(
                        resolverPointer, url, 1, 0, &type, &resolved), "CreateObjectFromURL");
                using var resolvedObject = new ComReference(resolved);
                using var source = Query(resolved, Source, "IMFMediaSource");
                using var wrapper = new PlayReadyMediaSource(source.Value, 0);
                using var exported = Export<IProtectedMediaSource>(wrapper);
                using var trustedInput = Query(exported.Value, typeof(ITrustedMediaInput).GUID, "IMFTrustedInput");
                using var agile = Query(exported.Value, new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), "IAgileObject");
                uint expected, actual;
                Check(Call(source.Value, 7, (nint)(&expected)), "Native.GetCharacteristics");
                Check(Call(exported.Value, 7, (nint)(&actual)), "Generated.GetCharacteristics");
                if (actual != expected) throw new InvalidOperationException("Generated COM source returned different characteristics.");
                nint descriptor;
                Check(Call(exported.Value, 8, (nint)(&descriptor)), "Generated.CreatePresentationDescriptor");
                using var presentation = new ComReference(descriptor);
                Console.WriteLine("AOT_WINDOWS_COM_OK: generated media-source export, native vtable forwarding, trusted input and agile identity");
            }
            finally { Check(MFShutdown(), "MFShutdown"); }
        }
        finally { CoUninitialize(); }
        using var media = new Pookie.Media.Windows.WindowsMediaSession();
        media.Update(new("aot", "AOT fixture", "Artist", null, null, 0, 2, Pookie.Media.MediaPlayback.Playing, true, true, true, 1, false));
        media.Clear();
        Console.WriteLine("AOT_WINDOWS_MEDIA_OK: LibSMTC metadata, playback state and clear");
    }
}
#endif
