using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep the original author, repost attribution and event date on one bounded line.
internal sealed class UserPublicationByline : Panel
{
    private readonly TextBlock author, actor, date;
    private readonly Image repeat;
    private readonly bool repost;
    private double authorWidth, actorWidth, dateWidth;

    internal UserPublicationByline(TextBlock author, TextBlock actor, TextBlock date, bool repost)
    {
        this.author = author; this.actor = actor; this.date = date; this.repost = repost;
        repeat = Icons.View("repeat", 12, Color.FromRgb(155, 155, 155));
        Height = 18; ClipToBounds = true;
        Add(author); Add(repeat); Add(actor); Add(date);
        repeat.IsVisible = actor.IsVisible = repost;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        author.Measure(new Size(double.PositiveInfinity, 18));
        date.Measure(new Size(double.PositiveInfinity, 18));
        if (repost) actor.Measure(new Size(double.PositiveInfinity, 18));
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 500;
        dateWidth = Math.Min(date.DesiredSize.Width, width * .32);
        var available = Math.Max(0, width - dateWidth - (dateWidth > 0 ? 12 : 0));
        actorWidth = repost ? Math.Min(actor.DesiredSize.Width, Math.Max(0, (available - 24) * .48)) : 0;
        authorWidth = Math.Min(author.DesiredSize.Width, Math.Max(0, available - (repost ? actorWidth + 24 : 0)));
        author.Measure(new Size(authorWidth, 18)); date.Measure(new Size(dateWidth, 18));
        if (repost) { actor.Measure(new Size(actorWidth, 18)); repeat.Measure(new Size(12, 12)); }
        return new Size(width, 18);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        author.Arrange(new Rect(Bounds.X, Bounds.Y, authorWidth, 18));
        if (repost)
        {
            repeat.Arrange(new Rect(Bounds.X + authorWidth + 6, Bounds.Y + 3, 12, 12));
            actor.Arrange(new Rect(Bounds.X + authorWidth + 24, Bounds.Y, actorWidth, 18));
        }
        date.Arrange(new Rect(Bounds.Right - dateWidth, Bounds.Y, dateWidth, 18));
        return finalSize;
    }
}
