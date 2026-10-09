using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Pookie.App.Platform;

// Own the selection with the visible application window. In XWayland this lets
// the compositor associate a copy with the focused application instead of GTK's
// separate, hidden clipboard window. A dedicated X connection receives selection
// requests that the MewUI event dispatcher otherwise drops. GTK is an optional
// read fallback: a Wayland-only GDK_BACKEND must never disable system copying.
internal sealed class LinuxWindowClipboard : IClipboardService, IDisposable
{
    private readonly Window window;
    private readonly GtkClipboardService? reader;
    private readonly string selectionName;
    private nint display;
    private nuint selection, selectionTime, requestWindow;
    private string text = "";

    internal LinuxWindowClipboard(Window window, GtkClipboardService? reader, string selectionName = "CLIPBOARD")
    {
        this.window = window; this.reader = reader; this.selectionName = selectionName;
        display = XOpenDisplay(0);
    }

    internal LinuxWindowClipboard ForSelection(GtkClipboardService? clipboardReader, string name) =>
        new(window, clipboardReader, name);

    public bool TrySetText(string value)
    {
        if (display == 0 || window.Handle == 0) return reader?.TrySetText(value) == true;
        selection = Atom(selectionName);
        text = value;
        selectionTime = ServerTime();
        XSetSelectionOwner(display, selection, (nuint)window.Handle, selectionTime);
        XFlush(display);
        return XGetSelectionOwner(display, selection) == (nuint)window.Handle;
    }

    public bool HasText() => OwnsSelection() ? text.Length > 0 : display != 0 &&
        XGetSelectionOwner(display, Atom(selectionName)) != 0;

    public bool TryGetText(out string value)
    {
        if (OwnsSelection()) { value = text; return true; }
        if (display != 0 && ReadSelection("UTF8_STRING", out value)) return true;
        if (reader != null && reader.TryGetText(out value)) return true;
        if (display != 0 && ReadSelection("STRING", out value)) return true;
        value = ""; return false;
    }

    private bool ReadSelection(string targetName, out string value)
    {
        value = "";
        if (requestWindow == 0)
            requestWindow = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 1, 1, 0, 0, 0);
        var requestedSelection = Atom(selectionName);
        if (XGetSelectionOwner(display, requestedSelection) == 0) return false;
        var target = Atom(targetName); var property = Atom("_POOKIE_CLIPBOARD_READ");
        XDeleteProperty(display, requestWindow, property);
        XConvertSelection(display, requestedSelection, target, property, requestWindow, 0);
        XFlush(display);
        var buffer = Marshal.AllocHGlobal(24 * IntPtr.Size);
        try
        {
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 1500)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, buffer);
                    var type = Marshal.ReadInt32(buffer);
                    if (type == 30) Reply(Marshal.PtrToStructure<SelectionRequest>(buffer));
                    if (type != 31) continue;
                    var notify = Marshal.PtrToStructure<SelectionNotify>(buffer);
                    if (notify.Requestor != requestWindow || notify.Selection != requestedSelection || notify.Target != target) continue;
                    if (notify.Property == 0) return false;
                    var result = XGetWindowProperty(display, requestWindow, property, 0, 262144, 1, 0,
                        out var actualType, out var format, out var count, out var remaining, out var data);
                    try
                    {
                        if (result != 0 || actualType != target || format != 8 || remaining != 0 || count > 1048576) return false;
                        var bytes = new byte[(int)count];
                        if (bytes.Length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
                        value = (targetName == "STRING" ? Encoding.Latin1 : Encoding.UTF8).GetString(bytes);
                        return true;
                    }
                    finally { if (data != 0) XFree(data); }
                }
                Thread.Sleep(1);
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private bool OwnsSelection() => display != 0 && selection != 0 && window.Handle != 0 &&
        XGetSelectionOwner(display, selection) == (nuint)window.Handle;

    private nuint Atom(string name) => XInternAtom(display, name, 0);

    private nuint ServerTime()
    {
        // Core input events may be absent with XInput2, and async URL resolution
        // can outlive the click timestamp. Request a fresh server timestamp.
        var property = Atom("_POOKIE_CLIPBOARD_TIMESTAMP");
        XSelectInput(display, (nuint)window.Handle, 1 << 22); // PropertyChangeMask
        XChangeBytes(display, (nuint)window.Handle, property, Atom("INTEGER"), 8, 0, [0], 1);
        XSync(display, 0);
        var buffer = Marshal.AllocHGlobal(24 * IntPtr.Size);
        try
        {
            while (XCheckTypedWindowEvent(display, (nuint)window.Handle, 28, buffer) != 0)
            {
                var ev = Marshal.PtrToStructure<PropertyEvent>(buffer);
                if (ev.Atom == property) return ev.Time;
            }
            return 0; // CurrentTime if the server supplied no notification.
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal void Pump()
    {
        if (display == 0 || XPending(display) == 0) return;
        var buffer = Marshal.AllocHGlobal(24 * IntPtr.Size);
        try
        {
            for (var count = 0; count < 32 && XPending(display) > 0; count++)
            {
                XNextEvent(display, buffer);
                if (Marshal.ReadInt32(buffer) == 30)
                    Reply(Marshal.PtrToStructure<SelectionRequest>(buffer));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void Reply(SelectionRequest request)
    {
        if (selection == 0 || request.Selection != selection || request.Owner != (nuint)window.Handle) return;
        var property = request.Property != 0 ? request.Property : request.Target;
        var supported = true;
        if (request.Target == Atom("TARGETS"))
        {
            nuint[] targets = [Atom("TARGETS"), Atom("TIMESTAMP"), Atom("UTF8_STRING"), Atom("text/plain;charset=utf-8"), Atom("text/plain"), Atom("TEXT"), Atom("STRING")];
            XChangeAtoms(display, request.Requestor, property, Atom("ATOM"), 32, 0, targets, targets.Length);
        }
        else if (request.Target == Atom("TIMESTAMP"))
            XChangeAtoms(display, request.Requestor, property, Atom("INTEGER"), 32, 0, [selectionTime], 1);
        else if (request.Target == Atom("UTF8_STRING") || request.Target == Atom("text/plain;charset=utf-8") ||
                 request.Target == Atom("text/plain") || request.Target == Atom("TEXT") || request.Target == Atom("STRING"))
        {
            var bytes = (request.Target == Atom("STRING") ? Encoding.Latin1 : Encoding.UTF8).GetBytes(text);
            XChangeBytes(display, request.Requestor, property, request.Target == Atom("TEXT") ? Atom("UTF8_STRING") : request.Target, 8, 0, bytes, bytes.Length);
        }
        else supported = false;
        var notify = new SelectionNotify { Type = 31, Display = display, Requestor = request.Requestor,
            Selection = request.Selection, Target = request.Target, Property = supported ? property : 0, Time = request.Time };
        var buffer = Marshal.AllocHGlobal(24 * IntPtr.Size);
        try
        {
            Marshal.Copy(new byte[24 * IntPtr.Size], 0, buffer, 24 * IntPtr.Size);
            Marshal.StructureToPtr(notify, buffer, false);
            XSendEvent(display, request.Requestor, 0, 0, buffer); XFlush(display);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        if (display != 0)
        {
            if (requestWindow != 0) { XDestroyWindow(display, requestWindow); requestWindow = 0; }
            XCloseDisplay(display); display = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyEvent { public int Type; public nuint Serial; public int SendEvent; public nint Display; public nuint Window, Atom, Time; public int State; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SelectionRequest { public int Type; public nuint Serial; public int SendEvent; public nint Display; public nuint Owner, Requestor, Selection, Target, Property, Time; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SelectionNotify { public int Type; public nuint Serial; public int SendEvent; public nint Display; public nuint Requestor, Selection, Target, Property, Time; }
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);
    [DllImport("libX11.so.6")] private static extern int XDestroyWindow(nint display, nuint window);
    [DllImport("libX11.so.6")] private static extern int XDeleteProperty(nint display, nuint window, nuint property);
    [DllImport("libX11.so.6")] private static extern int XConvertSelection(nint display, nuint selection, nuint target, nuint property, nuint requestor, nuint time);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint requestedType,
        out nuint actualType, out int format, out nuint count, out nuint remaining, out nint data);
    [DllImport("libX11.so.6")] private static extern int XFree(nint data);
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XSelectInput(nint display, nuint window, nint mask);
    [DllImport("libX11.so.6")] private static extern int XSync(nint display, int discard);
    [DllImport("libX11.so.6")] private static extern int XCheckTypedWindowEvent(nint display, nuint window, int type, nint ev);
    [DllImport("libX11.so.6")] private static extern int XPending(nint display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(nint display, nint ev);
    [DllImport("libX11.so.6")] private static extern nuint XInternAtom(nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);
    [DllImport("libX11.so.6")] private static extern nuint XGetSelectionOwner(nint display, nuint selection);
    [DllImport("libX11.so.6", EntryPoint = "XChangeProperty")] private static extern int XChangeBytes(nint display, nuint window, nuint property, nuint type, int format, int mode, byte[] data, int count);
    [DllImport("libX11.so.6", EntryPoint = "XChangeProperty")] private static extern int XChangeAtoms(nint display, nuint window, nuint property, nuint type, int format, int mode, nuint[] data, int count);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(nint display, nuint window, int propagate, nint mask, nint ev);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
}
