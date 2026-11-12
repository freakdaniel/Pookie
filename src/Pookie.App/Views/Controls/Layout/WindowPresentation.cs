using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Typed build-time extension of our pinned MewUI assembly; no runtime patching.
internal static class WindowPresentation
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "PookieHoldWindowFrame")]
    internal static extern ref Func<bool>? HoldFrame(Window owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "PookieWindowFramePresented")]
    internal static extern ref Action? FramePresented(Window owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_drawingReferenceFrame")]
    internal static extern ref bool DrawDirectly(Window owner);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "PookieFrameSlack")]
    internal static extern ref int RetainedFrameSlack(Window owner);

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookieYieldDispatcher")]
    internal static extern ref Func<bool>? YieldDispatcher(Window? declaringType);

    internal static void EnableLiveGlass(bool enabled)
    {
        BeforeReplay(null) = enabled ? static (element, context) =>
        {
            if (element is ModalDialog dialog) dialog.CaptureLiveGlass(context);
            else if (element is ChatPopupLayer popup) popup.CaptureLiveGlass(context);
            else if (element is WaveCommentPreview comment) comment.CaptureLiveGlass(context);
            else if (element is ShellHeader.BackdropLayer header) header.CaptureLiveGlass(context);
            else if (element is GlassToolGroup tool)
            {
                tool.CaptureLiveGlass(context);
                tool.DrawLiveGlass(context);
            }
            else if (element is GlassCard card) card.DrawLiveGlass(context);
            else if (element is PresentationRoot root && root.FindVisualRoot() is Window window)
                LiveGlassBackdrop.WarmInFrame(window, context);
        } : null;
        PopupDamage(null) = enabled ? LiveGlassBackdrop.ExpandDamage : null;
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookieBeforeReplay")]
    private static extern ref Action<UIElement, IGraphicsContext>? BeforeReplay(UIElement? declaringType);

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookiePopupDamage")]
    private static extern ref Action<Window, IReadOnlyList<Rect>, Action<Rect>>? PopupDamage(Window? declaringType);

    internal static bool IsRecording(IGraphicsContext context) => context.GetType().Name == "RenderDataRecorder";

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
