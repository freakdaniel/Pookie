using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

// One lightweight overlay shared by hover, keyboard focus and timed playback.
// It never intercepts seeking and keeps no textures or copies of the waveform.
internal sealed class WaveCommentPreview : Panel
{
    private readonly TextBlock author = new TextBlock().FontSize(13).Bold().Foreground(Color.FromRgb(142, 198, 238))
        .TextTrimming(TextTrimming.CharacterEllipsis);
    private readonly TextBlock time = new TextBlock().FontSize(11).Foreground(Color.FromRgb(165, 168, 175));
    private readonly TextBlock body = new TextBlock().FontSize(13).Foreground(Color.FromRgb(235, 236, 239))
        .TextWrapping(TextWrapping.Wrap).TextTrimming(TextTrimming.CharacterEllipsis).MaxHeight(40);
    private readonly StackPanel content;
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(230), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly AnimationClock textFade = new(TimeSpan.FromMilliseconds(160), Easing.CubicBezier(.2, 0, 0, 1));
    private Size size, targetSize;
    private double anchor, reveal;
    private SoundCloudComment? comment;
    internal long? CommentId => comment?.Id;
    internal double Reveal => reveal;
    internal Rect CardBounds { get; private set; }
    internal bool Animating => motion.IsRunning || textFade.IsRunning;

    internal WaveCommentPreview()
    {
        IsVisible = false; IsHitTestVisible = false;
        content = new StackPanel().Vertical().Spacing(4).Children(
            new Grid().Columns("*,Auto").Rows("Auto").Spacing(12).Children(author.Column(0), time.Column(1).CenterVertical()), body);
        Add(content);
    }

    internal void Show(SoundCloudComment? value, double fraction = 0)
    {
        if (comment?.Id == value?.Id && comment?.Body == value?.Body && comment?.User?.Username == value?.User?.Username) return;
        motion.Stop(); textFade.Stop();
        var from = reveal; var fromSize = size; var fromAnchor = anchor;
        comment = value;
        if (value != null)
        {
            author.Text = value.User?.Username ?? "Пользователь SoundCloud";
            var seconds = Math.Max(0, (value.Timestamp ?? 0) / 1000);
            time.Text = $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
            body.Text = value.Body;
            targetSize = MeasureCard(ActualWidth > 0 ? ActualWidth : 340);
            if (from < .001) { fromSize = targetSize; fromAnchor = fraction; }
            content.Opacity = from > .001 ? 0 : 1;
            textFade.TickCallback = t => content.Opacity = t;
            if (from > .001) textFade.Start();
            IsVisible = true;
        }
        var to = value == null ? 0d : 1d;
        var toAnchor = value == null ? fromAnchor : Math.Clamp(fraction, 0, 1);
        motion.TickCallback = t =>
        {
            reveal = from + (to - from) * t;
            size = new(fromSize.Width + (targetSize.Width - fromSize.Width) * t, fromSize.Height + (targetSize.Height - fromSize.Height) * t);
            anchor = fromAnchor + (toAnchor - fromAnchor) * t;
            InvalidateArrange(); InvalidateVisual();
        };
        motion.CompletedCallback = () => { reveal = to; size = targetSize; anchor = toAnchor; IsVisible = to > 0; InvalidateArrange(); };
        motion.Start(); InvalidateMeasure();
    }

    internal void Reset()
    {
        motion.Stop(); textFade.Stop(); comment = null; reveal = 0;
        author.Text = time.Text = body.Text = ""; content.Opacity = 1; IsVisible = false;
    }

    private Size MeasureCard(double width)
    {
        author.Measure(new(double.PositiveInfinity, 20)); time.Measure(new(double.PositiveInfinity, 20));
        body.Measure(new(double.PositiveInfinity, 40));
        var limit = Math.Max(1, Math.Min(320, width - 8));
        var desired = Math.Max(author.DesiredSize.Width + time.DesiredSize.Width + 12, body.DesiredSize.Width) + 24;
        var cardWidth = Math.Clamp(desired, Math.Min(180, limit), limit);
        content.Measure(new(cardWidth - 24, 76));
        return new(cardWidth, Math.Min(94, content.DesiredSize.Height + 18));
    }

    protected override Size MeasureContent(Size availableSize)
    {
        targetSize = MeasureCard(availableSize.Width);
        if (!motion.IsRunning) size = targetSize;
        return availableSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Min(size.Width, Math.Max(1, finalSize.Width - 8));
        var x = Bounds.X + Math.Clamp(anchor * finalSize.Width - width / 2, 4, Math.Max(4, finalSize.Width - width - 4));
        CardBounds = new(x, Bounds.Y + 97 - size.Height + 7 * (1 - reveal), width, size.Height);
        content.Arrange(new(x + 12, CardBounds.Y + 9, Math.Max(0, targetSize.Width - 24), Math.Max(0, targetSize.Height - 18)));
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (reveal <= 0) return;
        context.Save();
        try
        {
            context.GlobalAlpha *= (float)reveal;
            context.FillRoundedRectangle(CardBounds, 12, 12, Color.FromArgb(248, 31, 33, 38));
            context.DrawRoundedRectangle(CardBounds, 12, 12, Color.FromArgb(28, 255, 255, 255), 1);
            context.SetClipRoundedRect(CardBounds, 12, 12);
            base.RenderSubtree(context);
        }
        finally { context.Restore(); }
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null) Reset(); }
    protected override void OnDispose() { Reset(); base.OnDispose(); }
}
