using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Messages;

namespace Pookie.App;

// Measure the caption before choosing a media bubble's width. The image stays at
// its natural size; a wider caption extends its backdrop, never the image itself.
internal sealed class ChatBubbleContent(FrameworkElement attachments, TextBlock caption, FrameworkElement open, Border footer) : Panel
{
    private Size photoSize = new(180, 120);
    private bool inlineFooter;
    private Color captionBackground;
    internal Color CaptionBackground { get => captionBackground; set { captionBackground = value; InvalidateVisual(); } }
    internal bool PhotoMode { get; private set; }
    internal bool InlineFooter => inlineFooter;
    internal bool PhotoOnly => PhotoMode && !caption.IsVisible;
    internal ChatBubbleContent Initialize() { Add(attachments); Add(caption); Add(open); Add(footer); return this; }
    internal void SetPhoto(Size? size) { PhotoMode = true; photoSize = size ?? new(180, 120); InvalidateMeasure(); }
    internal void Reset() { PhotoMode = false; CaptionBackground = Color.Transparent; InvalidateMeasure(); }

    protected override Size MeasureContent(Size availableSize)
    {
        var available = double.IsFinite(availableSize.Width) ? availableSize.Width : 496;
        var width = Math.Min(496, available);
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var captionWidth = caption.DesiredSize.Width;
        if (PhotoMode)
        {
            width = Math.Min(available, ChatImagePreview.MessageWidth(photoSize,
                caption.IsVisible ? caption.DesiredSize.Width : null));
        }
        attachments.Measure(new Size(width, double.PositiveInfinity));
        caption.Measure(new Size(Math.Max(1, width - (PhotoMode ? 24 : 0)), double.PositiveInfinity));
        open.Measure(new Size(width, double.PositiveInfinity));
        footer.Measure(new Size(Math.Max(1, width - (PhotoMode ? 16 : 0)), double.PositiveInfinity));
        inlineFooter = caption.IsVisible && (PhotoMode || !attachments.IsVisible) && !open.IsVisible && !caption.Text.Contains('\n') &&
            captionWidth + footer.DesiredSize.Width + (PhotoMode ? 36 : 10) <= width;
        if (inlineFooter && !PhotoMode)
        {
            width = captionWidth + footer.DesiredSize.Width + 10;
            caption.Measure(new Size(captionWidth, double.PositiveInfinity));
        }
        if (!PhotoMode && !inlineFooter) width = Math.Min(width, Math.Max(attachments.DesiredSize.Width,
            Math.Max(caption.DesiredSize.Width, Math.Max(open.DesiredSize.Width, footer.DesiredSize.Width))));
        var height = attachments.IsVisible ? attachments.DesiredSize.Height : 0;
        if (inlineFooter) return new(width, height + Math.Max(caption.DesiredSize.Height, footer.DesiredSize.Height) + (PhotoMode ? 16 : 0));
        if (caption.IsVisible) height += caption.DesiredSize.Height + (PhotoMode ? 12 : height > 0 ? 6 : 0);
        if (open.IsVisible) height += open.DesiredSize.Height + 6;
        if (!PhotoOnly) height += footer.DesiredSize.Height + 6 + (PhotoMode ? 6 : 0);
        return new(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = Bounds.X; var y = Bounds.Y; var width = finalSize.Width;
        if (attachments.IsVisible)
        { attachments.Arrange(new Rect(x, y, width, attachments.DesiredSize.Height)); y += attachments.DesiredSize.Height; }
        if (caption.IsVisible)
        {
            y += PhotoMode ? 8 : attachments.IsVisible ? 6 : 0;
            caption.Arrange(new Rect(x + (PhotoMode ? 12 : 0), y, Math.Max(1, width - (PhotoMode ? 24 : inlineFooter ? footer.DesiredSize.Width + 10 : 0)), caption.DesiredSize.Height));
            y += caption.DesiredSize.Height;
        }
        if (open.IsVisible) { y += 6; open.Arrange(new Rect(x, y, width, open.DesiredSize.Height)); y += open.DesiredSize.Height; }
        var footerY = PhotoOnly ? Bounds.Bottom - footer.DesiredSize.Height - 5 : inlineFooter ?
            y - footer.DesiredSize.Height + 1 : y + 6;
        footer.Arrange(new Rect(x + width - footer.DesiredSize.Width - (PhotoMode ? 7 : 0), footerY,
            footer.DesiredSize.Width, footer.DesiredSize.Height));
        return finalSize;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (!PhotoMode || captionBackground.A == 0) return;
        var top = attachments.IsVisible ? attachments.Bounds.Bottom : Bounds.Y;
        context.FillRectangle(new Rect(Bounds.X, top, Bounds.Width, Math.Max(0, Bounds.Bottom - top)), captionBackground);
    }
}

internal sealed class ChatPhotoFrame : Panel
{
    // The frame has already fitted the aspect ratio. Fill its pixel-aligned box
    // to avoid a second fit adding subpixel letterboxing after preview downsampling.
    internal readonly Image Picture = new Image().StretchMode(Stretch.Fill);
    internal readonly Image Backdrop = new Image().StretchMode(Stretch.UniformToFill);
    internal readonly ChatMediaSkeleton Skeleton;
    private Size intrinsic;
    internal Size IntrinsicSize { get => intrinsic; set { intrinsic = value; InvalidateMeasure(); } }
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.2, 0, 0, 1));
    private Size fitted;
    internal ChatPhotoFrame(Size intrinsic, Func<bool> active)
    {
        this.intrinsic = intrinsic;
        ClipToBounds = true;
        Skeleton = new(active);
        Add(Backdrop); Add(Picture); Add(Skeleton);
        Picture.Opacity = Backdrop.Opacity = 0;
        fade.TickCallback = value => { Picture.Opacity = Backdrop.Opacity = value; Skeleton.Opacity = 1 - value; };
        fade.CompletedCallback = () => { Skeleton.IsVisible = false; Skeleton.SetActive(false); };
        Skeleton.SetActive(true);
    }
    internal void Show(ImageSource source, ImageSource? backdrop)
    {
        Picture.Source = source; Backdrop.Source = backdrop;
        Skeleton.SetActive(false); fade.Start();
    }
    internal void Stop()
    {
        fade.Stop(); Skeleton.SetActive(false);
        if (Picture.Source != null) { Picture.Opacity = Backdrop.Opacity = 1; Skeleton.IsVisible = false; }
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : ChatImagePreview.Fit(intrinsic).Width;
        fitted = ChatImagePreview.Fit(intrinsic, width);
        Picture.Measure(fitted);
        var area = new Size(width, fitted.Height);
        Backdrop.Measure(area); Skeleton.Measure(area);
        Backdrop.IsVisible = width > fitted.Width + 1;
        return area;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
        Backdrop.Arrange(rect); Skeleton.Arrange(rect);
        Picture.Arrange(new Rect(Bounds.X + (finalSize.Width - fitted.Width) / 2, Bounds.Y,
            fitted.Width, fitted.Height));
        return finalSize;
    }
    protected override void OnDispose() { Stop(); base.OnDispose(); }
}

internal sealed class ChatMediaSkeleton(Func<bool> active) : Control
{
    private readonly AnimationClock pulse = new(TimeSpan.FromMilliseconds(1400), Easing.Linear) { RepeatCount = -1 };
    private double phase;
    internal bool Running => pulse.IsRunning;
    internal void SetActive(bool value)
    {
        IsHitTestVisible = false;
        pulse.TickCallback = amount => { if (active()) { phase = amount; InvalidateVisual(); } };
        if (value) pulse.Start(); else pulse.Stop();
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var color = Color.FromArgb((byte)(18 + 10 * (.5 - .5 * Math.Cos(phase * Math.PI * 2))), 255, 255, 255);
        context.FillRoundedRectangle(Bounds, 12, 12, color);
    }
    protected override void OnDispose() { pulse.Stop(); base.OnDispose(); }
}

