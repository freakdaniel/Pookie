using System.Runtime.InteropServices;
using System.Text;
using Pookie.App.Messages;

namespace Pookie.App.Platform;

internal sealed record ChatClipboardData(string[] Files, byte[]? Image = null, string Extension = ".png");

internal static class ChatClipboard
{
    internal static string[] FileUris(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile &&
            (uri.Host.Length == 0 || uri.IsLoopback) && uri.Query.Length == 0 && uri.Fragment.Length == 0)
        .Select(line => new Uri(line).LocalPath).Distinct().ToArray();

    internal static async Task<ChatClipboardData?> ReadAsync(CancellationToken token, string selection = "CLIPBOARD")
    {
        if (OperatingSystem.IsLinux()) return await ReadGtkAsync(selection, token);
        if (OperatingSystem.IsWindows())
        {
            var (data, dib) = ReadWindows();
            if (dib != null)
                return new([], await Task.Run(() => ClipboardBitmap.PngFromDib(dib), token));
            return data;
        }
        return null;
    }

    private static async Task<ChatClipboardData?> ReadGtkAsync(string selection, CancellationToken token)
    {
        try
        {
            if (gtk_init_check(0, 0) == 0) return null;
            var clipboard = gtk_clipboard_get(gdk_atom_intern(selection, 0));
            var targets = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            gtk_clipboard_request_targets(clipboard, targetsReceived, GCHandle.ToIntPtr(GCHandle.Alloc(targets)));
            var formats = await AwaitGtkAsync(targets.Task, token) ?? [];
            foreach (var format in new[] { "text/uri-list", "x-special/gnome-copied-files", "image/png", "image/jpeg", "image/gif", "image/webp" })
            {
                if (!formats.Contains(format)) continue;
                var result = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
                gtk_clipboard_request_contents(clipboard, gdk_atom_intern(format, 0), contentsReceived, GCHandle.ToIntPtr(GCHandle.Alloc(result)));
                var bytes = await AwaitGtkAsync(result.Task, token);
                if (bytes == null) continue;
                if (format.StartsWith("image/", StringComparison.Ordinal))
                    return new([], bytes, format switch { "image/jpeg" => ".jpg", "image/gif" => ".gif", "image/webp" => ".webp", _ => ".png" });
                var paths = FileUris(Encoding.UTF8.GetString(bytes));
                if (paths.Length > 0) return new(paths);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { }
        return null;
    }

    private static async Task<T?> AwaitGtkAsync<T>(Task<T> task, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 1500;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            for (var i = 0; i < 32 && g_main_context_iteration(0, 0) != 0; i++) { }
            if (!task.IsCompleted) await Task.Delay(10, token);
        }
        // A timed-out native callback still owns its handle until GTK replies.
        return task.IsCompleted ? await task : default;
    }

    private static readonly TargetsCallback targetsReceived = OnTargets;
    private static readonly ContentsCallback contentsReceived = OnContents;
    private static void OnTargets(nint clipboard, nint atoms, int count, nint state)
    {
        var handle = GCHandle.FromIntPtr(state);
        try
        {
            var formats = new List<string>();
            for (var i = 0; i < Math.Clamp(count, 0, 256); i++)
            {
                var name = gdk_atom_name(Marshal.ReadIntPtr(atoms, i * IntPtr.Size));
                if (name == 0) continue;
                try { if (Marshal.PtrToStringUTF8(name) is { } format) formats.Add(format); }
                finally { g_free(name); }
            }
            ((TaskCompletionSource<string[]>)handle.Target!).TrySetResult(formats.ToArray());
        }
        catch (Exception error) { ((TaskCompletionSource<string[]>)handle.Target!).TrySetException(error); }
        finally { handle.Free(); }
    }
    private static void OnContents(nint clipboard, nint selection, nint state)
    {
        var handle = GCHandle.FromIntPtr(state);
        try
        {
            byte[]? bytes = null;
            var length = gtk_selection_data_get_length(selection);
            var data = gtk_selection_data_get_data(selection);
            if (length > ChatMediaCrypto.MaxFileBytes) throw new InvalidOperationException("Изображение или список файлов в буфере превышает 16 МБ.");
            if (data != 0 && gtk_selection_data_get_format(selection) == 8 && length is > 0 and <= ChatMediaCrypto.MaxFileBytes)
            { bytes = new byte[length]; Marshal.Copy(data, bytes, 0, length); }
            ((TaskCompletionSource<byte[]?>)handle.Target!).TrySetResult(bytes);
        }
        catch (Exception error) { ((TaskCompletionSource<byte[]?>)handle.Target!).TrySetException(error); }
        finally { handle.Free(); }
    }

    private static (ChatClipboardData? Data, byte[]? Dib) ReadWindows()
    {
        if (!OpenClipboard(0)) return default;
        try
        {
            if (IsClipboardFormatAvailable(15))
            {
                var drop = GetClipboardData(15); var count = DragQueryFileW(drop, uint.MaxValue, null, 0);
                if (count > ChatMediaCrypto.MaxAttachments) throw new InvalidOperationException("За раз можно прикрепить до четырёх файлов.");
                var paths = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    var length = DragQueryFileW(drop, i, null, 0);
                    if (length > 32768) continue;
                    var path = new StringBuilder((int)length + 1); DragQueryFileW(drop, i, path, (uint)path.Capacity); paths.Add(path.ToString());
                }
                if (paths.Count > 0) return (new(paths.ToArray()), null);
            }
            var png = RegisterClipboardFormatW("PNG");
            if (CopyWindowsBytes(png, ChatMediaCrypto.MaxFileBytes) is { } bytes) return (new([], bytes), null);
            var dib = CopyWindowsBytes(17, 64 * 1024 * 1024) ?? CopyWindowsBytes(8, 64 * 1024 * 1024);
            return (null, dib);
        }
        finally { CloseClipboard(); }
    }
    private static byte[]? CopyWindowsBytes(uint format, int limit)
    {
        if (!IsClipboardFormatAvailable(format)) return null;
        var handle = GetClipboardData(format); var length = GlobalSize(handle);
        if (length == 0) return null;
        if (length > (nuint)limit) throw new InvalidOperationException("Изображение в буфере слишком большое.");
        var pointer = GlobalLock(handle); if (pointer == 0) return null;
        try { var data = new byte[(int)length]; Marshal.Copy(pointer, data, 0, data.Length); return data; }
        finally { GlobalUnlock(handle); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TargetsCallback(nint clipboard, nint atoms, int count, nint state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ContentsCallback(nint clipboard, nint selection, nint state);
    [DllImport("libgtk-3.so.0")] private static extern int gtk_init_check(nint argc, nint argv);
    [DllImport("libgtk-3.so.0")] private static extern nint gtk_clipboard_get(nint selection);
    [DllImport("libgtk-3.so.0")] private static extern void gtk_clipboard_request_targets(nint clipboard, TargetsCallback callback, nint state);
    [DllImport("libgtk-3.so.0")] private static extern void gtk_clipboard_request_contents(nint clipboard, nint target, ContentsCallback callback, nint state);
    [DllImport("libgtk-3.so.0")] private static extern int gtk_selection_data_get_length(nint data);
    [DllImport("libgtk-3.so.0")] private static extern int gtk_selection_data_get_format(nint data);
    [DllImport("libgtk-3.so.0")] private static extern nint gtk_selection_data_get_data(nint data);
    [DllImport("libgdk-3.so.0")] private static extern nint gdk_atom_intern([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport("libgdk-3.so.0")] private static extern nint gdk_atom_name(nint atom);
    [DllImport("libglib-2.0.so.0")] private static extern void g_free(nint pointer);
    [DllImport("libglib-2.0.so.0")] private static extern int g_main_context_iteration(nint context, int mayBlock);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(nint drop, uint index, StringBuilder? path, uint size);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
}
