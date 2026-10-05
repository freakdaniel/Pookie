using System.Runtime.InteropServices;
using InfiniFrame;

namespace Pookie.App.Auth;

// InfiniFrame 0.62 exposes minimize/focus, but no hide API. Use its public OS handle.
internal static class BrowserWindowVisibility
{
    public static void Set(IInfiniFrameWindow window, bool visible)
    {
        var handle = window.WindowHandle;
        if (handle == 0) return;
        if (visible) window.Features.State.SetMinimized(false);
        if (OperatingSystem.IsWindows())
        {
            _ = ShowWindow(handle, visible ? 9 : 0); // SW_RESTORE / SW_HIDE
            if (visible) _ = SetForegroundWindow(handle);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Send(handle, Selector(visible ? "makeKeyAndOrderFront:" : "orderOut:"), 0);
        }
        else if (OperatingSystem.IsLinux())
        {
            if (visible) { GtkShow(handle); GtkPresent(handle); }
            else GtkHide(handle);
        }
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void Send(nint receiver, nint selector, nint argument);
    [DllImport("libgtk-3.so.0", EntryPoint = "gtk_widget_show_all")] private static extern void GtkShow(nint window);
    [DllImport("libgtk-3.so.0", EntryPoint = "gtk_widget_hide")] private static extern void GtkHide(nint window);
    [DllImport("libgtk-3.so.0", EntryPoint = "gtk_window_present")] private static extern void GtkPresent(nint window);
}
