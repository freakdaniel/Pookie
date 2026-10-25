using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;

namespace Pookie.App;

internal sealed class ChatComposerSurface : Panel
{
    private readonly AnimationClock resize = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private double fromHeight, targetHeight = -1, resizeAmount = 1;
    private bool snapNext;
    private bool replyResize;
    internal bool IsResizing => resize.IsRunning;
    internal Action? ResizeStarted;
    internal void SnapNextLayout() { snapNext = true; resize.Stop(); InvalidateMeasure(); }
    internal void AnimateReplyChange() { replyResize = true; InvalidateMeasure(); }
    private readonly FrameworkElement content;
    private readonly Border hint;
    internal Border DropHint => hint;
    internal bool Dragging { get; private set; }
    internal ChatComposerSurface(FrameworkElement content)
    {
        this.content = content; ClipToBounds = true;
        resize.TickCallback = value => { resizeAmount = value; InvalidateMeasure(); };
        resize.CompletedCallback = () => { resizeAmount = 1; InvalidateMeasure(); };
        // One horizontal hint fits the compact row. Only the outer composer
        // paints a surface, avoiding a second inset contour during the fade.
        hint = new Border().BorderThickness(0).Background(Color.Transparent)
            .Child(new StackPanel().Horizontal().Spacing(10).Center().Children(
                Icons.View("paperclip", 24, TrackButtons.OnSurface).CenterVertical(),
                new TextBlock().Text("Отпусти файлы здесь").FontSize(14).Foreground(TrackButtons.OnSurface).CenterVertical()));
        hint.Opacity = 0; hint.IsHitTestVisible = false;
        hint.Transitions = [Transition.Create(UIElement.OpacityProperty, 170)];
        content.Transitions = [Transition.Create(UIElement.OpacityProperty, 170)];
        Add(content); Add(hint);
    }
    internal void SetDragging(bool value)
    {
        Dragging = value; hint.Opacity = value ? 1 : 0; content.Opacity = value ? 0 : 1;
    }
    protected override Size MeasureContent(Size availableSize)
    {
        // DockPanel's second measure offers the interpolated height. Measure the
        // target independently so that offer cannot shrink/restart the animation.
        content.Measure(new Size(availableSize.Width, double.PositiveInfinity)); hint.Measure(content.DesiredSize);
        var nextHeight = Math.Ceiling(content.DesiredSize.Height);
        if (targetHeight < 0 || snapNext)
        { resize.Stop(); targetHeight = nextHeight; resizeAmount = 1; snapNext = false; }
        else if (Math.Abs(targetHeight - nextHeight) > .5)
        {
            var restarting = resize.IsRunning;
            if (!restarting) ResizeStarted?.Invoke();
            fromHeight = resize.IsRunning ? fromHeight + (targetHeight - fromHeight) * resizeAmount : ActualHeight;
            resize.Stop(); targetHeight = nextHeight; resizeAmount = 0;
            resize.Duration = TimeSpan.FromMilliseconds(replyResize ? 250 : 220);
            resize.EasingFunction = replyResize ? Easing.CubicBezier(.4,0,.2,1) : Easing.CubicBezier(.2,0,0,1);
            resize.Start();
        }
        replyResize = false;
        return new(content.DesiredSize.Width, resize.IsRunning ? fromHeight + (targetHeight - fromHeight) * resizeAmount : targetHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
        content.Arrange(bounds); hint.Arrange(bounds); return finalSize;
    }
    protected override void OnDispose() { resize.Stop(); base.OnDispose(); }
}
