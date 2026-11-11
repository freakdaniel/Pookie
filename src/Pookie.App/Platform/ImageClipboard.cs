using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Pookie.Media.MacOS;

namespace Pookie.App.Platform;

internal static partial class ImageClipboard
{
    private static bool ownsGtkImage;
    internal static void Pump()
    {
        if (!ownsGtkImage) return;
        // MewUI's Linux window loop owns a separate X11 connection. GTK's image
        // selection must also dispatch requests from the destination application.
        for (var index = 0; index < 16 && g_main_context_iteration(0, 0) != 0; index++) { }
    }
    internal static bool TrySet(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 16 * 1024 * 1024 || bgra.Length != (long)width * height * 4) return false;
        try
        {
            var dib = new byte[40 + bgra.Length];
            BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
            BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
            BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -height);
            BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
            bgra.CopyTo(dib, 40);
            if (OperatingSystem.IsLinux()) return TrySetLinuxPng(ClipboardBitmap.PngFromDib(dib), "CLIPBOARD");
            if (OperatingSystem.IsWindows()) return SetWindows(dib);
            if (OperatingSystem.IsMacOS())
            {
                using var pool = new ObjC.Pool();
                var png = ClipboardBitmap.PngFromDib(dib);
                var data = ObjC.Data(ObjC.objc_getClass("NSData"), ObjC.S("dataWithBytes:length:"), png, (nuint)png.Length);
                var pasteboard = ObjC.Send(ObjC.objc_getClass("NSPasteboard"), ObjC.S("generalPasteboard"));
                ObjC.Send(pasteboard, ObjC.S("clearContents"));
                return SetPasteboardData(pasteboard, ObjC.S("setData:forType:"), data, ObjC.String("public.png"));
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or InvalidDataException) { }
        return false;
    }

    private static bool SetWindows(byte[] dib)
    {
        var owner = GetActiveWindow();
        if (owner == 0 || !OpenClipboard(owner)) return false;
        nint memory = 0;
        try
        {
            memory = GlobalAlloc(2, (nuint)dib.Length);
            if (memory == 0) return false;
            var pointer = GlobalLock(memory);
            if (pointer == 0) return false;
            try { Marshal.Copy(dib, 0, pointer, dib.Length); }
            finally { GlobalUnlock(memory); }
            if (!EmptyClipboard() || SetClipboardData(8, memory) == 0) return false;
            memory = 0; // CF_DIB ownership transfers to Windows only on success.
            return true;
        }
        finally { if (memory != 0) GlobalFree(memory); CloseClipboard(); }
    }

    // Offer exactly the encoded PNG. gtk_clipboard_set_image advertises lossy
    // alternatives too; its JPEG serializer can lose colour on some desktops.
    internal static bool TrySetLinuxPng(byte[] png, string selection)
    {
        if (!OperatingSystem.IsLinux()) return false;
        if (gtk_init_check(0, 0) == 0) return false;
        var target = new ClipboardTarget { Name = Marshal.StringToCoTaskMemUTF8("image/png") };
        var state = GCHandle.Alloc(png.ToArray());
        var transferred = false;
        try
        {
            var clipboard = gtk_clipboard_get(gdk_atom_intern(selection, 0));
            if (clipboard == 0 || gtk_clipboard_set_with_data(clipboard, ref target, 1, providePng, clearPng, GCHandle.ToIntPtr(state)) == 0)
            {
                return false;
            }
            transferred = true;
            ownsGtkImage = true;
            gtk_clipboard_set_can_store(clipboard, ref target, 1);
            gtk_clipboard_store(clipboard);
            return true; // GTK's clear callback now owns the handle.
        }
        finally { if (!transferred) state.Free(); Marshal.FreeCoTaskMem(target.Name); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ClipboardTarget { public nint Name; public uint Flags, Info; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ProvidePng(nint clipboard, nint selection, uint info, nint state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ClearPng(nint clipboard, nint state);
    private static readonly ProvidePng providePng = (_, selection, _, state) =>
    {
        if (GCHandle.FromIntPtr(state).Target is byte[] bytes)
            gtk_selection_data_set(selection, gdk_atom_intern("image/png", 0), 8, bytes, bytes.Length);
    };
    private static readonly ClearPng clearPng = (_, state) => GCHandle.FromIntPtr(state).Free();

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenClipboard(nint owner);
    [LibraryImport("user32.dll")] private static partial nint GetActiveWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseClipboard();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll")] private static partial nint SetClipboardData(uint format, nint memory);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalAlloc(uint flags, nuint size);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalLock(nint memory);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalFree(nint memory);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)] private static partial bool SetPasteboardData(nint obj, nint selector, nint data, nint type);
    [LibraryImport("libgtk-3.so.0")] private static partial int gtk_init_check(nint argc, nint argv);
    [LibraryImport("libgtk-3.so.0")] private static partial nint gtk_clipboard_get(nint selection);
    [LibraryImport("libgtk-3.so.0")] private static partial int gtk_clipboard_set_with_data(nint clipboard, ref ClipboardTarget targets, uint count, ProvidePng provide, ClearPng clear, nint state);
    [LibraryImport("libgtk-3.so.0")] private static partial void gtk_clipboard_set_can_store(nint clipboard, ref ClipboardTarget targets, int count);
    [LibraryImport("libgtk-3.so.0")] private static partial void gtk_selection_data_set(nint selection, nint type, int format, byte[] bytes, int length);
    [LibraryImport("libgtk-3.so.0")] private static partial void gtk_clipboard_store(nint clipboard);
    [LibraryImport("libgdk-3.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint gdk_atom_intern(string name, int onlyIfExists);
    [LibraryImport("libglib-2.0.so.0")] private static partial int g_main_context_iteration(nint context, int mayBlock);
}
