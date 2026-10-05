using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Aprillz.MewUI.Platform;

namespace Pookie.App.Platform;

// GTK owns a separate connection to the desktop. Pump it on the UI thread so
// other applications can request copied text without xclip/wl-copy subprocesses.
internal sealed class GtkClipboardService : IClipboardService
{
    private readonly nint clipboard;
    private readonly nint selection;
    private ReadRequest? pendingRead;
    private static readonly TextReceivedCallback textReceived = OnTextReceived;

    private GtkClipboardService(nint clipboard, nint selection)
    {
        this.clipboard = clipboard;
        this.selection = selection;
    }

    public static GtkClipboardService? TryCreate(string selectionName = "CLIPBOARD")
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            // MewUI currently uses X11, including XWayland on Wayland desktops.
            // Use the same display and selection ownership model for GTK.
            gdk_set_allowed_backends("x11");
            if (gtk_init_check(0, 0) == 0) return null;
            var atom = gdk_atom_intern(selectionName, 0);
            var clipboard = gtk_clipboard_get(atom);
            return clipboard == 0 ? null : new(clipboard, atom);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    public bool TrySetText(string text)
    {
        gtk_clipboard_set_text(clipboard, text, Encoding.UTF8.GetByteCount(text));
        return true;
    }

    public bool HasText() => gdk_selection_owner_get(selection) != 0;

    public bool TryGetText(out string text)
    {
        // Asynchronous requests let us bound an unresponsive clipboard owner.
        // GTK always calls the callback, even when no text is available. Its
        // GCHandle remains alive after a timeout until that callback arrives.
        var request = pendingRead;
        if (request == null)
        {
            pendingRead = request = new();
            var handle = GCHandle.Alloc(request);
            gtk_clipboard_request_text(clipboard, textReceived, GCHandle.ToIntPtr(handle));
        }
        var timeout = Stopwatch.StartNew();
        while (!request.Completed && timeout.ElapsedMilliseconds < 1500)
        {
            Pump();
            if (!request.Completed) Thread.Sleep(1);
        }
        if (request.Completed) pendingRead = null;
        text = request.Text ?? "";
        return request.Completed && request.Text != null;
    }

    public void Pump()
    {
        for (var count = 0; count < 32 && g_main_context_iteration(0, 0) != 0; count++) { }
    }

    private static void OnTextReceived(nint clipboard, nint text, nint userData)
    {
        var handle = GCHandle.FromIntPtr(userData);
        try
        {
            var request = (ReadRequest)handle.Target!;
            request.Text = text == 0 ? null : Marshal.PtrToStringUTF8(text);
            request.Completed = true;
        }
        finally { handle.Free(); }
    }

    private sealed class ReadRequest
    {
        public bool Completed;
        public string? Text;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TextReceivedCallback(nint clipboard, nint text, nint userData);
    [DllImport("libgtk-3.so.0")] private static extern int gtk_init_check(nint argc, nint argv);
    [DllImport("libgtk-3.so.0")] private static extern nint gtk_clipboard_get(nint selection);
    [DllImport("libgtk-3.so.0")] private static extern void gtk_clipboard_set_text(nint clipboard, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int length);
    [DllImport("libgtk-3.so.0")] private static extern void gtk_clipboard_request_text(nint clipboard, TextReceivedCallback callback, nint userData);
    [DllImport("libgdk-3.so.0")] private static extern nint gdk_atom_intern([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport("libgdk-3.so.0")] private static extern void gdk_set_allowed_backends([MarshalAs(UnmanagedType.LPUTF8Str)] string backends);
    [DllImport("libgdk-3.so.0")] private static extern nint gdk_selection_owner_get(nint selection);
    [DllImport("libglib-2.0.so.0")] private static extern int g_main_context_iteration(nint context, int mayBlock);
}
