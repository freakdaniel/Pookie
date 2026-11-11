using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using InfiniFrame;

namespace Pookie.App.Browser;

// InfiniFrame 0.62 exposes minimize/focus, but no hide API. Use its public OS handle.
internal static partial class BrowserWindowVisibility
{
    private sealed record ServicePlacement(int Left, int Top, nint Style);
    private static readonly ConditionalWeakTable<IInfiniFrameWindow, ServicePlacement> services = new();

    // WebView2 defers MSE attachment in an SW_HIDE window. A visible off-screen
    // service window keeps its media element active without a taskbar item or focus.
    public static void UseForPlayback(IInfiniFrameWindow window)
    {
        if (!OperatingSystem.IsWindows()) { Set(window, false); return; }
        var handle = window.WindowHandle;
        if (handle == 0) return;
        if (!services.TryGetValue(window, out _))
        {
            if (!GetWindowRect(handle, out var rect)) throw new InvalidOperationException("Не удалось подготовить окно браузерного проигрывателя.");
            services.Add(window, new(rect.Left, rect.Top, GetWindowLongPtr(handle, -20)));
        }
        Set(window, false);
    }

    public static void Set(IInfiniFrameWindow window, bool visible)
    {
        var handle = window.WindowHandle;
        if (handle == 0) return;
        if (visible) window.Features.State.SetMinimized(false);
        if (OperatingSystem.IsWindows())
        {
            if (services.TryGetValue(window, out var placement))
            {
                _ = SetWindowLongPtr(handle, -20, visible ? placement.Style : (placement.Style | 0x80) & ~0x40000);
                _ = SetWindowPos(handle, 0, visible ? placement.Left : -32000, visible ? placement.Top : -32000, 0, 0, 0x15);
                if (!visible) { _ = ShowWindow(handle, 4); return; } // SW_SHOWNOACTIVATE
            }
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtr(nint window, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static partial nint SetWindowLongPtr(nint window, int index, nint value);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static partial nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void Send(nint receiver, nint selector, nint argument);
    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_widget_show_all")] private static partial void GtkShow(nint window);
    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_widget_hide")] private static partial void GtkHide(nint window);
    [LibraryImport("libgtk-3.so.0", EntryPoint = "gtk_window_present")] private static partial void GtkPresent(nint window);
}
