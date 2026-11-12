using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Fixed page viewports and a fade-through slide keep outgoing/incoming text
// separate while the outer card changes height. Reversals retain the current pose.
internal class DialogPageDeck : Panel
{
    private readonly Page[] pages;
    private readonly double[] heights;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(260), Easing.CubicBezier(.2, 0, 0, 1));
    internal int SelectedIndex { get; private set; }
    internal bool Animating => motion.IsRunning;
    internal bool ContentOverlaps => pages.Count(page => page.Opacity > .001) > 1;
    internal DialogPageDeck(params FrameworkElement[] content)
    {
        pages = content.Select(child => new Page(child)).ToArray(); heights = new double[pages.Length];
        foreach (var page in pages) Add(page);
        ClipToBounds = true; Snap(0);
    }
    internal void SetPageHeight(int index, double height)
    { height = Math.Max(0, height); if (heights[index] == height) return; heights[index] = height; InvalidateMeasure(); }
    internal double PageHeight(int index) => heights[index];
    internal Rect PageBounds(int index) => pages[index].Bounds;
    internal FrameworkElement PageContent(int index) => (FrameworkElement)pages[index].Content!;
    internal void Show(int index, bool animate = true)
    {
        if (index == SelectedIndex && animate) return;
        motion.Stop(); var previous = SelectedIndex; SelectedIndex = index;
        if (!animate || FindVisualRoot() is not Window) { Snap(index); return; }
        var visible = Array.FindIndex(pages, page => page.Opacity > .001);
        var outgoing = visible >= 0 ? pages[visible] : null;
        var incoming = pages[index];
        var fromOpacity = outgoing?.Opacity ?? 0; var fromOffset = outgoing?.Offset ?? 0;
        var direction = index >= previous ? 1 : -1;
        foreach (var page in pages)
        {
            var active = ReferenceEquals(page, outgoing) || ReferenceEquals(page, incoming);
            page.IsVisible = ((UIElement)page.Content!).IsVisible = active;
            page.IsHitTestVisible = ReferenceEquals(page, incoming);
            if (!active) page.Opacity = 0;
        }
        var reversing = ReferenceEquals(outgoing, incoming);
        if (!reversing) { incoming.Opacity = 0; incoming.Move(24 * direction); }
        incoming.IsHitTestVisible = incoming.Opacity > .001;
        motion.Duration = TimeSpan.FromMilliseconds(260 * (reversing ? Math.Max(.25, 1 - fromOpacity) : 1));
        motion.TickCallback = t =>
        {
            if (reversing)
            { incoming.Opacity = fromOpacity + (1 - fromOpacity) * t; incoming.Move(fromOffset * (1 - t)); }
            else
            {
                var exit = Math.Min(1, t * 2); var enter = Math.Max(0, t * 2 - 1);
                if (outgoing != null) { outgoing.Opacity = fromOpacity * (1 - exit); outgoing.Move(fromOffset + (-24 * direction - fromOffset) * t); }
                incoming.Opacity = enter; incoming.Move(24 * direction * (1 - t));
            }
            incoming.IsHitTestVisible = incoming.Opacity > .001; InvalidateVisual();
        };
        motion.CompletedCallback = () => Snap(index);
        motion.Start(); InvalidateMeasure();
    }
    private void Snap(int index)
    {
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].Opacity = i == index ? 1 : 0; pages[i].Move(0);
            pages[i].IsVisible = ((UIElement)pages[i].Content!).IsVisible = pages[i].IsHitTestVisible = i == index;
        }
        InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        for (var i = 0; i < pages.Length; i++) pages[i].Measure(new(availableSize.Width, heights[i]));
        return new(double.IsFinite(availableSize.Width) ? availableSize.Width : 480, heights[SelectedIndex]);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < pages.Length; i++) pages[i].Arrange(new(Bounds.X, Bounds.Y, finalSize.Width, heights[i]));
        return finalSize;
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
    private sealed class Page : ContentControl
    {
        internal double Offset { get; private set; }
        internal Page(FrameworkElement child)
        { Content = child; Background = Color.Transparent; BorderThickness = 0; Padding = default; }
        internal void Move(double value) { Offset = value; InvalidateVisual(); }
        protected override bool TryGetChildTransform(out Matrix3x2 transform)
        { transform = Matrix3x2.CreateTranslation((float)Offset, 0); return !transform.IsIdentity; }
        protected override UIElement? OnHitTest(Point point) => base.OnHitTest(new(point.X - Offset, point.Y));
    }
}
