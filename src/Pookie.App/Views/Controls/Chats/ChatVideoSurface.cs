using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Keep the renderer's repaint clip: retained frames may erase/redraw only a
// portion of the window. Keep the last image until the closing fade completes.
internal sealed class ChatVideoSurface(FrameworkElement content) : Panel
{
    internal const byte NormalBackdropAlpha = 200;
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.4, 0, .2, 1));
    private TaskCompletionSource? completion;
    private double reveal, from;
    private bool target;
    internal double Reveal => reveal;
    internal double Expansion { get; set; }
    internal ChatVideoSurface Initialize() { Add(content); IsVisible = false; return this; }
    internal Task SetOpen(bool open, bool immediate = false)
    {
        if (target == open && !immediate) return completion?.Task ?? Task.CompletedTask;
        completion?.TrySetResult(); fade.Stop(); target = open; from = reveal;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsHitTestVisible = open; IsVisible = true;
        if (immediate) { Finish(); return Task.CompletedTask; }
        fade.TickCallback = t => { reveal = from + ((target ? 1 : 0) - from) * t; InvalidateVisual(); };
        fade.CompletedCallback = Finish; fade.Start(); InvalidateMeasure();
        return completion.Task;
    }
    private void Finish()
    {
        fade.Stop(); reveal = target ? 1 : 0; IsVisible = target; InvalidateVisual(); completion?.TrySetResult();
    }
    protected override Size MeasureContent(Size availableSize) { content.Measure(availableSize); return content.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize) { content.Arrange(Bounds); return finalSize; }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (reveal <= 0) return;
        context.Save();
        try
        {
            // Intersect with our bounds instead of discarding the damage clip.
            // Painting outside the erased area would darken retained chat pixels
            // repeatedly as video frames and the conversation update separately.
            context.SetClip(Bounds);
            // Blend a scrim over the already rendered chat. Clear would replace
            // the underlying pixels on some backends, defeating transparency.
            var alpha = (byte)Math.Round((NormalBackdropAlpha + (255 - NormalBackdropAlpha) * Expansion) * reveal);
            context.FillRectangle(Bounds, Color.FromArgb(alpha, 0, 0, 0));
            // Fade the content separately: the scrim's alpha already includes
            // reveal and must not be faded twice.
            context.GlobalAlpha *= (float)reveal;
            base.RenderSubtree(context);
        }
        finally { context.Restore(); }
    }
    protected override void OnDispose() { fade.Stop(); completion?.TrySetResult(); base.OnDispose(); }
}
