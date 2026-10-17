using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class ChatLoadingView : IDisposable
{
    internal Grid Root { get; }
    internal ChatSkeleton Skeleton { get; }
    private readonly FrameworkElement content;
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(230), Easing.CubicBezier(.2, 0, 0, 1));
    private bool loading;
    internal ChatLoadingView(FrameworkElement content, bool inbox)
    {
        this.content = content; Skeleton = new(inbox) { IsVisible = false };
        Root = new Grid().Columns("*").Rows("*").Children(content, Skeleton);
        fade.TickCallback = amount => { content.Opacity = amount; Skeleton.Opacity = 1 - amount; };
        fade.CompletedCallback = () => { Skeleton.IsVisible = false; Skeleton.SetActive(false); };
    }
    internal void SetLoading(bool value)
    {
        if (value == loading) return;
        loading = value; fade.Stop(); content.IsHitTestVisible = !value;
        if (value) { content.Opacity = 0; Skeleton.Opacity = 1; Skeleton.IsVisible = true; Skeleton.SetActive(true); }
        else fade.Start();
    }
    internal void Pause() => Skeleton.SetActive(false);
    public void Dispose() { fade.Stop(); Skeleton.SetActive(false); }
}

internal sealed class ChatSkeleton(bool inbox) : Control
{
    private readonly AnimationClock pulse = new(TimeSpan.FromMilliseconds(1300), Easing.Linear) { RepeatCount = -1 };
    private double phase;
    internal bool Running => pulse.IsRunning;
    internal void SetActive(bool active)
    {
        IsHitTestVisible = false;
        pulse.TickCallback = value => { phase = value; InvalidateVisual(); };
        if (active) pulse.Start(); else pulse.Stop();
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var color = Color.FromArgb((byte)(18 + 13 * (.5 - .5 * Math.Cos(phase * Math.PI * 2))), 255, 255, 255);
        void Block(double x, double y, double w, double h, double r) => context.FillRoundedRectangle(
            new Rect(Bounds.X + x, Bounds.Y + y, Math.Max(0, w), h), r, r, color);
        for (var i = 0; i < (inbox ? 7 : 6); i++)
        {
            var y = i * (inbox ? 82 : 90) + 12;
            if (y + 60 > Bounds.Height) break;
            if (inbox) { Block(10, y, 46, 46, 23); Block(68, y + 5, Bounds.Width * .48, 12, 5); Block(68, y + 29, Bounds.Width * .63, 10, 4); }
            else { var w = Bounds.Width * (i % 3 == 0 ? .56 : .4); Block(i % 2 == 0 ? 8 : Bounds.Width - w - 12, y, w, i % 3 == 0 ? 70 : 52, 16); }
        }
    }
    protected override void OnDispose() { pulse.Stop(); base.OnDispose(); }
}
