using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Timed avatars keep their real buttons for input, but their resting pixels
// form one image. Replaying dozens of circular clips per scroll is expensive.
internal sealed class CommentMarkerLayer : Grid
{
    private IRenderSurface? raster;
    private IGraphicsContext? renderer;
    private IImage? image;
    private IGraphicsFactory? factory;
    private nint contextIdentity;
    private bool dirty = true;
    private long interaction;
    private readonly AnimationClock entrance = new(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.2, 0, 0, 1));
    private double reveal;
    internal double Reveal => reveal;

    internal void RevealMarkers()
    {
        if (reveal > 0 || entrance.IsRunning) return;
        entrance.TickCallback = t => { reveal = t; InvalidateVisual(); };
        entrance.Start();
    }
    internal void ResetReveal() { entrance.Stop(); reveal = 0; InvalidateVisual(); }

    internal void RefreshSnapshot() { dirty = true; InvalidateVisual(); }
    internal void Interact() { interaction = Stopwatch.GetTimestamp(); InvalidateVisual(); }

    protected override UIElement? OnHitTest(Point point)
    {
        if (reveal < .1 || !IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || !Bounds.Contains(point)) return null;
        for (var i = Children.Count - 1; i >= 0; i--)
            if (Children[i] is UIElement child && child.HitTest(point) is { } hit) return hit;
        // Empty parts of the avatar layer belong to the waveform underneath.
        return null;
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (reveal <= 0) return;
        context.Save();
        try { context.GlobalAlpha *= (float)reveal; RenderMarkers(context); }
        finally { context.Restore(); }
    }

    private void RenderMarkers(IGraphicsContext context)
    {
        if (Children.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (IsMouseOver || IsFocusWithin || Stopwatch.GetElapsedTime(interaction).TotalMilliseconds < 350)
        { dirty = true; base.RenderSubtree(context); return; }
        var scale = GetDpi() / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        var graphics = Application.Current.GraphicsFactory;
        var resized = raster == null || raster.PixelWidth != width || raster.PixelHeight != height || raster.DpiScale != scale || factory != graphics;
        if (dirty || resized)
        {
            using var rendering = graphics.AcquireBackgroundRenderScope();
            var currentContext = Win32BackdropBlur.CurrentContextIdentity;
            if (resized || contextIdentity != currentContext)
            {
                ClearRaster(); factory = graphics; contextIdentity = currentContext;
                raster = graphics.CreateSurface(OperatingSystem.IsWindows() && currentContext == 0
                    ? RenderSurfaceDescriptor.CpuPixels(width, height, scale, debugName: "CommentMarkers")
                    : RenderSurfaceDescriptor.CachedImage(width, height, scale, debugName: "CommentMarkers"));
                renderer = graphics.CreateContext(raster); image = graphics.CreateImageView(raster);
            }
            renderer!.BeginFrame(raster!);
            try
            {
                renderer.Clear(Color.Transparent); renderer.Translate(-Bounds.X, -Bounds.Y); renderer.SetClip(Bounds);
                base.RenderSubtree(renderer);
            }
            finally { renderer.EndFrame(); }
            dirty = false;
        }
        context.DrawImage(image!, new Rect(Bounds.X, Bounds.Y, width / scale, height / scale));
    }

    private void ClearRaster()
    {
        image?.Dispose(); image = null; renderer?.Dispose(); renderer = null;
        raster?.Dispose(); raster = null; factory = null; dirty = true;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) { ResetReveal(); ClearRaster(); } }
    protected override void OnDispose() { entrance.Stop(); ClearRaster(); base.OnDispose(); }
}
