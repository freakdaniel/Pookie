using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Centred mode icons. Selected modes are passive, including focus.
internal sealed class ChatEmojiModeBar : Panel
{
    internal Button Emoji { get; }
    internal Button Gif { get; }
    internal ChatEmojiModeBar(Action<bool> select)
    {
        Emoji = TrackButtons.Icon(Icons.View("smiley", 23, TrackButtons.OnSurface), () => select(false), TrackButtons.Text, 36);
        Gif = TrackButtons.Action(new Border().Width(26).Height(20).CornerRadius(5).Padding(1).BorderThickness(1)
            .BorderBrush(TrackButtons.OnSurface).Background(Color.Transparent).Child(Icons.View("gif", 20, TrackButtons.OnSurface).Center()),
            () => select(true), TrackButtons.Text, 36).Width(36).Padding(0);
        Add(Emoji); Add(Gif);
    }
    internal void SetMode(bool gif, bool allowGif)
    {
        Gif.IsVisible = allowGif;
        Select(Emoji, !gif); Select(Gif, gif);
        InvalidateMeasure();
    }
    private static void Select(Button button, bool active)
    {
        TrackButtons.SetVariant(button, active ? TrackButtons.Tonal : TrackButtons.Text);
        ((MaterialTrackButton)button).SetPassive(active); button.Focusable = !active;
    }
    protected override Size MeasureContent(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(36, 36));
        return new(availableSize.Width, 48);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = Bounds.Y + (finalSize.Height - 36) / 2;
        var start = Bounds.X + finalSize.Width / 2 - (Gif.IsVisible ? 42 : 18);
        Emoji.Arrange(new Rect(start, y, 36, 36));
        Gif.Arrange(Gif.IsVisible ? new Rect(start + 48, y, 36, 36) : Rect.Empty);
        return finalSize;
    }
    protected override void OnRender(IGraphicsContext context) =>
        context.FillRectangle(new Rect(Bounds.X, Bounds.Y, Bounds.Width, 1), Color.FromArgb(14, 255, 255, 255));
}
