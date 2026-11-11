using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pookie.App;

internal static partial class MetalStorageCompatibility
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookieScratchStorageMode")]
    private static extern ref Func<nint, ulong>? StorageMode(
        [UnsafeAccessorType("Aprillz.MewVG.MNVGcontext, Aprillz.MewVG.Metal")] object? declaringType);

    internal static void Install()
    {
        if (OperatingSystem.IsMacOS()) StorageMode(null) = ChooseStorageMode;
    }

    private static ulong ChooseStorageMode(nint device)
    {
        var familySelector = Selector("supportsFamily:");
        // Memoryless render targets require an Apple-family GPU (Apple2+).
        // Query the device, so Intel builds running through Rosetta also keep
        // the tile-memory optimization when they run on Apple Silicon.
        return RespondsTo(device, Selector("respondsToSelector:"), familySelector) &&
            SupportsFamily(device, familySelector, 1002) ? 3UL : 2UL;
    }

    private const string Library = "/usr/lib/libobjc.A.dylib";
    [LibraryImport(Library, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool RespondsTo(nint device, nint selector, nint argument);
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SupportsFamily(nint device, nint selector, ulong family);
}
