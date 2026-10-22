using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyCardGlassSource(CoverGlassBackdrop glass, Image cover, Image background, FrameworkElement reference)
    {
        var originalCover = cover.Source; var originalBackground = background.Source;
        var opacity = background.Opacity; var transitions = background.Transitions;
        Color Read()
        {
            var region = new Rect(reference.Bounds.X + reference.ActualWidth / 2 - 2,
                reference.Bounds.Y + reference.ActualHeight / 2 - 2, 4, 4);
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(4, 4, 1));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-region.X, -region.Y);
            glass.Draw(context, region, new CornerRadius(0)); context.EndFrame();
            var cpu = (ICpuPixelSurface)surface;
            var pixels = cpu.GetReadOnlyPixelSpan(); var index = 2 * cpu.StrideBytes + 2 * 4;
            return Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
        }
        static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        try
        {
            background.Transitions = []; background.Opacity = 1;
            background.Source = ImageSource.FromBgraPixels(1, 1, [64, 128, 192, 255], hasAlpha: false);
            glass.RefreshArtwork();
            var before = Read();
            // The scrim must be present in the sampled pixels, rather than
            // sampling the brighter uncomposited image or the cover column.
            if (before.R > 130 || before.G > 110 || before.B > 85)
                throw new InvalidOperationException($"Card glass omitted the background scrim: {before}.");
            cover.Source = ImageSource.FromBgraPixels(1, 1, [255, 255, 255, 255], hasAlpha: false);
            glass.RefreshArtwork();
            if (Distance(before, Read()) > 2)
                throw new InvalidOperationException("Card controls sampled the foreground cover instead of the unchanged card background.");
            background.Source = ImageSource.FromBgraPixels(1, 1, [30, 180, 40, 255], hasAlpha: false);
            glass.RefreshArtwork();
            if (Distance(before, Read()) < 35)
                throw new InvalidOperationException("Card glass did not follow the actual background change.");
            background.Source = null; glass.RefreshArtwork();
            var empty = Read();
            if (Math.Max(empty.R, Math.Max(empty.G, empty.B)) > 65)
                throw new InvalidOperationException("Card glass retained an old cover after its background was cleared.");
        }
        finally
        {
            cover.Source = originalCover; background.Source = originalBackground;
            background.Opacity = opacity; background.Transitions = transitions; glass.RefreshArtwork();
        }
        Console.WriteLine("CARD_GLASS_SOURCE_OK: pixel-verified composited background/scrim, no foreground cover sampling, background replacement and empty fallback");
    }
}
