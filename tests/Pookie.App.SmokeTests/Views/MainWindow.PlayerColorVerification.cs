using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlayerColorsAsync()
    {
        var grayGradient = new PlayerPalette(Color.FromRgb(25, 25, 25), Color.FromRgb(45, 45, 45), Color.FromRgb(30, 30, 30));
        var raster = PlayerBackdrop.CreateRaster(grayGradient, 1024);
        if (!raster.SequenceEqual(PlayerBackdrop.CreateRaster(grayGradient, 1024)))
            throw new InvalidOperationException("Gradient noise changed between identical frames");
        var mixedColumns = 0;
        for (var x = 0; x < 1024; x++)
        {
            var levels = Enumerable.Range(0, 64).Select(y => raster[(y * 1024 + x) * 4]).ToArray();
            if (levels.Max() - levels.Min() > 1) throw new InvalidOperationException("Gradient dithering introduced visible grain");
            if (levels.Max() != levels.Min()) mixedColumns++;
        }
        if (mixedColumns < 800) throw new InvalidOperationException("Gradient retained wide quantized bands");
        for (var i = 0; i < raster.Length; i += 4)
            if (raster[i] != raster[i + 1] || raster[i] != raster[i + 2] || raster[i + 3] != 255)
                throw new InvalidOperationException("Gradient noise introduced colour or transparency artifacts");

        static ImageSource Cover(Color color, bool transparent = false)
        {
            var pixels = new byte[64 * 64 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R;
                pixels[i + 3] = transparent ? (byte)0 : (byte)255;
            }
            return ImageSource.FromBgraPixels(64, 64, pixels);
        }

        var red = Cover(Color.FromRgb(218, 45, 37));
        var blue = Cover(Color.FromRgb(38, 89, 222));
        var redPalette = await Task.Run(() => PlayerPalette.FromArtwork(red));
        var bluePalette = await Task.Run(() => PlayerPalette.FromArtwork(blue));
        if (redPalette.Middle.R < redPalette.Middle.B + 25 || bluePalette.Middle.B < bluePalette.Middle.R + 25)
            throw new InvalidOperationException("Artwork palette lost the dominant cover hue");
        if (PlayerPalette.FromArtwork(Cover(Color.FromRgb(255, 0, 0), true)) != PlayerPalette.Neutral)
            throw new InvalidOperationException("Transparent artwork coloured the player");
        var gray = PlayerPalette.FromArtwork(Cover(Color.FromRgb(225, 225, 225))).Middle;
        if (gray.R != gray.G || gray.G != gray.B) throw new InvalidOperationException("Grayscale cover introduced a hue");
        var assembly = typeof(MainWindow).Assembly;
        using (var resource = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("soundcloud-mark-white.png")))!)
        {
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            var encoded = ImageSource.FromBytes(bytes.ToArray());
            var decoded = await Task.Run(() => PlayerPalette.FromArtwork(encoded));
            if (decoded.Middle.R != decoded.Middle.G || decoded.Middle.G != decoded.Middle.B)
                throw new InvalidOperationException("Encoded PNG decoding introduced a hue");
        }
        static double Luminance(Color color)
        {
            static double Linear(byte value) => value <= 10 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        foreach (var color in new[] { Color.FromRgb(255, 255, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(255, 255, 255) })
        {
            var palette = PlayerPalette.FromArtwork(Cover(color));
            foreach (var stop in new[] { palette.Start, palette.Middle, palette.End })
                if ((Luminance(Muted) + .05) / (Luminance(stop) + .05) < 4.5)
                    throw new InvalidOperationException("Player gradient reduced secondary text contrast");
        }

        playerBackdrop.Reset();
        SetPlayerArtwork(red, playGeneration);
        await WaitForLikedLayoutAsync(() => playerBackdrop.Target == redPalette && playerBackdrop.Current != PlayerPalette.Neutral);
        if (!playerBackdrop.Running || playerBackdrop.Current == redPalette)
            throw new InvalidOperationException("Player colour jumped instead of transitioning");
        await WaitForLikedLayoutAsync(() => !playerBackdrop.Running);
        await WaitForLoginFrameAsync();
        CheckPlayerRegion(); CaptureUiPreview("player-cover-red");

        // An old cover finishing after the next selection must not recolour the current track.
        var lateCover = Cover(Color.FromRgb(218, 45, 37));
        var latePalette = new TaskCompletionSource<PlayerPalette>(TaskCreationOptions.RunContinuationsAsynchronously);
        playerPalettes.Add(lateCover, latePalette.Task);
        SetPlayerArtwork(lateCover, playGeneration - 1);
        SetPlayerArtwork(blue, playGeneration);
        await WaitForLikedLayoutAsync(() => playerBackdrop.Target == bluePalette && playerBackdrop.Current != redPalette);
        var intermediate = playerBackdrop.Current;
        playerBackdrop.SetPalette(redPalette);
        if (playerBackdrop.Current != intermediate) throw new InvalidOperationException("Interrupted colour transition jumped");
        playerBackdrop.SetPalette(bluePalette);
        await WaitForLikedLayoutAsync(() => !playerBackdrop.Running);
        latePalette.SetResult(redPalette);
        await Task.Delay(100, lifetime.Token);
        await WaitForLoginFrameAsync();
        if (playerBackdrop.Current != bluePalette) throw new InvalidOperationException("Stale artwork replaced the current palette");
        CaptureUiPreview("player-cover-blue");

        SetPlayerArtwork(null, playGeneration);
        await WaitForLikedLayoutAsync(() => !playerBackdrop.Running);
        if (playerBackdrop.Current != PlayerPalette.Neutral) throw new InvalidOperationException("Missing artwork kept the old cover colour");
        Console.WriteLine("UI_PLAYER_COLORS_OK: stable one-level dithering, cover hues, grayscale/transparent fallback, text contrast, animated transition, interruption and stale-cover protection");
    }
}
