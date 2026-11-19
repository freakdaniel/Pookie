using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// The cover and its details remain one retained subtree throughout the transition.
internal sealed class ExpandedPlayerLayout(PlayerMotionLayer card, PlayerMotionLayer panel, Border cover, Button miniArtwork, Thickness coverShadowPadding) : Panel
{
    private double sideAmount, revealAmount, sideTarget, panelReveal;
    private double coverSize, panelWidth, gap;
    private double center, split;

    internal bool ReduceMotion { get; set; }
    internal double SideAmount => sideAmount;

    internal void SetSideTarget(double target)
    {
        if (sideTarget == target) return;
        sideTarget = target;
        InvalidateArrange();
    }

    internal void SetMotion(double reveal, double side, double panelAmount)
    {
        revealAmount = reveal;
        sideAmount = side;
        panelReveal = panelAmount;
        card.InkStrength = ReduceMotion ? reveal : reveal > 0 ? 1 : 0;
        panel.InkStrength = panelAmount * reveal;
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        var restingX = center + (split - center) * sideTarget;
        var animatedX = center + (split - center) * sideAmount;
        var y = ReduceMotion ? 0 : 40 * (1 - revealAmount);
        var settled = Math.Abs(sideTarget - sideAmount) < .000001 && revealAmount > .999999;
        var from = miniArtwork.Bounds;
        var to = cover.Bounds;
        var remaining = 1 - revealAmount;
        if (!ReduceMotion && from.Width > 0 && to.Width > 0)
        {
            var x = (animatedX - restingX) * revealAmount + (from.X - to.X) * remaining;
            var scale = revealAmount + from.Width / to.Width * remaining;
            card.Move(x, (from.Y - to.Y) * remaining, settled, scale, new Point(to.X, to.Y));
        }
        else card.Move(animatedX - restingX, y, settled);
        // Keep room between the cover and the panel throughout the same spring
        // in either direction. No delayed fade is needed to hide an intersection.
        var closedOffset = Math.Max(36, center - split - gap + 24);
        panel.Move(closedOffset * (1 - sideAmount), y, settled && panelReveal > .999999);
    }

    // FrameworkElement removes the outer margin before MeasureContent. Using
    // MeasureOverride here measured the panel 32 px taller than its arranged slot.
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1280;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 720;
        gap = Math.Clamp(width * .065, 40, 120);
        coverSize = Math.Clamp(Math.Min(height - 172, width * .34), 200, 460);
        panelWidth = Math.Min(620, Math.Max(280, width - coverSize - gap - 96));
        cover.Width = cover.Height = coverSize;
        card.Measure(new Size(coverSize, coverSize + 156));
        panel.Measure(new Size(panelWidth, Math.Max(0, height - 32)));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        center = (finalSize.Width - coverSize) / 2;
        split = (finalSize.Width - coverSize - gap - panelWidth) / 2;
        var x = center + (split - center) * sideTarget;
        var cardHeight = Math.Min(finalSize.Height, coverSize + 156);
        var y = (finalSize.Height - cardHeight) / 2;
        // A render translation does not change layout bounds. Keep the host's
        // repaint region large enough for both endpoints and the vertical reveal,
        // with a margin for device-pixel rounding and antialiased edges.
        var bounceMargin = Math.Max(24, Math.Abs(center - split) * .1);
        var mini = miniArtwork.Bounds;
        var miniScale = mini.Width / coverSize;
        var miniCardBounds = new Rect(mini.X, mini.Y, mini.Width, cardHeight * miniScale)
            .Inflate(coverShadowPadding * miniScale);
        card.ArrangeWithin(
            new Rect(Bounds.X + Math.Min(center, split), Bounds.Y + y,
                coverSize + Math.Abs(center - split), cardHeight + 40).Inflate(bounceMargin, 2)
                .Inflate(new Thickness(coverShadowPadding.Left, coverShadowPadding.Top, coverShadowPadding.Right, 0))
                .Union(miniCardBounds),
            new Rect(Bounds.X + x, Bounds.Y + y, coverSize, cardHeight));
        var panelX = Bounds.X + split + coverSize + gap;
        var panelHeight = Math.Max(0, finalSize.Height - 32);
        var closedOffset = Math.Max(36, center - split - gap + 24);
        panel.ArrangeWithin(
            new Rect(panelX, Bounds.Y + 16, panelWidth + closedOffset, panelHeight + 40).Inflate(bounceMargin, 2),
            new Rect(panelX, Bounds.Y + 16, panelWidth, panelHeight));
        ApplyMotion();
        return finalSize;
    }
}
