using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Pookie.App;

// Only presentation changes: offsets and the document remain the original Unicode.
internal sealed class ChatEmojiInline(TextElement owner, string value) : IInlineTextObject
{
    private double Size => owner.FontSize * 1.3;
    public InlineMetrics Measure() => new(Size, Size, Size * .82);
    public void Draw(ITextRenderContext context, Point origin) =>
        ChatEmojiArtwork.Draw(context.Graphics, value, new Rect(origin.X, origin.Y, Size, Size));
}
