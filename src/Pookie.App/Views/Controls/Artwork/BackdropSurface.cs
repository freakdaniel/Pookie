using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal static class BackdropSurface
{
    // Direct2D filter intermediates share the thread's device context. Nested
    // header captures are read before the enclosing GPU frame has completed,
    // so a CPU readback can cache an empty image. Independent DC surfaces end
    // their own draw before the next sampling stage reads them.
    internal static bool UsesCpuCapture => OperatingSystem.IsWindows() && Win32BackdropBlur.CurrentContextIdentity == 0;

    internal static IRenderSurface Create(IGraphicsFactory factory, int width, int height, double scale, string name) =>
        factory.CreateSurface(UsesCpuCapture
            ? RenderSurfaceDescriptor.CpuPixels(width, height, scale, debugName: name)
            : RenderSurfaceDescriptor.FilterIntermediate(width, height, scale, debugName: name));

    // DC pixels are premultiplied despite their straight-alpha surface hint.
    // Capture the actual opaque page background so intermediate image views
    // cannot multiply translucent text/artwork edges by alpha a second time.
    internal static Color ClearColor(Color background) => UsesCpuCapture ? background : Color.Transparent;
}
