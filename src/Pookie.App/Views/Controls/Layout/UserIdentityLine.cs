using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Reserve space for identity badges before truncating a long display name.
internal sealed class UserIdentityLine(TextBlock name, params FrameworkElement[] badges) : Panel
{
    private double nameWidth, lineHeight;
    internal UserIdentityLine Initialize()
    {
        Add(name);
        foreach (var badge in badges) Add(badge);
        return this;
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var badgeWidth = 0d;
        lineHeight = 0;
        foreach (var badge in badges.Where(badge => badge.IsVisible))
        {
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            badgeWidth += badge.DesiredSize.Width + 8;
            lineHeight = Math.Max(lineHeight, badge.DesiredSize.Height);
        }
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 680;
        name.Measure(new Size(Math.Max(0, width - badgeWidth), double.PositiveInfinity));
        nameWidth = Math.Min(name.DesiredSize.Width, Math.Max(0, width - badgeWidth));
        lineHeight = Math.Max(lineHeight, name.DesiredSize.Height);
        return new Size(nameWidth + badgeWidth, lineHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        name.Arrange(new Rect(Bounds.X, Bounds.Y, nameWidth, lineHeight));
        var x = Bounds.X + nameWidth;
        foreach (var badge in badges.Where(badge => badge.IsVisible))
        {
            x += 8;
            badge.Arrange(new Rect(x, Bounds.Y + (lineHeight - badge.DesiredSize.Height) / 2, badge.DesiredSize.Width, badge.DesiredSize.Height));
            x += badge.DesiredSize.Width;
        }
        return finalSize;
    }
}
