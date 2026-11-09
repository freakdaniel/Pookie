using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class ShellHeader : Grid
{
    internal const double HeaderHeight = 64;
    internal const double NavigationWidth = 440;
    internal const double NavigationButtonHeight = 40;
    internal const double HistoryButtonSize = 36;
    internal const double AccountButtonSize = 34;
    private const double CardHeight = 48;
    private const double NavigationInset = (CardHeight - NavigationButtonHeight) / 2;
    private const double HistoryInset = (CardHeight - HistoryButtonSize) / 2;
    private const double AccountInset = (CardHeight - AccountButtonSize) / 2;
    internal static readonly Color GlassTint = Color.FromArgb(202, 34, 34, 39);
    private readonly LiveGlassBackdrop glass;
    internal GlassCard HistoryCard { get; }
    internal GlassCard NavigationCard { get; }
    internal GlassCard AccountCard { get; }
    internal bool ReuseBlurForResizedFrame() => glass.ReuseForResizedFrame();
    internal void ResumeBlurCapture() => glass.ResumeCapture();

    internal ShellHeader(Func<FrameworkElement> page, FrameworkElement history, FrameworkElement navigation, FrameworkElement account,
        Func<bool> deferResizeCapture, Func<bool>? paceCapture = null)
    {
        HistoryCard = Card(history, 12, HistoryInset).Left();
        NavigationCard = Card(navigation, NavigationInset, NavigationInset).Width(NavigationWidth + 2 * NavigationInset).CenterHorizontal();
        AccountCard = Card(account, AccountInset, AccountInset).Right();
        this.Columns("144,*,144").Rows("*").Padding(36, 14, 36, 2).Children(
            HistoryCard.Column(0), NavigationCard.Column(1), AccountCard.Column(2));

        // The three cards look through the same scene. Sample and blur that
        // strip once, then refract each card at its own screen coordinates.
        glass = new LiveGlassBackdrop(this, (context, _) => page().Render(context), tint: GlassTint,
            captureArea: GlassBounds, maximumSampleSide: 2048, sampleDensity: OperatingSystem.IsWindows() ? .375 : .5,
            reuseStrip: OperatingSystem.IsWindows(), deferResizeCapture: deferResizeCapture, paceCapture: paceCapture);
        HistoryCard.UseLiveBackdrop(glass);
        NavigationCard.UseLiveBackdrop(glass);
        AccountCard.UseLiveBackdrop(glass);
    }

    private Rect GlassBounds()
    {
        var left = HistoryCard.Bounds;
        var right = AccountCard.Bounds;
        return left.IsEmpty || right.IsEmpty ? Rect.Empty : new Rect(left.X, left.Y, right.Right - left.X, left.Height);
    }

    private static GlassCard Card(FrameworkElement content, double leftInset, double rightInset) =>
        new GlassCard { HoverEnabled = false, ReflectionEnabled = false }.Height(CardHeight).CornerRadius(CardHeight / 2)
            .Padding(leftInset, 0, rightInset, 0).CenterVertical().Child(content);

    internal static Rect StateLayerBounds(FrameworkElement control, double dpiScale)
    {
        var bounds = control.Bounds;
        Element? ancestor = control.Parent;
        while (ancestor != null && ancestor is not GlassCard) ancestor = ancestor.Parent;
        if (ancestor is not GlassCard card || bounds.IsEmpty) return bounds;

        // Derive all painted edges from the same physical inset. Independently
        // rounded control sizes can leave one extra pixel on one side.
        var frame = card.Bounds;
        var inset = LayoutRounding.RoundToPixel((card.Height - control.Height) / 2, dpiScale);
        var tolerance = 2 / dpiScale;
        var left = Math.Abs(bounds.X - frame.X - inset) <= tolerance ? frame.X + inset : bounds.X;
        var right = Math.Abs(frame.Right - bounds.Right - inset) <= tolerance ? frame.Right - inset : bounds.Right;
        return new Rect(left, frame.Y + inset, right - left, frame.Height - 2 * inset);
    }

    // Capture before entering the header's opacity group, while the scene
    // behind it is still in the window framebuffer.
    internal sealed class BackdropLayer : Grid
    {
        private readonly ShellHeader header;

        internal BackdropLayer(ShellHeader header)
        {
            this.header = header;
            Add(header);
        }

        internal void CaptureLiveGlass(IGraphicsContext context) => header.glass.CaptureLive(context);
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        glass.Attach(newRoot as Window);
    }

    protected override void OnDispose()
    {
        glass.Dispose();
        base.OnDispose();
    }
}
