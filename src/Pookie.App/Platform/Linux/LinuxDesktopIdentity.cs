using System.Runtime.InteropServices;
using System.Text;
using StbImageSharp;

namespace Pookie.App.Platform;

internal static partial class LinuxDesktopIdentity
{
    internal const string AppId = "io.github.freakdaniel.Pookie";

    internal static void Apply(nint window)
    {
        if (!OperatingSystem.IsLinux() || window == 0) return;
        var display = XOpenDisplay(0);
        if (display == 0) return;
        try
        {
            // Both instance and class match the exported desktop file so X11
            // and XWayland group the window under the installed app icon.
            var value = Encoding.UTF8.GetBytes(AppId + '\0' + AppId + '\0');
            XChangeProperty(display, (nuint)window, XInternAtom(display, "WM_CLASS", 0),
                31 /* XA_STRING */, 8, 0 /* PropModeReplace */, value, value.Length);
            SetIcon(display, (nuint)window);
            XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }

    private static void SetIcon(nint display, nuint window)
    {
        using var stream = typeof(LinuxDesktopIdentity).Assembly.GetManifestResourceStream("Pookie.App.Assets.Branding.app-icon-128.png")
            ?? throw new InvalidOperationException("Встроенная иконка приложения не найдена.");
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        // Xlib uses native unsigned longs even for a 32-bit CARDINAL property.
        var pixels = new nuint[2 + image.Width * image.Height];
        pixels[0] = (nuint)image.Width; pixels[1] = (nuint)image.Height;
        for (var index = 0; index < image.Width * image.Height; index++)
        {
            var offset = index * 4;
            pixels[index + 2] = (nuint)((uint)image.Data[offset + 3] << 24 |
                (uint)image.Data[offset] << 16 | (uint)image.Data[offset + 1] << 8 | image.Data[offset + 2]);
        }
        XChangeCardinals(display, window, XInternAtom(display, "_NET_WM_ICON", 0),
            6 /* XA_CARDINAL */, 32, 0, pixels, pixels.Length);
    }

    [LibraryImport("libX11.so.6")] private static partial nint XOpenDisplay(nint name);
    [LibraryImport("libX11.so.6")] private static partial int XCloseDisplay(nint display);
    [LibraryImport("libX11.so.6")] private static partial int XFlush(nint display);
    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);
    [LibraryImport("libX11.so.6")]
    private static partial int XChangeProperty(nint display, nuint window, nuint property, nuint type,
        int format, int mode, byte[] data, int count);
    [LibraryImport("libX11.so.6", EntryPoint = "XChangeProperty")]
    private static partial int XChangeCardinals(nint display, nuint window, nuint property, nuint type,
        int format, int mode, nuint[] data, int count);
}
