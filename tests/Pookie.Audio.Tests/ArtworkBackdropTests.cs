using Aprillz.MewUI;
using Pookie.App;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ArtworkBackdropTests
{
    private static ImageSource Solid(byte red, byte green, byte blue) => ImageSource.FromBgraPixels(1, 1, [blue, green, red, 255], hasAlpha: false);
    private static byte[] Pixels(ImageSource image)
    { var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4); return pixels; }

    [Fact]
    public void MixedBackdropUsesEveryCoverWithSmoothTransitions()
    {
        var image = ArtworkBackdrop.CreateMixed([Solid(255, 0, 0), Solid(0, 255, 0), Solid(0, 0, 255)], visibility: 1)!;
        var pixels = Pixels(image); var center = (48 * image.PixelWidth + 48) * 4;
        Assert.All(pixels.Where((_, index) => index % 4 == 3), alpha => Assert.Equal(255, alpha));
        for (var channel = 0; channel < 3; channel++) Assert.InRange(pixels[center + channel], 20, 200);
        Assert.True(pixels[2] > pixels[0]);
        var bottom = (image.PixelHeight - 1) * image.PixelWidth * 4;
        Assert.True(pixels[bottom] > pixels[bottom + 2]);
        for (var y = 1; y < image.PixelHeight; y++)
        for (var channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(pixels[y * image.PixelWidth * 4 + channel] - pixels[(y - 1) * image.PixelWidth * 4 + channel]), 0, 5);
    }
    [Fact]
    public void MissingCoversHaveANeutralFallbackAndOneCoverStillWorks()
    {
        Assert.Null(ArtworkBackdrop.CreateMixed([]));
        var cover = Solid(220, 80, 30);
        var image = ArtworkBackdrop.CreateMixed([cover])!;
        var expected = Pixels(ArtworkBackdrop.Create(cover)!);
        Assert.Equal(expected, Pixels(image)[..4]);
    }
}
