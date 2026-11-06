using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Keep the renderer's repaint clip: retained frames may erase/redraw only a
// portion of the window. Keep the last image until the closing fade completes.
internal sealed class ChatVideoSurface(FrameworkElement content) : Panel
{
    internal const byte NormalBackdropAlpha = OverlayScrim.NormalAlpha;
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.4, 0, .2, 1));
    private TaskCompletionSource? completion;
    private double reveal, from;
    private bool target;
    internal double Reveal => reveal;
    internal double Expansion { get; set; }
    internal void RenderGlassBackdrop(IGraphicsContext context)
    {
        OverlayScrim.Draw(context, Bounds, reveal, Expansion);
        context.BeginOpacity(reveal);
        try { if (content is ChatVideoLayout layout) layout.RenderMediaBackdrop(context); }
        finally { context.EndOpacity(); }
    }
    internal ChatVideoSurface Initialize() { Add(content); content.Opacity = 0; IsVisible = false; return this; }
    internal Task SetOpen(bool open, bool immediate = false)
    {
        if (target == open && !immediate) return completion?.Task ?? Task.CompletedTask;
        completion?.TrySetResult(); fade.Stop(); target = open; from = reveal;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsHitTestVisible = open; IsVisible = true;
        if (immediate) { Finish(); return Task.CompletedTask; }
        fade.TickCallback = t => { reveal = from + ((target ? 1 : 0) - from) * t; content.Opacity = reveal; InvalidateVisual(); };
        fade.CompletedCallback = Finish; fade.Start(); InvalidateMeasure();
        return completion.Task;
    }
    private void Finish()
    {
        fade.Stop(); reveal = target ? 1 : 0; content.Opacity = reveal; IsVisible = target; InvalidateVisual(); completion?.TrySetResult();
    }
    protected override Size MeasureContent(Size availableSize) { content.Measure(availableSize); return content.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize) { content.Arrange(Bounds); return finalSize; }
    protected override ChildClip? GetChildClip(UIElement child) => new(Bounds);
    protected override void OnRender(IGraphicsContext context)
    {
        OverlayScrim.Draw(context, Bounds, reveal, Expansion);
    }
    protected override void OnDispose() { fade.Stop(); completion?.TrySetResult(); base.OnDispose(); }
}
