using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Shared by the composer and message bubbles. Two single-line labels preserve
// the compact quote shape even for long names, captions and missing originals.
internal sealed class ChatQuoteView : Panel
{
    private readonly TextBlock title = new TextBlock().FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
    private readonly TextBlock preview = new TextBlock().FontSize(14).TextTrimming(TextTrimming.CharacterEllipsis);
    private readonly Image image = new Image().StretchMode(Stretch.UniformToFill);
    private readonly Border thumbnail;
    private readonly bool composer;
    private readonly Color accent;
    internal string Title => title.Text;
    internal string Preview => preview.Text;
    internal bool HasThumbnail => image.Source != null;
    internal long BindingVersion { get; private set; }

    internal ChatQuoteView(Color accent, bool composer = false)
    {
        this.accent = accent; this.composer = composer;
        if (composer) title.FontWeight = FontWeight.Normal;
        title.Foreground = accent;
        preview.Foreground = composer ? Color.FromRgb(170,170,176) : TrackButtons.OnSurface;
        thumbnail = new Border().BorderThickness(0).Padding(0).CornerRadius(3).ClipToBounds().Child(image).IsVisible(false);
        Add(thumbnail); Add(title); Add(preview);
        IsHitTestVisible = false;
    }
    internal long BeginBinding() => ++BindingVersion;
    internal void Set(string author, string text, IImageSource? source)
    {
        if (title.Text == author && preview.Text == text && ReferenceEquals(image.Source,source)) return;
        title.Text = author; preview.Text = text;
        image.Source = source; thumbnail.IsVisible = source != null;
        InvalidateMeasure();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 360;
        var inset = HasThumbnail ? 46 : 10;
        var textWidth = Math.Max(0,width-inset-8);
        title.Measure(new Size(textWidth,18)); preview.Measure(new Size(textWidth,18));
        thumbnail.Measure(new Size(32,32));
        return new Size(Math.Min(width,Math.Max(80,Math.Max(title.DesiredSize.Width,preview.DesiredSize.Width)+inset+8)),composer ? 40 : 42);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var inset = HasThumbnail ? 46 : 10;
        var y = Bounds.Y+(finalSize.Height-36)/2;
        thumbnail.Arrange(HasThumbnail ? new Rect(Bounds.X+8,Bounds.Y+(finalSize.Height-32)/2,32,32) : Rect.Empty);
        title.Arrange(new Rect(Bounds.X+inset,y,Math.Max(0,finalSize.Width-inset-8),18));
        preview.Arrange(new Rect(Bounds.X+inset,y+18,Math.Max(0,finalSize.Width-inset-8),18));
        return finalSize;
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var radius = composer ? 8 : 4;
        context.FillRoundedRectangle(Bounds,radius,radius,Color.FromArgb(26,accent.R,accent.G,accent.B));
        context.FillRoundedRectangle(new Rect(Bounds.X,Bounds.Y,3,Bounds.Height),1.5,1.5,accent);
    }
}
