using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyCommentTextPixels()
    {
        const string text = "Комментарий 🎧 e\u0301 — переносы строк и Unicode.\n" +
            "Длинная строка должна переноситься одинаково при изменении ширины карточки. 日本語";
        using var actual = new CommentTextBlock { TextWrapping = TextWrapping.Wrap };
        using var expected = new TextBlock { TextWrapping = TextWrapping.Wrap };
        foreach (var block in new TextBlock[] { actual, expected })
        { block.Text = text; block.FontFamily = trackCommentInput.FontFamily; block.FontSize = 14; block.FontWeight = FontWeight.Normal; block.Foreground = Muted; }
        foreach (var fixture in new[]
        {
            (text, TextWrapping.Wrap, TextTrimming.None),
            ("Ответить · на 0:10", TextWrapping.NoWrap, TextTrimming.None),
            ("Очень длинное имя слушателя SoundCloud 🎧", TextWrapping.NoWrap, TextTrimming.CharacterEllipsis)
        })
        {
            foreach (var block in new TextBlock[] { actual, expected })
            { block.Text = fixture.Item1; block.TextWrapping = fixture.Item2; block.TextTrimming = fixture.Item3; block.MaxWidth = fixture.Item3 == TextTrimming.None ? double.PositiveInfinity : 200; }
            foreach (var width in new[] { 211.25, 480.5, 90.75, 320.75, 211.25 })
            {
                actual.Measure(new Size(width, double.PositiveInfinity)); expected.Measure(new Size(width, double.PositiveInfinity));
                if (actual.DesiredSize != expected.DesiredSize) throw new InvalidOperationException($"Comment text measurement changed: mode={fixture.Item2}/{fixture.Item3} width={width} actual={actual.DesiredSize} expected={expected.DesiredSize}.");
                var height = expected.DesiredSize.Height;
                actual.Arrange(new Rect(0, 0, width, height)); expected.Arrange(new Rect(0, 0, width, height));
                byte[] Pixels(TextBlock block)
                {
                    using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
                    using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                        (int)Math.Ceiling((width + 2) * Window.DpiScale), (int)Math.Ceiling((height + 2) * Window.DpiScale), Window.DpiScale));
                    using var context = Window.GraphicsFactory.CreateContext(surface);
                    context.BeginFrame(surface);
                    try { context.Clear(Color.Transparent); block.Render(context); }
                    finally { context.EndFrame(); }
                    return ((ICpuPixelSurface)surface).CopyPixels();
                }
                if (!Pixels(actual).SequenceEqual(Pixels(expected))) throw new InvalidOperationException($"Comment text pixels changed: mode={fixture.Item2}/{fixture.Item3} width={width} actual={actual.Bounds} expected={expected.Bounds}.");
            }
        }
        Console.WriteLine("COMMENT_TEXT_PIXELS_OK: wrapped Unicode, emoji, line breaks, plain labels, ellipsis and repeated fractional-width resizing match TextBlock pixels");
    }
}
