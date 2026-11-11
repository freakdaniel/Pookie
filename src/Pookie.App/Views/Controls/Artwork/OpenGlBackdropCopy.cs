using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Copy the part of the window already painted below the popup. A framebuffer
// blit can downsample in the same operation without transferring pixels to CPU.
internal static unsafe partial class OpenGlBackdropCopy
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "FlushQueuedDraws")]
    private static extern void FlushQueuedDraws(
        [UnsafeAccessorType("Aprillz.MewUI.Rendering.MewVG.MewVGX11GraphicsContext, Aprillz.MewUI.Backend.MewVG.X11")] object context);

    internal static bool TryCopy(IGraphicsContext context, IRenderSurface destination, Rect pixels)
    {
        if (!OperatingSystem.IsLinux() || GetCurrentContext() == 0 || destination is not IExternalWritableGpuSurface writable)
            return false;
        if (context.GetType().FullName != "Aprillz.MewUI.Rendering.MewVG.MewVGX11GraphicsContext") return false;

        // MewVG queues drawing until flush. This is its own mid-frame flush,
        // which preserves the transform and clip for the commands that follow.
        FlushQueuedDraws(context);
        var draw = Get(0x8CA6);
        var read = Get(0x8CAA);
        var texture = Get(0x8069);
        int* viewport = stackalloc int[4];
        GetIntegers(0x0BA2, viewport);
        if (pixels.Right > viewport[2] || pixels.Bottom > viewport[3] || Get(0x80A9) > 0) return false;
        var scissor = IsEnabled(0x0C11);
        try
        {
            using var write = writable.BeginExternalWrite();
            BindFramebuffer(0x8CA8, (uint)draw);
            Disable(0x0C11);
            Blit((int)pixels.X, viewport[3] - (int)pixels.Bottom,
                (int)pixels.Right, viewport[3] - (int)pixels.Y,
                0, 0, destination.PixelWidth, destination.PixelHeight, 0x4000, 0x2601);
        }
        finally
        {
            BindFramebuffer(0x8CA9, (uint)draw);
            BindFramebuffer(0x8CA8, (uint)read);
            Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            BindTexture(0x0DE1, (uint)texture);
            if (scissor != 0) Enable(0x0C11);
        }
        writable.MarkExternalContentChanged();
        return true;
    }

    internal static int Get(uint name) { int value = 0; GetIntegers(name, &value); return value; }
    private const string Library = "libGL.so.1";
    [LibraryImport(Library, EntryPoint = "glXGetCurrentContext")] internal static partial nint GetCurrentContext();
    [LibraryImport(Library, EntryPoint = "glGetIntegerv")] internal static partial void GetIntegers(uint name, int* value);
    [LibraryImport(Library, EntryPoint = "glIsEnabled")] internal static partial byte IsEnabled(uint capability);
    [LibraryImport(Library, EntryPoint = "glDisable")] internal static partial void Disable(uint capability);
    [LibraryImport(Library, EntryPoint = "glEnable")] internal static partial void Enable(uint capability);
    [LibraryImport(Library, EntryPoint = "glBindFramebuffer")] internal static partial void BindFramebuffer(uint target, uint framebuffer);
    [LibraryImport(Library, EntryPoint = "glBindTexture")] internal static partial void BindTexture(uint target, uint texture);
    [LibraryImport(Library, EntryPoint = "glViewport")] internal static partial void Viewport(int x, int y, int width, int height);
    [LibraryImport(Library, EntryPoint = "glDrawArrays")] internal static partial void DrawArrays(uint mode, int first, int count);
    [LibraryImport(Library, EntryPoint = "glColorMask")] internal static partial void ColorMask(byte red, byte green, byte blue, byte alpha);
    [LibraryImport(Library, EntryPoint = "glBlitFramebuffer")] private static partial void Blit(
        int left, int bottom, int right, int top, int destLeft, int destBottom, int destRight, int destTop, uint mask, uint filter);
}
