using System.Runtime.InteropServices;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyEmbeddedNativeGeometry()
    {
        if (!OperatingSystem.IsLinux()) return;
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for browser geometry verification.");
        nint children = 0;
        try
        {
            var bounds = embeddedLogin!.TranslateRect(new(0, 0, embeddedLogin.ActualWidth, embeddedLogin.ActualHeight), Window);
            var scale = Window.DpiScale;
            if (EmbeddedXQueryTree(display, Window.Handle, out _, out _, out children, out var count) == 0)
                throw new InvalidOperationException("Could not inspect native login children.");
            for (var i = 0; i < count; i++)
            {
                var child = Marshal.ReadIntPtr(children, i * nint.Size);
                if (EmbeddedXGetGeometry(display, child, out _, out var x, out var y, out var width, out var height, out _, out _) == 0 ||
                    Math.Abs(x - bounds.X * scale) > 2 || Math.Abs(y - bounds.Y * scale) > 2) continue;
                if (Math.Abs(width - bounds.Width * scale) > 2 || Math.Abs(height - bounds.Height * scale) > 2)
                    throw new InvalidOperationException($"Native browser escaped its viewport: {width}x{height}, expected {bounds.Width * scale}x{bounds.Height * scale} physical pixels.");
                return;
            }
            throw new InvalidOperationException("No native browser child at the displayed viewport.");
        }
        finally { if (children != 0) EmbeddedXFree(children); XCloseDisplay(display); }
    }

    [DllImport("libX11.so.6", EntryPoint = "XQueryTree")] private static extern int EmbeddedXQueryTree(nint display, nint window, out nint root, out nint parent, out nint children, out uint count);
    [DllImport("libX11.so.6", EntryPoint = "XGetGeometry")] private static extern int EmbeddedXGetGeometry(nint display, nint drawable, out nint root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport("libX11.so.6", EntryPoint = "XFree")] private static extern int EmbeddedXFree(nint data);
}
