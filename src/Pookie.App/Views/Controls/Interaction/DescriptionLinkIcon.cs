using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

// Only replaces the first displayed character, leaving URLs wrappable and the
// document/clipboard untouched. Uses the composer's circular miniature treatment.
internal sealed class DescriptionLinkIcon(DescriptionTextBlock owner,
    string character, Func<Color> foreground) : IInlineTextObject, IDisposable
{
    private ITextLayout? label;
    private IImage? image;
    private ImageSource? source;
    private InlineMetrics metrics;
    internal int CharacterLength => character.Length;
    internal Rect Bounds { get; private set; }
    internal bool HasSource => source != null;
    internal void SetSource(ImageSource value) { image?.Dispose(); image = null; source = value; }
    public InlineMetrics Measure()
    {
        label = Application.Current.GraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = character.AsMemory(),
            Dpi = (uint)Math.Round(96 * (owner.FindVisualRoot() is Window window ? window.DpiScale : 1)),
            DefaultStyle = new(owner.FontFamily, owner.FontSize, owner.FontWeight),
            Paragraph = new() { Wrapping = TextWrapping.NoWrap }
        });
        var baseline = label.Lines.Count > 0 ? label.Lines[0].Baseline : owner.FontSize;
        return metrics = new(18 + label.MeasuredSize.Width, Math.Max(16, label.MeasuredSize.Height), Math.Max(13, baseline));
    }
    public void Draw(ITextRenderContext context, Point origin)
    {
        if (label == null) Measure();
        var picture = Bounds = new Rect(origin.X, origin.Y + metrics.Baseline - 12, 14, 14);
        var graphics = context.Graphics;
        graphics.Save();
        try
        {
            graphics.SetClipRoundedRect(picture, 7, 7);
            graphics.FillRoundedRectangle(picture, 7, 7, Color.FromArgb(35, 166, 195, 246));
            if (source != null)
            {
                image ??= source.CreateImage(Application.Current.GraphicsFactory, 32, 32);
                graphics.DrawImage(image, picture);
            }
            else
            {
                // A small globe, rather than an empty gap while loading/offline.
                graphics.DrawRoundedRectangle(new(picture.X + 2, picture.Y + 2, 10, 10), 5, 5, foreground(), 1);
                graphics.DrawRoundedRectangle(new(picture.X + 5, picture.Y + 2, 4, 10), 2, 5, foreground(), .8);
                graphics.DrawLine(new(picture.X + 2, picture.Y + 7), new(picture.Right - 2, picture.Y + 7), foreground(), .8);
            }
        }
        finally { graphics.Restore(); }
        context.Draw(label!, new(origin.X + 18, origin.Y + metrics.Baseline - label!.Lines[0].Baseline), new TextDrawOptions(foreground()));
    }
    public void Dispose() { image?.Dispose(); image = null; source = null; Bounds = Rect.Empty; }
}
