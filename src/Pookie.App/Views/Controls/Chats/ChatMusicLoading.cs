using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep the measured row in place while its real artwork, metadata and waveform load.
internal sealed class ChatMusicLoading : Panel
{
    private readonly FrameworkElement content;
    internal readonly LibrarySkeleton Skeleton = new();
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(260), Easing.CubicBezier(.2, 0, 0, 1));
    internal bool Loading { get; private set; } = true;
    internal double Reveal { get; private set; }

    internal ChatMusicLoading(FrameworkElement content, bool compact)
    {
        this.content = content;
        content.Opacity = 0; content.IsHitTestVisible = false;
        Skeleton.ConfigureLoadingRow(160, 1, compact ? LoadingRowStyle.Compact : LoadingRowStyle.Waveform);
        Skeleton.Height = compact ? 64 : TrackRowLayout.EmbeddedHeight;
        Skeleton.SetActive(true);
        Add(content); Add(Skeleton);
        fade.TickCallback = amount => { Reveal = amount; content.Opacity = amount; Skeleton.Opacity = 1 - amount; };
        fade.CompletedCallback = Finish;
    }
    internal void Show()
    {
        if (!Loading) return;
        Loading = false; content.IsHitTestVisible = true;
        if (ActualWidth <= 0 || FindVisualRoot() is not Window) Finish(); else fade.Start();
    }
    private void Finish()
    {
        Reveal = 1; content.Opacity = 1; Skeleton.Opacity = 0;
        Skeleton.SetActive(false); Skeleton.IsVisible = false;
    }
    internal void Stop() { fade.Stop(); Skeleton.SetActive(false); }
    protected override Size MeasureContent(Size availableSize)
    {
        content.Measure(availableSize); Skeleton.Measure(availableSize); return content.DesiredSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var area = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
        content.Arrange(area); Skeleton.Arrange(area); return finalSize;
    }
    protected override void OnDispose() { Stop(); base.OnDispose(); }
}
