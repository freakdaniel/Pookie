using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep short artist names next to the metadata, while giving both a finite width
// for ellipsis. A horizontal StackPanel measures its text children at infinity.
internal sealed class TrackMetadataLine : Panel
{
    private const double Gap = 6;
    private readonly TextBlock author, metadata;
    private double naturalAuthorWidth, naturalMetadataWidth;

    internal TrackMetadataLine(TextBlock author, TextBlock metadata)
    {
        this.author = author; this.metadata = metadata;
        Height = 21; ClipToBounds = true;
        Add(author); Add(metadata);
    }

    private double AuthorWidth(double width) => Math.Min(naturalAuthorWidth,
        Math.Max(0, width - Math.Min(naturalMetadataWidth, width / 2) - Gap));

    protected override Size MeasureContent(Size availableSize)
    {
        author.Measure(new Size(double.PositiveInfinity, 21));
        metadata.Measure(new Size(double.PositiveInfinity, 21));
        naturalAuthorWidth = author.DesiredSize.Width; naturalMetadataWidth = metadata.DesiredSize.Width;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : naturalAuthorWidth + Gap + naturalMetadataWidth;
        var authorWidth = AuthorWidth(width);
        author.Measure(new Size(authorWidth, 21));
        metadata.Measure(new Size(Math.Max(0, width - authorWidth - Gap), 21));
        return new Size(width, 21);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var authorWidth = AuthorWidth(finalSize.Width);
        author.Arrange(new Rect(Bounds.X, Bounds.Y, authorWidth, finalSize.Height));
        metadata.Arrange(new Rect(Bounds.X + authorWidth + Gap, Bounds.Y,
            Math.Max(0, finalSize.Width - authorWidth - Gap), finalSize.Height));
        return finalSize;
    }
}
