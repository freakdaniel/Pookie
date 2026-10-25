using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Messages;

namespace Pookie.App;

internal interface IChatMusicFooter
{
    void ReserveFooter(double width);
}

// Measure the caption before choosing a media bubble's width. The image stays at
// its natural size; a wider caption extends its backdrop, never the image itself.
internal sealed class ChatBubbleContent(FrameworkElement attachments, TextBlock caption, FrameworkElement open, Border footer) : Panel
{
    private readonly AnimationClock resize = new(TimeSpan.FromMilliseconds(260), Easing.CubicBezier(.2, 0, 0, 1));
    private Size resizeFrom, resizeTo;
    private double resizeAmount = 1;
    private bool resizePending;
    internal bool IsResizing => resize.IsRunning;
    internal Action? ResizeTick;
    internal void AnimateMediaChange()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        resizeFrom = new(ActualWidth, ActualHeight); resizePending = true;
    }
    internal void StopResize() { resize.Stop(); resizePending = false; resizeAmount = 1; }
    private Size AnimateSize(Size natural)
    {
        if (resizePending)
        {
            resizePending = false; resizeTo = natural;
            if (Math.Abs(resizeFrom.Width - natural.Width) > .5 || Math.Abs(resizeFrom.Height - natural.Height) > .5)
            {
                resizeAmount = 0;
                resize.TickCallback = value => { resizeAmount = value; InvalidateMeasure(); ResizeTick?.Invoke(); };
                resize.CompletedCallback = () => { resizeAmount = 1; InvalidateMeasure(); ResizeTick?.Invoke(); };
                resize.Start();
            }
        }
        if (!resize.IsRunning) return natural;
        resizeTo = natural;
        return new(resizeFrom.Width + (resizeTo.Width - resizeFrom.Width) * resizeAmount,
            resizeFrom.Height + (resizeTo.Height - resizeFrom.Height) * resizeAmount);
    }
    protected override void OnDispose() { StopResize(); base.OnDispose(); }
    private Size photoSize = new(240, 180);
    private bool inlineFooter, videoMode;
    private bool fileOnly, musicMode, musicOnly, voiceOnly;
    private ChatVoiceCard? voice;
    private Color captionBackground;
    internal Color CaptionBackground { get => captionBackground; set { captionBackground = value; InvalidateVisual(); } }
    internal bool PhotoMode { get; private set; }
    internal bool InlineFooter => inlineFooter;
    internal bool PhotoOnly => PhotoMode && !caption.IsVisible;
    internal double MusicCaptionTop => musicMode && caption.IsVisible ? attachments.Bounds.Bottom : Bounds.Bottom;
    internal ChatBubbleContent Initialize() { Add(attachments); Add(caption); Add(open); Add(footer); return this; }
    internal void SetPhoto(Size? size, bool video = false) { videoMode = video; PhotoMode = true; photoSize = size ?? new(180, 120); InvalidateMeasure(); }
    internal void SetMusic() { musicMode = true; InvalidateMeasure(); }
    internal void Reset() { musicMode = videoMode = false; PhotoMode = false; CaptionBackground = Color.Transparent; InvalidateMeasure(); }

    protected override Size MeasureContent(Size availableSize)
    {
        var available = double.IsFinite(availableSize.Width) ? availableSize.Width : 496;
        var width = Math.Min(musicMode ? 680 : 496, available);
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var captionWidth = caption.DesiredSize.Width;
        if (PhotoMode)
        {
            width = Math.Min(available, videoMode ? Math.Min(440, Math.Max(ChatImagePreview.Fit(photoSize, 300, 480).Width,
                caption.IsVisible ? Math.Max(220, caption.DesiredSize.Width + 24) : 0)) :
                ChatImagePreview.MessageWidth(photoSize, caption.IsVisible ? caption.DesiredSize.Width : null));
        }
        footer.Measure(new Size(Math.Max(1, width - (PhotoMode ? 16 : 0)), double.PositiveInfinity));
        var file = attachments is StackPanel { Children.Count: 1 } ? attachments.FindVisualChild<ChatFileCard>() as ChatFileCard : null;
        fileOnly = !PhotoMode && !caption.IsVisible && !open.IsVisible && file != null;
        file?.ReserveFooter(fileOnly ? footer.DesiredSize.Width + 8 : 0);
        voice = attachments is StackPanel { Children.Count: 1 } ? attachments.FindVisualChild<ChatVoiceCard>() as ChatVoiceCard : null;
        voiceOnly = !PhotoMode && !caption.IsVisible && !open.IsVisible && voice != null;
        voice?.ReserveFooter(voiceOnly ? footer.DesiredSize.Width + 8 : 0);
        musicOnly = musicMode && !caption.IsVisible && !open.IsVisible;
        if (attachments is StackPanel music)
            foreach (var child in music.Children.OfType<IChatMusicFooter>())
                child.ReserveFooter(musicOnly && ReferenceEquals(child, music.Children.LastOrDefault()) ? footer.DesiredSize.Width + 10 : 0);
        attachments.Measure(new Size(width, double.PositiveInfinity));
        if (musicMode)
            width = Math.Min(width, Math.Max(attachments.DesiredSize.Width, caption.IsVisible ? Math.Min(width, captionWidth + footer.DesiredSize.Width + 10) : 0));
        caption.Measure(new Size(Math.Max(1, width - (PhotoMode ? 24 : 0)), double.PositiveInfinity));
        open.Measure(new Size(width, double.PositiveInfinity));
        footer.Measure(new Size(Math.Max(1, width - (PhotoMode ? 16 : 0)), double.PositiveInfinity));
        inlineFooter = caption.IsVisible && !open.IsVisible && (musicMode ||
            (PhotoMode || !attachments.IsVisible) && !caption.Text.Contains('\n') && captionWidth + footer.DesiredSize.Width + (PhotoMode ? 36 : 10) <= width);
        if (inlineFooter && musicMode)
            caption.Measure(new Size(Math.Max(1, width - footer.DesiredSize.Width - 10), double.PositiveInfinity));
        else if (inlineFooter && !PhotoMode)
        {
            width = captionWidth + footer.DesiredSize.Width + 10;
            caption.Measure(new Size(captionWidth, double.PositiveInfinity));
        }
        if (!PhotoMode && !inlineFooter) width = Math.Min(width, Math.Max(attachments.DesiredSize.Width,
            Math.Max(caption.DesiredSize.Width, Math.Max(open.DesiredSize.Width, footer.DesiredSize.Width))));
        var height = attachments.IsVisible ? attachments.DesiredSize.Height : 0;
        if (fileOnly || musicOnly || voiceOnly) return AnimateSize(new(width, height));
        if (inlineFooter) return AnimateSize(new(width, height + Math.Max(caption.DesiredSize.Height, footer.DesiredSize.Height) + (PhotoMode ? 16 : musicMode ? 6 : 0)));
        if (caption.IsVisible) height += caption.DesiredSize.Height + (PhotoMode ? 12 : height > 0 ? 6 : 0);
        if (open.IsVisible) height += open.DesiredSize.Height + 6;
        if (!PhotoOnly) height += footer.DesiredSize.Height + 6 + (PhotoMode ? 6 : 0);
        return AnimateSize(new(width, height));
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
        var footerY = voiceOnly && voice != null ? voice.DurationLabel.Bounds.Bottom-footer.DesiredSize.Height : musicOnly ? attachments.Bounds.Bottom - 20 - footer.DesiredSize.Height / 2 : fileOnly ? attachments.Bounds.Bottom - 7 - footer.DesiredSize.Height : PhotoOnly ? Bounds.Bottom - footer.DesiredSize.Height - 5 : inlineFooter ?
            y - footer.DesiredSize.Height + 1 : y + 6;
        footer.Arrange(new Rect(x + width - footer.DesiredSize.Width - (PhotoMode ? 7 : 0), footerY,
            footer.DesiredSize.Width, footer.DesiredSize.Height));
        return finalSize;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if ((!PhotoMode && !musicMode) || captionBackground.A == 0) return;
        var top = attachments.IsVisible ? attachments.Bounds.Bottom : Bounds.Y;
        context.FillRectangle(new Rect(Bounds.X, top, Bounds.Width, Math.Max(0, Bounds.Bottom - top)), captionBackground);
    }
}

internal sealed class ChatPhotoFrame : Panel
{
    // The frame has already fitted the aspect ratio. Fill its pixel-aligned box
    // to avoid a second fit adding subpixel letterboxing after preview downsampling.
    internal readonly Image Picture = new Image().StretchMode(Stretch.Fill);
    private readonly Image preview = new Image().StretchMode(Stretch.Fill);
    internal IImageSource? PreviewSource => preview.Source;
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
        Add(Backdrop); Add(preview); Add(Picture); Add(Skeleton);
        Picture.Opacity = Backdrop.Opacity = 0;
        fade.TickCallback = value => { Picture.Opacity = value; Backdrop.Opacity = 1; preview.Opacity = 1 - value; Skeleton.Opacity = 1 - value; };
        fade.CompletedCallback = () => { Skeleton.IsVisible = false; Skeleton.SetActive(false); preview.Source = null; };
        Skeleton.SetActive(true);
    }
    internal void ShowPreview(ImageSource source, ImageSource? backdrop)
    {
        preview.Source = source; preview.Opacity = 1;
        Backdrop.Source = backdrop; Backdrop.Opacity = 1;
        Skeleton.IsVisible = false; Skeleton.SetActive(false);
    }
    internal void ShowImmediate(ImageSource source, ImageSource? backdrop)
    {
        fade.Stop(); Picture.Source = source; Backdrop.Source = backdrop;
        Picture.Opacity = Backdrop.Opacity = 1; preview.Source = null;
        Skeleton.IsVisible = false; Skeleton.SetActive(false);
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
        Picture.Measure(fitted); preview.Measure(fitted);
        var area = new Size(width, fitted.Height);
        Backdrop.Measure(area); Skeleton.Measure(area);
        Backdrop.IsVisible = width > fitted.Width + 1;
        return area;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        fitted = ChatImagePreview.Fit(intrinsic, finalSize.Width);
        Backdrop.IsVisible = finalSize.Width > fitted.Width + 1 || finalSize.Height > fitted.Height + 1;
        var rect = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
        Backdrop.Arrange(rect); Skeleton.Arrange(rect);
        preview.Arrange(new Rect(Bounds.X + (finalSize.Width - fitted.Width) / 2, Bounds.Y, fitted.Width, fitted.Height));
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

