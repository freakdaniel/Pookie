using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Fade the fixed actions before reclaiming their space, like the header search
// transition. Keep the live editor, draft and focus; reverse from visible values.
internal sealed class TrackCommentActionRow : Panel
{
    private readonly FrameworkElement leading, trailing, composer;
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly AnimationClock width = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private double amount, opacity = 1, widthFrom, widthTarget, fadeFrom, fadeTarget, leadingWidth, trailingWidth;
    internal bool Expanded { get; private set; }
    internal double Expansion => (amount * 220 + (1 - opacity) * 150) / 370;

    internal TrackCommentActionRow(FrameworkElement leading, FrameworkElement composer, FrameworkElement trailing)
    {
        this.leading = leading; this.trailing = trailing; this.composer = composer;
        Height = 44; ClipToBounds = true;
        Add(leading); Add(composer); Add(trailing);
        width.TickCallback = t => { amount = widthFrom + (widthTarget - widthFrom) * t; InvalidateArrange(); };
        width.CompletedCallback = FinishWidth;
        fade.TickCallback = t => { opacity = fadeFrom + (fadeTarget - fadeFrom) * t; ApplyActionState(); };
        fade.CompletedCallback = FinishFade;
    }

    internal void SetExpanded(bool expanded, bool animate = true)
    {
        if (Expanded == expanded && animate) return;
        Expanded = expanded; width.Stop(); fade.Stop();
        SetResizing(false); ApplyActionState();
        if (!animate)
        {
            amount = expanded ? 1 : 0; opacity = expanded ? 0 : 1;
            ApplyActionState(); InvalidateArrange(); return;
        }
        if (expanded) StartFade(); else StartWidth();
    }

    private void StartFade()
    {
        fadeFrom = opacity; fadeTarget = Expanded ? 0 : 1;
        if (Math.Abs(fadeFrom - fadeTarget) < .000001) FinishFade(); else fade.Start();
    }
    private void FinishFade()
    {
        opacity = fadeTarget; ApplyActionState();
        if (Expanded) StartWidth();
    }
    private void StartWidth()
    {
        widthFrom = amount; widthTarget = Expanded ? 1 : 0;
        if (Math.Abs(widthFrom - widthTarget) < .000001) FinishWidth();
        else { SetResizing(true); width.Start(); }
    }
    private void FinishWidth()
    {
        amount = widthTarget; SetResizing(false); InvalidateArrange();
        if (!Expanded) StartFade();
    }
    private void SetResizing(bool resizing)
    { if (composer is ExpandedGlassSurface surface) surface.IsResizing = resizing; }

    private void ApplyActionState()
    {
        leading.Opacity = trailing.Opacity = opacity;
        leading.IsHitTestVisible = trailing.IsHitTestVisible = !Expanded && opacity >= .999999 && amount == 0;
        // Invisible actions also leave keyboard navigation, while their cached
        // widths preserve the collapsed slot during the editor's width motion.
        leading.IsVisible = trailing.IsVisible = opacity > 0;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var availableWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : 640;
        if (leading.IsVisible) { leading.Measure(new Size(double.PositiveInfinity, 44)); leadingWidth = leading.DesiredSize.Width; }
        if (trailing.IsVisible) { trailing.Measure(new Size(double.PositiveInfinity, 44)); trailingWidth = trailing.DesiredSize.Width; }
        composer.Measure(new Size(EditorWidth(availableWidth), 44));
        return new Size(availableWidth, 44);
    }
    private double EditorWidth(double total) => Math.Max(0, total - (leadingWidth + trailingWidth + 2 * TrackButtons.ActionSpacing) * (1 - amount));
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Buttons never translate or shrink. The editor changes width locally,
        // once the actions have faded out, without remeasuring the whole page.
        leading.Arrange(new Rect(Bounds.X, Bounds.Y, leadingWidth, 44));
        trailing.Arrange(new Rect(Bounds.Right - trailingWidth, Bounds.Y, trailingWidth, 44));
        var editorWidth = EditorWidth(finalSize.Width);
        composer.Measure(new Size(editorWidth, 44));
        composer.Arrange(new Rect(Bounds.X + (leadingWidth + TrackButtons.ActionSpacing) * (1 - amount), Bounds.Y, editorWidth, 44));
        return finalSize;
    }
    protected override void OnDispose() { width.Stop(); fade.Stop(); base.OnDispose(); }
}
