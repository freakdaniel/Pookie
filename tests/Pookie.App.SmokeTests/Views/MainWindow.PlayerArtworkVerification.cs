using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlayerArtworkAsync()
    {
        var original = player;
        using var audio = new BufferFixturePlayer();
        timer.Stop(); player = audio;
        try
        {
            const string url = "https://i1.sndcdn.com/player-artwork-fixture-large.jpg";
            const string fullUrl = "https://i1.sndcdn.com/player-artwork-fixture-t500x500.jpg";
            var fullBytes = ArtworkFixtureBitmap(500);
            await imageDiskCache.WriteAsync(url, ArtworkFixtureBitmap(100), lifetime.Token);
            await imageDiskCache.WriteAsync(fullUrl, fullBytes, lifetime.Token);
            var track = new SoundCloudTrack { Id = 79001, Title = "Проверка обложки", ArtworkUrl = url,
                Duration = 180000, User = new() { Username = "Pookie preview" } };
            ReplaceTracks(new([track], null)); SetQueue(track);
            await PlayAsync(track);
            await WaitForLikedLayoutAsync(() => artwork.Source is ImageSource image && image.PixelWidth == 500);
            var shared = libraryCoverCache[track.Id];
            if (!ReferenceEquals(artwork.Source, shared) || !ReferenceEquals(expandedArtwork.Source, shared) ||
                !ReferenceEquals(await GetLibraryArtworkAsync(track), shared))
                throw new InvalidOperationException("Player and card used different cover sources.");
            await VerifyMainPlaybackGlassAsync();
            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning && expandedArtwork.ActualWidth > 200);
            VerifyExpandedArtworkPixels(); CaptureUiPreview("artwork-first-open");
            SetExpandedPlayer(false); await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            SetExpandedPlayer(true); await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            VerifyExpandedArtworkPixels(); CaptureUiPreview("artwork-reopened");
            SetExpandedPlayer(false); await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);

            // A compact-row cache from earlier playback cannot lock in a thumbnail.
            var next = track with { Id = 79002 };
            coverCache[next.Id] = ImageSource.FromBytes(ArtworkFixtureBitmap(100));
            SetQueue(next); await PlayAsync(next);
            await WaitForLikedLayoutAsync(() => artwork.Source is ImageSource image && image.PixelWidth == 500);
            if (!ReferenceEquals(artwork.Source, shared) || !ReferenceEquals(coverCache[next.Id], shared))
                throw new InvalidOperationException("A thumbnail cache suppressed full-size playback artwork.");

            const string arrivingUrl = "https://i1.sndcdn.com/player-artwork-arriving-large.jpg";
            var arrivingReply = new TaskCompletionSource<ImageSource?>();
            artworkRequests[arrivingUrl] = arrivingReply.Task;
            var arriving = track with { Id = 79004, ArtworkUrl = arrivingUrl };
            SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            SetQueue(arriving); await PlayAsync(arriving);
            await WaitForLoginFrameAsync();
            if (!ReferenceEquals(expandedArtwork.Source, shared) || expandedArtworkFade.ContentOpacity != 0)
                throw new InvalidOperationException("The fullscreen player replaced its cover while the next one was pending.");
            await WaitForLikedLayoutAsync(() => expandedGlassBackdrop.IsCurrent);
            var center = expandedArtwork.Bounds.Center;
            var lensBounds = new Rect(center.X - 24, center.Y - 24, 48, 48);
            using var lens = new LiquidGlassLens();
            var beforeGlass = ReadArtworkLens(lensBounds, lens);
            var arrivingPixels = new byte[500 * 500 * 4];
            for (var i = 0; i < arrivingPixels.Length; i += 4)
            {
                arrivingPixels[i] = 32; arrivingPixels[i + 1] = 155; arrivingPixels[i + 2] = 225; arrivingPixels[i + 3] = 255;
            }
            var arrivingSource = ImageSource.FromBgraPixels(500, 500, arrivingPixels, hasAlpha: false);
            var blended = false;
            Color duringGlass = default;
            var previousOpacity = 0d;
            void SampleCover()
            {
                if (blended || !ReferenceEquals(expandedArtwork.Source, arrivingSource) ||
                    !ReferenceEquals(outgoingExpandedArtwork.Source, shared) || expandedArtworkFade.ContentOpacity is not (> .35 and < .65)) return;
                blended = true;
                previousOpacity = expandedArtworkFade.ContentOpacity;
                if (!expandedGlassBackdrop.IsBlending || !expandedGlassBackdrop.IsCurrent)
                    throw new InvalidOperationException("The artwork started to fade before both glass samples were ready.");
                duringGlass = ReadArtworkLens(lensBounds, lens);
                CaptureUiPreview("artwork-crossfade");
            }
            Window.FrameRendered += SampleCover;
            try
            {
                arrivingReply.SetResult(arrivingSource);
                await WaitForLikedLayoutAsync(() => ReferenceEquals(expandedArtwork.Source, arrivingSource) &&
                    expandedArtworkFade.ContentOpacity == 0 && !expandedArtwork.IsMeasureDirty && !expandedArtwork.IsArrangeDirty);
            }
            finally { Window.FrameRendered -= SampleCover; }
            if (!blended || outgoingExpandedArtwork.Source != null)
                throw new InvalidOperationException("The new artwork skipped intermediate blend frames or retained the previous cover.");
            await WaitForLoginFrameAsync();
            var afterGlass = ReadArtworkLens(lensBounds, lens);
            var expectedGlass = beforeGlass.Lerp(afterGlass, 1 - previousOpacity);
            if (Math.Abs(beforeGlass.R - afterGlass.R) < 30 ||
                Math.Abs(duringGlass.R - expectedGlass.R) > 4 || Math.Abs(duringGlass.G - expectedGlass.G) > 4 ||
                Math.Abs(duringGlass.B - expectedGlass.B) > 4 || expandedGlassBackdrop.IsBlending)
                throw new InvalidOperationException($"Glass pixels did not follow the artwork fade: {beforeGlass} → {duringGlass} → {afterGlass}, expected {expectedGlass}.");
            VerifyExpandedArtworkPixels(); CaptureUiPreview("artwork-arrives-while-open");
            await VerifyQueueArtworkRevealAsync(arriving);

            // Rapid changes finish the current blend and discard stale completions.
            const string delayedUrl = "https://i1.sndcdn.com/player-artwork-delayed-large.jpg";
            var reply = new TaskCompletionSource<ImageSource?>();
            artworkRequests[delayedUrl] = reply.Task;
            var oldGeneration = playGeneration;
            var delayed = LoadArtworkAsync(track with { Id = 79003, ArtworkUrl = delayedUrl }, oldGeneration);
            SetPlayerArtwork(shared, oldGeneration);
            await WaitForLikedLayoutAsync(() => ReferenceEquals(expandedArtwork.Source, shared) && expandedArtworkFade.ContentOpacity > 0);
            playGeneration++;
            var latest = ImageSource.FromBytes(ArtworkFixtureBitmap(300));
            SetPlayerArtwork(latest, playGeneration);
            if (!ReferenceEquals(expandedArtwork.Source, shared) || !ReferenceEquals(outgoingExpandedArtwork.Source, arrivingSource))
                throw new InvalidOperationException("A rapid cover change interrupted the current blend.");
            reply.SetResult(shared); await delayed;
            await WaitForLikedLayoutAsync(() => ReferenceEquals(expandedArtwork.Source, latest) && expandedArtworkFade.ContentOpacity == 0);
            if (!ReferenceEquals(artwork.Source, latest) || !ReferenceEquals(expandedArtwork.Source, latest))
                throw new InvalidOperationException("A stale cover replaced the selected track.");
            await VerifyPlayerColorsAsync();
            CaptureUiPreview("artwork-missing-fullscreen");
            Console.WriteLine("ARTWORK_UI_OK: full-size shared sources, sharp fullscreen textures, delayed and rapid cover crossfades, queue thumbnail reveal, thumbnail-cache upgrades and stale completion rejection");
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("ARTWORK_UI_FAILED: " + error); }
        finally { player = original; Window.Close(); }
    }

    private async Task VerifyMainPlaybackGlassAsync()
    {
        await WaitForLikedLayoutAsync(() => playerPlayButton.ActualWidth == 34 && playerChrome.Opacity == 1 && !playerBackdrop.Running);
        var bounds = playerPlayButton.Bounds;
        var palette = playerBackdrop.Target;
        var red = await SampleAsync(Color.FromRgb(73, 22, 28));
        var blue = await SampleAsync(Color.FromRgb(22, 33, 73));
        if (red.R - red.B < 20 || blue.B - blue.R < 20 || playerPlayButton.Bounds != bounds)
            throw new InvalidOperationException($"Main play glass did not sample the player gradient in place: {red}, {blue}.");
        playerBackdrop.SetPalette(palette);

        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive && isPlaying.Value);
        RouteWaveformClick(bounds.Center);
        await WaitForLikedLayoutAsync(() => !isPlaying.Value);
        RouteWaveformClick(bounds.Center);
        await WaitForLikedLayoutAsync(() => isPlaying.Value);
        CaptureUiPreview("player-play-glass");
        Console.WriteLine("PLAYER_PLAY_GLASS_OK: gradient follows the player palette; play/pause still works in place");

        async Task<Color> SampleAsync(Color color)
        {
            playerBackdrop.SetPalette(new(color, color, color));
            await WaitForLikedLayoutAsync(() => !playerBackdrop.Running);
            var pixels = CaptureSectionButton(playerPlayButton);
            var scale = Window.DpiScale;
            var width = (int)Math.Ceiling(bounds.Width * scale);
            // Read the glass to the left of the centered play/pause glyph.
            var i = ((int)(17 * scale) * width + (int)(6 * scale)) * 4;
            return Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
        }
    }

    private Color ReadArtworkLens(Rect bounds, LiquidGlassLens lens)
    {
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(48, 48, 1));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.Translate(-bounds.X, -bounds.Y);
        expandedGlassBackdrop.Draw(context, bounds, new CornerRadius(24), lens: lens);
        context.EndFrame();
        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var i = 24 * cpu.StrideBytes + 24 * 4;
        return Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private async Task VerifyQueueArtworkRevealAsync(SoundCloudTrack playing)
    {
        const string queuedUrl = "https://i1.sndcdn.com/queue-artwork-arriving-large.jpg";
        var queued = playing with { Id = 79005, ArtworkUrl = queuedUrl };
        var thumbnail = ImageSource.FromBytes(ArtworkFixtureBitmap(CompactArtworkSide(CompactCoverSize)));
        var reply = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        compactArtworkRequests[(queuedUrl, CompactArtworkSide(CompactCoverSize))] = reply.Task;
        playbackQueue.Enqueue(queued, false);
        ToggleExpandedPanel(PlayerPanel.Queue);
        CompactTrackRow? row = null;
        await WaitForLikedLayoutAsync(() =>
        {
            row = expandedQueueRows.Values.FirstOrDefault(item => item.Track?.Id == queued.Id);
            return !panelAnimation.IsRunning && row != null && ArtworkSkeleton(row.Cover).Running;
        });
        var cover = row!.Cover;
        var skeleton = ArtworkSkeleton(cover);
        var blended = false;
        void Sample() => blended |= ReferenceEquals(cover.Source, thumbnail) &&
            skeleton.IsVisible && !skeleton.Running && skeleton.Opacity is > 0 and < 1;
        Window.FrameRendered += Sample;
        try
        {
            reply.SetResult(thumbnail);
            await WaitForLikedLayoutAsync(() => ReferenceEquals(cover.Source, thumbnail) && !skeleton.IsVisible);
        }
        finally { Window.FrameRendered -= Sample; }
        if (!blended || skeleton.Opacity != 1)
            throw new InvalidOperationException("A delayed queue thumbnail skipped its reveal or left the recycled skeleton translucent.");
        ToggleExpandedPanel(PlayerPanel.Queue);
        await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning);
    }

    private void VerifyExpandedArtworkPixels()
    {
        // Inspect the actual native resource before a CPU preview can realize its own image.
        var native = typeof(Image).GetField("_cachedImage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(expandedArtwork) as IImage;
        if (native == null || native.PixelWidth < 500 || native.PixelHeight < 500)
            throw new InvalidOperationException($"Fullscreen retained a thumbnail texture: {native?.PixelWidth}x{native?.PixelHeight}.");
    }

    private static byte[] ArtworkFixtureBitmap(int size)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var stride = (size * 3 + 3) & ~3;
        writer.Write((ushort)0x4d42); writer.Write(54 + stride * size); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(size); writer.Write(size); writer.Write((ushort)1); writer.Write((ushort)24);
        writer.Write(0); writer.Write(stride * size); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
        var row = new byte[stride];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var high = ((x / 3 + y / 3) & 1) == 0;
                row[x * 3] = (byte)(high ? 220 : 20); row[x * 3 + 1] = (byte)(high ? 190 : 30); row[x * 3 + 2] = (byte)(high ? 250 : 50);
            }
            writer.Write(row);
        }
        return stream.ToArray();
    }
}
