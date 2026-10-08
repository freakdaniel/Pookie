using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// The cover and its details remain one retained subtree throughout the transition.
internal sealed class ExpandedPlayerLayout(PlayerMotionLayer card, PlayerMotionLayer panel, Border cover) : Panel
{
    private double sideAmount, revealAmount, sideTarget, panelReveal;
    private double coverSize, panelWidth, gap;
    private double center, split;
    private double panelOffsetFrom, panelRevealFrom;

    internal double SideAmount => sideAmount;

    internal void SetSideTarget(double target)
    {
        if (sideTarget == target) return;
        panelOffsetFrom = target > 0 && panelReveal == 0 ? 36 : panel.OffsetX;
        panelRevealFrom = panelReveal;
        sideTarget = target;
        InvalidateArrange();
    }

    internal void SetMotion(double reveal, double side, double panelAmount)
    {
        revealAmount = reveal;
        sideAmount = side;
        panelReveal = panelAmount;
        panel.Opacity = panelAmount;
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        var restingX = center + (split - center) * sideTarget;
        var animatedX = center + (split - center) * sideAmount;
        var y = 40 * (1 - revealAmount);
        var settled = Math.Abs(sideTarget - sideAmount) < .000001 && revealAmount > .999999;
        card.Move(animatedX - restingX, y, settled);
        var panelFraction = sideTarget > 0
            ? (panelRevealFrom < 1 ? (panelReveal - panelRevealFrom) / (1 - panelRevealFrom) : 1)
            : (panelRevealFrom > 0 ? 1 - panelReveal / panelRevealFrom : 1);
        var panelOffsetTo = sideTarget > 0 ? 0 : -Math.Min(36, gap * .35);
        panel.Move(panelOffsetFrom + (panelOffsetTo - panelOffsetFrom) * Math.Clamp(panelFraction, 0, 1),
            y, settled && panelReveal > .999999);
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
        card.ArrangeWithin(
            new Rect(Bounds.X + Math.Min(center, split), Bounds.Y + y,
                coverSize + Math.Abs(center - split), cardHeight + 40).Inflate(2, 2),
            new Rect(Bounds.X + x, Bounds.Y + y, coverSize, cardHeight));
        var panelX = Bounds.X + split + coverSize + gap;
        var panelHeight = Math.Max(0, finalSize.Height - 32);
        panel.ArrangeWithin(
            new Rect(panelX - 36, Bounds.Y + 16, panelWidth + 72, panelHeight + 40).Inflate(2, 2),
            new Rect(panelX, Bounds.Y + 16, panelWidth, panelHeight));
        ApplyMotion();
        return finalSize;
    }
}
