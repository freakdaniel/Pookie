using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Typed build-time extension of our pinned MewUI assembly; no runtime patching.
internal static class WindowPresentation
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "_renderCullViewport")]
    private static extern ref Rect? CullViewport(UIElement? declaringType);

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookieCaptureDepth")]
    private static extern ref int CaptureDepth(UIElement? declaringType);

    internal static CaptureScope CaptureViewport(IRenderSurface surface)
    {
        var previous = CullViewport(null);
        CaptureDepth(null)++;
        CullViewport(null) = new Rect(0, 0, surface.PixelWidth / surface.DpiScale, surface.PixelHeight / surface.DpiScale);
        return new(previous);
    }

    internal readonly struct CaptureScope(Rect? previous) : IDisposable
    {
        public void Dispose()
        {
            CullViewport(null) = previous;
            CaptureDepth(null)--;
        }
    }
}
