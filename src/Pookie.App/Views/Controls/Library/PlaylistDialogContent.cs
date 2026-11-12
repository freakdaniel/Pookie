using System.Numerics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Reserve the track's final position from the first layout. It slides out from
// behind the dialog, rather than pushing that dialog down as it appears.
internal sealed class PlaylistDialogContent : Panel
{
    internal const double PreferredWidth = 480, MaximumWidth = 520;
    internal const double PreferredBodyHeight = 420, MaximumBodyHeight = 460;
    private const double TrackHeight = TrackDialogChrome.PreviewHeight, TrackSlot = TrackHeight + 20;
    internal const double BodyCenterOffset = TrackSlot / 2;
    private readonly FrameworkElement body;
    private readonly RisingTrack track;
    private readonly AnimationClock reveal = new(TimeSpan.FromMilliseconds(420), Easing.CubicBezier(.16, 1, .3, 1));
    private readonly AnimationClock resize = new(TimeSpan.FromMilliseconds(280), Easing.CubicBezier(.2, 0, 0, 1));
    private double bodyHeight = PreferredBodyHeight;
    internal double TargetBodyHeight { get; private set; } = PreferredBodyHeight;
    internal bool Resizing => resize.IsRunning;
    private double amount;
    private Action? hidden;
    internal double TrackReveal => amount;
    internal bool Animating => reveal.IsRunning;
    internal Rect BodyBounds => body.Bounds;
    internal Rect TrackBounds => track.Bounds;

    internal PlaylistDialogContent(FrameworkElement body, FrameworkElement preview)
    {
        this.body = body;
        body.Height = bodyHeight;
        track = new RisingTrack(preview) { Opacity = 0, IsHitTestVisible = false };
        Add(track); Add(body);
    }
    internal void SetLoadingSize(bool animate) => ResizeTo(PreferredBodyHeight, animate);
    internal void FitCreation(double height, bool animate = true) => ResizeTo(Math.Min(MaximumBodyHeight, height), animate);
    internal static double PlaylistHeight(int count, bool message) =>
        Math.Min(MaximumBodyHeight, 96 + (count > 0 ? count * 72d - 8 : 72) + (count > 0 && message ? 44 : 0));
    internal void FitPlaylists(int count, bool message)
    {
        ResizeTo(PlaylistHeight(count, message), true);
    }
    internal void FreezeSize() => resize.Stop();
    private void ResizeTo(double height, bool animate)
    {
        if (animate && TargetBodyHeight == height && (resize.IsRunning || bodyHeight == height)) return;
        resize.Stop(); TargetBodyHeight = height;
        if (!animate || FindVisualRoot() is not Window) { ApplyHeight(height); return; }
        var from = bodyHeight;
        resize.TickCallback = t => ApplyHeight(from + (height - from) * t);
        resize.CompletedCallback = () => ApplyHeight(height);
        resize.Start();
    }
    private void ApplyHeight(double height)
    { bodyHeight = height; body.Height = height; InvalidateMeasure(); }
    internal void ResetTrack()
    {
        reveal.Stop(); hidden = null; amount = track.Opacity = 0; track.Move(92);
    }
    internal void RevealTrack() { hidden = null; AnimateTrack(1); }
    internal void ShowTrackImmediately()
    { reveal.Stop(); hidden = null; amount = track.Opacity = 1; track.Move(0); }
    internal void HideTrack(Action completed)
    {
        hidden = completed;
        if (amount == 0) { hidden = null; completed(); return; }
        AnimateTrack(0);
    }
    private void AnimateTrack(double target)
    {
        reveal.Stop(); var from = amount;
        reveal.Duration = TimeSpan.FromMilliseconds((target == 1 ? 420 : 240) * Math.Max(.1, Math.Abs(target - from)));
        reveal.TickCallback = t =>
        {
            amount = from + (target - from) * t;
            track.Opacity = amount; track.Move(92 * (1 - amount));
        };
        reveal.CompletedCallback = () =>
        {
            amount = track.Opacity = target; track.Move(92 * (1 - target));
            if (target == 0) { var completed = hidden; hidden = null; completed?.Invoke(); }
        };
        reveal.Start();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = Math.Min(PreferredWidth, Math.Max(0, availableSize.Width));
        body.Measure(new(width, bodyHeight));
        track.Measure(new(Math.Max(0, width - 64), TrackHeight));
        return new(width, TrackSlot + bodyHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        body.Arrange(new(Bounds.X, Bounds.Y + TrackSlot, finalSize.Width, Math.Max(0, finalSize.Height - TrackSlot)));
        track.Arrange(new(Bounds.X + 32, Bounds.Y + 4, Math.Max(0, finalSize.Width - 64), TrackHeight));
        return finalSize;
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) { ResetTrack(); resize.Stop(); } }
    protected override void OnDispose() { reveal.Stop(); resize.Stop(); base.OnDispose(); }

    private sealed class RisingTrack : ContentControl
    {
        private double offset = 92;
        internal RisingTrack(FrameworkElement content)
        {
            Content = content; Padding = new Thickness(0); BorderThickness = 0; CornerRadius = 0; Background = Color.Transparent;
        }
        internal void Move(double value) { offset = value; InvalidateVisual(); }
        protected override bool TryGetChildTransform(out Matrix3x2 transform)
        { transform = Matrix3x2.CreateTranslation(0, (float)offset); return !transform.IsIdentity; }
    }
}
