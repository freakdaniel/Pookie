using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class ChatEmojiPanelLayout(FrameworkElement categories, FrameworkElement search, FrameworkElement body, FrameworkElement modes) : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(300), Easing.CubicBezier(.4, 0, .2, 1));
    private double amount;
    private bool searching;
    internal double SearchReveal => amount;
    internal ChatEmojiPanelLayout Initialize() { Add(categories); Add(search); Add(body); Add(modes); return this; }
    internal void SearchMode(bool value, bool immediate = false)
    {
        if (searching == value && !immediate) return;
        searching = value; motion.Stop(); var from = amount;
        categories.IsHitTestVisible = !value;
        motion.TickCallback = t => { amount = from + ((value ? 1 : 0) - from) * t; InvalidateArrange(); };
        if (immediate) { amount = value ? 1 : 0; InvalidateArrange(); } else motion.Start();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var size = new Size(Math.Min(382, availableSize.Width), Math.Min(420, availableSize.Height));
        categories.Measure(new(size.Width - 16, 44)); search.Measure(new(size.Width - 16, 38));
        modes.Measure(new(size.Width, 48)); body.Measure(new(size.Width, Math.Max(0, size.Height - 142 + 44 * amount)));
        return size;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var searchTop = Bounds.Y + 8 + 44 * (1 - amount);
        categories.Opacity = 1 - amount;
        categories.Arrange(new(Bounds.X + 8, Bounds.Y + 4 - 44 * amount, Math.Max(0, finalSize.Width - 16), 40));
        search.Arrange(new(Bounds.X + 8, searchTop, Math.Max(0, finalSize.Width - 16), 38));
        body.Arrange(new(Bounds.X, searchTop + 42, finalSize.Width, Math.Max(0, Bounds.Bottom - 48 - searchTop - 42)));
        modes.Arrange(new(Bounds.X, Bounds.Bottom - 48, finalSize.Width, 48));
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    { context.Save(); try { context.SetClipRoundedRect(Bounds, 20, 20); base.RenderSubtree(context); } finally { context.Restore(); } }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
