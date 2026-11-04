using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyCommentTextPixels()
    {
        VerifyCombiningTextPixels();
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
    private void VerifyCombiningTextPixels()
    {
        var attack = "зацените ласт трек!! ฏ" + new string('\u0e4e', 500) + "ฏ" + new string('\u0e4e', 500) + " конец 😀";
        foreach (var fixture in new[] { attack, "X\u0300\u0301\u0302\u0303\u0304\u0305\u0306\u0307 конец" })
        foreach (var block in new TextBlock[] { new CommentTextBlock(), new ChatMessageText(), new DescriptionTextBlock(), new TextBlock() })
        {
            using (block)
            {
                if (fixture != attack && block.GetType() == typeof(TextBlock)) continue;
                block.Text = fixture; block.FontFamily = trackCommentInput.FontFamily;
                block.FontSize = 14; block.Foreground = Color.White; block.TextWrapping = TextWrapping.Wrap;
                block.Measure(new Size(320, double.PositiveInfinity));
                if (block.DesiredSize.Height > 64) throw new InvalidOperationException($"Combining text inflated {block.GetType().Name}: {block.DesiredSize}.");
                var bounds = new Rect(32, 160, 320, block.DesiredSize.Height);
                block.Arrange(bounds);
                var scale = Window.DpiScale;
                using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
                using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                    (int)Math.Ceiling(400 * scale), (int)Math.Ceiling(400 * scale), scale));
                using var context = Window.GraphicsFactory.CreateContext(surface);
                context.BeginFrame(surface); context.Clear(Color.Transparent);
                block.Render(context); context.EndFrame();
                var cpu = (ICpuPixelSurface)surface; var pixels = cpu.CopyPixels(); var painted = 0;
                var width = (int)Math.Ceiling(400 * scale); var height = (int)Math.Ceiling(400 * scale);
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    if (pixels[y * cpu.StrideBytes + x * 4 + 3] == 0) continue;
                    painted++;
                    if (x < Math.Floor(bounds.X * scale) - 1 || x > Math.Ceiling(bounds.Right * scale) + 1 ||
                        y < Math.Floor(bounds.Y * scale) - 1 || y > Math.Ceiling(bounds.Bottom * scale) + 1)
                        throw new InvalidOperationException($"Combining ink escaped {block.GetType().Name}: pixel={x},{y}, bounds={bounds}.");
                }
                if (painted < 100 || block.Text != fixture) throw new InvalidOperationException("Combining protection erased the message or changed its source.");
                if (block is ChatMessageText message)
                {
                    string? copied = null; message.CopyText = value => copied = value;
                    message.SelectAll(); message.Copy();
                    if (copied != fixture || message.SelectionLength != fixture.Length)
                        throw new InvalidOperationException("Combining protection changed selection/copy offsets.");
                }
            }
        }
        Console.WriteLine("COMBINING_TEXT_PIXELS_OK: 1000 stacked Thai marks stay inside comment/chat/description/label bounds; short-stack ink clipping, compact layout and exact original selection/copy");
    }

}
