using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

internal sealed class ChatMusicInline(TextElement owner, ChatMusicEditor.Link link) : IInlineTextObject, IDisposable
{
    internal readonly ChatMusicEditor.Link Link = link;
    internal Rect Bounds { get; private set; }
    private ITextLayout? label;
    private IImage? cover;
    private double measuredWidth;
    internal double MaximumWidth = 460;
    public InlineMetrics Measure()
    {
        var factory = Application.Current.GraphicsFactory;
        var maximum = Math.Clamp(owner is MultiLineTextBox editor ? editor.ActualWidth - editor.Padding.HorizontalThickness - 4 : MaximumWidth, 48, 460);
        label = factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Dpi = (uint)Math.Round(96 * (owner.FindVisualRoot() is Window window ? window.DpiScale : 1)),
            Text = Link.Label.AsMemory(), DefaultStyle = new(owner.FontFamily, 14, FontWeight.SemiBold),
            Paragraph = new() { MaxWidth = maximum - 26, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.CharacterEllipsis }
        });
        measuredWidth = Math.Clamp(label.MeasuredSize.Width + 26, 48, maximum);
        return new(measuredWidth, 22, 16);
    }
    public void Draw(ITextRenderContext context, Point origin)
    {
        if (label == null) Measure();
        Bounds = new(origin.X, origin.Y, measuredWidth, 22);
        var graphics = context.Graphics;
        var picture = new Rect(origin.X, origin.Y + 2, 18, 18);
        if (Link.Cover != null)
        {
            cover ??= Link.Cover.CreateImage(Application.Current.GraphicsFactory, 48, 48);
            graphics.Save();
            try { graphics.SetClipRoundedRect(picture, 9, 9); graphics.DrawImage(cover, picture); }
            finally { graphics.Restore(); }
        }
        else graphics.FillRoundedRectangle(picture, 9, 9, Color.FromArgb(30, 128, 196, 249));
        context.Draw(label!, new(origin.X + 24, origin.Y + (22 - label!.MeasuredSize.Height) / 2), new TextDrawOptions(Color.FromRgb(128, 196, 249)));
    }
    internal void ClearBounds() => Bounds = Rect.Empty;
    public void Dispose() { cover?.Dispose(); cover = null; ClearBounds(); }
}
