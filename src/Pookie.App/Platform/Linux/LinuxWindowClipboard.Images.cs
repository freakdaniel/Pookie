using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Pookie.App.Platform;

internal sealed partial class LinuxWindowClipboard
{
    private Task<byte[]>? image;
    private readonly List<(SelectionRequest Request, Task<byte[]> Image, long Started)> pendingImages = [];
    private readonly Dictionary<(nuint Window, nuint Property), ImageTransfer> imageTransfers = [];
    private const int ImageChunkSize = 64 * 1024;
    private sealed class ImageTransfer(byte[] bytes, nuint target)
    {
        internal readonly byte[] Bytes = bytes;
        internal readonly nuint Target = target;
        internal int Offset;
        internal long Touched = Environment.TickCount64;
    }

    // Claim ownership in the copy command, before yielding to PNG encoding.
    // XWayland must see a copy from the application's currently focused window.
    internal bool TrySetPng(Task<byte[]> encoded)
    {
        if (display == 0 || window.Handle == 0) return false;
        InstallImageErrorHandler();
        selection = Atom(selectionName);
        image = encoded; text = "";
        selectionTime = ServerTime();
        XSetSelectionOwner(display, selection, (nuint)window.Handle, selectionTime);
        XFlush(display);
        return XGetSelectionOwner(display, selection) == (nuint)window.Handle;
    }

    private void ReplyImage(SelectionRequest request, Task<byte[]> encoded)
    {
        if (!encoded.IsCompleted)
        {
            if (pendingImages.Count < 32) pendingImages.Add((request, encoded, Environment.TickCount64));
            else Notify(request, 0);
            return;
        }
        if (!encoded.IsCompletedSuccessfully || encoded.Result.Length is < 8 or > 16 * 1024 * 1024)
        { Notify(request, 0); return; }
        var bytes = encoded.Result;
        var property = request.Property != 0 ? request.Property : request.Target;
        if (bytes.Length <= ImageDirectLimit())
            XChangeBytes(display, request.Requestor, property, request.Target, 8, 0, bytes, bytes.Length);
        else
        {
            if (imageTransfers.Count >= 16) { Notify(request, 0); return; }
            // Above the server's request limit, use ICCCM INCR: the reader
            // deletes the property to request each next chunk.
            XSelectInput(display, request.Requestor, 1 << 22);
            imageTransfers[(request.Requestor, property)] = new(bytes, request.Target);
            XChangeAtoms(display, request.Requestor, property, Atom("INCR"), 32, 0, [(nuint)bytes.Length], 1);
        }
        Notify(request, property);
    }

    private int ImageDirectLimit()
    {
        // BIG-REQUESTS can carry an ordinary cover in one reply. Honour the
        // server's negotiated limit; unnecessary INCR transactions can stall
        // the XWayland-to-Wayland clipboard bridge on some Mutter versions.
        var words = XExtendedMaxRequestSize(display);
        if (words == 0) words = XMaxRequestSize(display);
        return (int)Math.Min(16 * 1024 * 1024L, Math.Max(0, (long)words * 4 - 256));
    }

    [LibraryImport("libX11.so.6")] private static partial nint XExtendedMaxRequestSize(nint display);
    [LibraryImport("libX11.so.6")] private static partial nint XMaxRequestSize(nint display);

    private void PumpImages()
    {
        var now = Environment.TickCount64;
        for (var index = pendingImages.Count - 1; index >= 0; index--)
        {
            var pending = pendingImages[index];
            if (!pending.Image.IsCompleted && now - pending.Started < 10000) continue;
            pendingImages.RemoveAt(index);
            if (pending.Image.IsCompleted) ReplyImage(pending.Request, pending.Image);
            else Notify(pending.Request, 0);
        }
        foreach (var pair in imageTransfers.Where(pair => now - pair.Value.Touched >= 10000).ToArray())
            imageTransfers.Remove(pair.Key);
    }

    private void ImagePropertyChanged(PropertyEvent property)
    {
        var key = (property.Window, property.Atom);
        if (property.State != 1 || !imageTransfers.TryGetValue(key, out var transfer)) return;
        var count = Math.Min(ImageChunkSize, transfer.Bytes.Length - transfer.Offset);
        var chunk = transfer.Bytes.AsSpan(transfer.Offset, count).ToArray();
        XChangeBytes(display, property.Window, property.Atom, transfer.Target, 8, 0, chunk, count);
        XFlush(display);
        transfer.Offset += count; transfer.Touched = Environment.TickCount64;
        // The zero-length final chunk terminates the INCR transaction.
        if (count == 0) imageTransfers.Remove(key);
    }

    private void DisposeImages()
    {
        foreach (var pending in pendingImages) Notify(pending.Request, 0);
        pendingImages.Clear(); imageTransfers.Clear(); image = null;
    }

    private static readonly ConcurrentDictionary<nint, byte> imageErrorDisplays = new();
    private static readonly object imageErrorLock = new();
    private static readonly ClipboardXErrorHandler imageErrorHandler = HandleImageXError;
    private static ClipboardXErrorHandler? previousImageErrorHandler;
    private static bool imageErrorHandlerInstalled;
    private void InstallImageErrorHandler()
    {
        imageErrorDisplays.TryAdd(display, 0);
        lock (imageErrorLock)
        {
            if (imageErrorHandlerInstalled) return;
            var previous = XSetErrorHandler(imageErrorHandler);
            if (previous != 0) previousImageErrorHandler = Marshal.GetDelegateForFunctionPointer<ClipboardXErrorHandler>(previous);
            imageErrorHandlerInstalled = true;
        }
    }

    private static int HandleImageXError(nint connection, nint error)
    {
        // A paste destination can disappear during an asynchronous transfer.
        // Preserve the existing handler for all other clients/errors.
        if (imageErrorDisplays.ContainsKey(connection) && Marshal.PtrToStructure<ClipboardXError>(error).ErrorCode == 3) return 0;
        return previousImageErrorHandler?.Invoke(connection, error) ?? 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ClipboardXError
    {
        public int Type;
        public nint Display;
        public nuint Resource, Serial;
        public byte ErrorCode, RequestCode, MinorCode;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ClipboardXErrorHandler(nint display, nint error);
    [LibraryImport("libX11.so.6")]
    private static partial nint XSetErrorHandler(ClipboardXErrorHandler handler);
}
