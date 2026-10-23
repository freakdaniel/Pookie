using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyExpandedGlassAsync()
    {
        var buttons = new List<ExpandedGlassButton>();
        VisualTree.Visit(expandedCover, element => { if (element is ExpandedGlassButton button) buttons.Add(button); });
        var feedback = buttons.Where(button => button.IsConnected).ToArray();
        var transportButtons = buttons.Where(button => button.Height is 42 or 60).ToArray();
        if (transportButtons.Length != 3 || transportButtons.Max(button => button.Bounds.Y + button.ActualHeight / 2) -
            transportButtons.Min(button => button.Bounds.Y + button.ActualHeight / 2) > 1)
            throw new InvalidOperationException("The previous/next controls do not share the play button's vertical center.");
        if (feedback.Length != 2 || feedback.Any(button => button.Height != 38) ||
            Math.Abs(feedback[1].Bounds.X - feedback[0].Bounds.Right - 2) > 1)
            throw new InvalidOperationException("Fullscreen feedback buttons did not form a compact connected pair.");

        // The sharp green ring in the artwork fixture must soften in the actual
        // sampled backdrop; a translucent flat fill cannot satisfy this check.
        byte[] ReadCover(bool blur)
        {
            if (blur) expandedGlassBackdrop.Prepare();
            var bounds = expandedArtwork.Bounds;
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 1));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-bounds.X, -bounds.Y);
            if (blur) expandedGlassBackdrop.Draw(context, bounds, new CornerRadius(0)); else expandedArtwork.Render(context);
            context.EndFrame();
            var cpu = (ICpuPixelSurface)surface;
            var pixels = cpu.GetReadOnlyPixelSpan();
            var row = new byte[surface.PixelWidth];
            for (var x = 0; x < row.Length; x++) row[x] = pixels[(surface.PixelHeight / 2) * cpu.StrideBytes + x * 4 + 1];
            return row;
        }
        var raw = ReadCover(false); var blurred = ReadCover(true);
        int Edge(byte[] row) => Enumerable.Range(20, row.Length - 40).Max(x => Math.Abs(row[x + 1] - row[x]));
        if (!expandedGlassBackdrop.HasBlurredArtwork || Edge(raw) < 20 || Edge(blurred) >= Edge(raw) * .7 ||
            blurred.Max() - blurred.Min() < 25)
            throw new InvalidOperationException($"Fullscreen controls did not blur the artwork: raw edge={Edge(raw)}, blurred edge={Edge(blurred)}.");

        var liked = isLiked.Value; var disliked = isDisliked.Value; var shuffled = shuffle.Value;
        var source = expandedArtwork.Source;
        try
        {
            isLiked.Value = false; isDisliked.Value = false;
            await WaitForLikedLayoutAsync(() => Math.Abs(feedback[0].GlassCorners.TopRight - 7) < .01 &&
                Math.Abs(feedback[1].GlassCorners.TopLeft - 7) < .01);
            isLiked.Value = true;
            await WaitForLikedLayoutAsync(() => Math.Abs(feedback[0].GlassCorners.TopRight - 19) < .01);
            var shuffleButton = buttons.Single(button => button.Content is Image glyph && ReferenceEquals(glyph.Source, Icons.Source("shuffle", Color.White)));
            RouteExpandedPointer(SettingsCenter(shuffleButton));
            RouteWaveformClick(SettingsCenter(shuffleButton));
            await WaitForLikedLayoutAsync(() => shuffle.Value != shuffled);
            RouteExpandedPointer(new Point(expandedCover.Bounds.X + 8, expandedCover.Bounds.Y + expandedCover.ActualHeight / 2));
            if (KeyboardFocusFeedback.IsVisible(shuffleButton)) throw new InvalidOperationException("Pointer activation left a keyboard focus cue on glass controls.");

            var replacement = ImageSource.FromBgraPixels(1, 1, [35, 45, 210, 255], hasAlpha: false);
            SetPlayerArtwork(replacement, playGeneration);
            await WaitForLoginFrameAsync();
            var replaced = ReadCover(true);
            if (replaced.Max() > 55 || replaced.Min() < 35 || Math.Abs(replaced.Average(value => (double)value) - blurred.Average(value => (double)value)) < 30)
                throw new InvalidOperationException("The glass backdrop retained pixels from the previous artwork.");
            shuffle.Value = false;
            RouteExpandedPointer(new Point(expandedCover.Bounds.X + 8, expandedCover.Bounds.Y + expandedCover.ActualHeight / 2));
            await WaitForLoginFrameAsync();
            foreach (var brightness in new byte[] { 255, 8 })
            {
                SetPlayerArtwork(ImageSource.FromBgraPixels(1, 1, [brightness, brightness, brightness, 255], hasAlpha: false), playGeneration);
                await WaitForLoginFrameAsync();
                ReadCover(true);
                var pixels = CaptureSectionButton(shuffleButton);
                var width = (int)Math.Ceiling(shuffleButton.ActualWidth * Window.DpiScale);
                var index = ((int)(shuffleButton.ActualHeight / 2 * Window.DpiScale) * width + (int)(5 * Window.DpiScale)) * 4;
                var painted = Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
                double Luminance(Color color)
                {
                    double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
                    return Linear(color.R) * .2126 + Linear(color.G) * .7152 + Linear(color.B) * .0722;
                }
                var contrast = (Luminance(TrackButtons.OnSurface) + .05) / (Luminance(painted) + .05);
                if (contrast < 4.5 || brightness == 255 && painted.R > 115 || brightness == 8 && painted.R > 65)
                    throw new InvalidOperationException($"Glass controls lost contrast on artwork brightness {brightness}: surface={painted}, contrast={contrast:F2}.");
                CaptureUiPreview(brightness == 255 ? "expanded-glass-light" : "expanded-glass-dark");
            }
            if (source is ImageSource original) SetPlayerArtwork(original, playGeneration);
            RouteExpandedPointer(SettingsCenter(feedback[0]));
            await WaitForLikedLayoutAsync(() => ((UIElement)expandedVolume.Parent!).Opacity > .999);
            CaptureUiPreview("expanded-glass-feedback");
        }
        finally
        {
            isLiked.Value = liked; isDisliked.Value = disliked; shuffle.Value = shuffled;
            if (source is ImageSource original) SetPlayerArtwork(original, playGeneration);
            RouteExpandedPointer(new Point(1, 1));
        }
        Console.WriteLine("EXPANDED_GLASS_PIXELS_OK: centered transport, softened cover edges, connected selected feedback, fresh artwork samples and pixel-verified contrast over white/dark covers");
    }
}
