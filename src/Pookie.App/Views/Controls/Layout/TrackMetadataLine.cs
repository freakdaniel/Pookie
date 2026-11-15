using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep short artist names next to the metadata, while giving both a finite width
// for ellipsis. A horizontal StackPanel measures its text children at infinity.
internal sealed class TrackMetadataLine : Panel
{
    private const double Gap = 6;
    private readonly TextBlock author, metadata;
    private readonly FrameworkElement? badge;
    private double naturalAuthorWidth, naturalMetadataWidth;
    private double badgeWidth;
    private readonly AnimationClock reveal = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private long displayedTrack;
    private bool metadataReady;
    internal double MetadataOpacity => metadata.Opacity;
    internal bool IsMetadataRevealing => reveal.IsRunning;

    internal TrackMetadataLine(TextBlock author, TextBlock metadata, FrameworkElement? badge = null)
    {
        this.author = author; this.metadata = metadata; this.badge = badge;
        Height = 21; ClipToBounds = true;
        Add(author); Add(metadata);
        if (badge != null) Add(badge);
        reveal.TickCallback = SetMetadataOpacity;
        reveal.CompletedCallback = () => SetMetadataOpacity(1);
        SetMetadataOpacity(0);
    }

    internal void SetMetadataReady(long trackId, bool ready)
    {
        if (displayedTrack != trackId || !ready)
        {
            reveal.Stop();
            displayedTrack = trackId; metadataReady = false;
            SetMetadataOpacity(0);
        }
        if (!ready || metadataReady) return;
        metadataReady = true;
        reveal.Start();
    }

    private void SetMetadataOpacity(double value)
    {
        // One clock keeps text, both separators and HD on the same fade frame.
        // The author's opacity and input remain independent of loading.
        metadata.Opacity = value;
        metadata.IsHitTestVisible = value >= 1;
        if (badge == null) return;
        badge.Opacity = value;
        badge.IsHitTestVisible = value >= 1;
    }

    private double AuthorWidth(double width) => Math.Min(naturalAuthorWidth,
        Math.Max(0, width - Math.Min(naturalMetadataWidth, width / 2) - Gap));

    protected override Size MeasureContent(Size availableSize)
    {
        author.Measure(new Size(double.PositiveInfinity, 21));
        metadata.Measure(new Size(double.PositiveInfinity, 21));
        badge?.Measure(new Size(double.PositiveInfinity, 21));
        badgeWidth = badge is { IsVisible: true } ? badge.DesiredSize.Width : 0;
        naturalAuthorWidth = author.DesiredSize.Width; naturalMetadataWidth = metadata.DesiredSize.Width;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : naturalAuthorWidth + Gap + naturalMetadataWidth + BadgeReserve;
        var textWidth = Math.Max(0, width - BadgeReserve);
        var authorWidth = AuthorWidth(textWidth);
        author.Measure(new Size(authorWidth, 21));
        metadata.Measure(new Size(Math.Max(0, textWidth - authorWidth - Gap), 21));
        return new Size(width, 21);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var textWidth = Math.Max(0, finalSize.Width - BadgeReserve);
        var authorWidth = AuthorWidth(textWidth);
        var metadataWidth = Math.Min(naturalMetadataWidth, Math.Max(0, textWidth - authorWidth - Gap));
        author.Arrange(new Rect(Bounds.X, Bounds.Y, authorWidth, finalSize.Height));
        metadata.Arrange(new Rect(Bounds.X + authorWidth + Gap, Bounds.Y,
            metadataWidth, finalSize.Height));
        badge?.Arrange(new Rect(Bounds.X + authorWidth + Gap + metadataWidth + (badgeWidth > 0 ? Gap : 0),
            Bounds.Y, badgeWidth, finalSize.Height));
        return finalSize;
    }

    private double BadgeReserve => badgeWidth > 0 ? badgeWidth + Gap : 0;

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) { reveal.Stop(); SetMetadataOpacity(metadataReady ? 1 : 0); }
    }

    protected override void OnDispose() { reveal.Stop(); base.OnDispose(); }
}
