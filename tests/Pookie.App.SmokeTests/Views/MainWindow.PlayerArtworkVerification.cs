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
            coverCache[arriving.Id] = ImageSource.FromBytes(ArtworkFixtureBitmap(100));
            SetQueue(arriving); await PlayAsync(arriving); SetExpandedPlayer(true);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning);
            var arrivingSource = ImageSource.FromBytes(fullBytes);
            arrivingReply.SetResult(arrivingSource);
            await WaitForLikedLayoutAsync(() => ReferenceEquals(expandedArtwork.Source, arrivingSource) &&
                !expandedArtwork.IsMeasureDirty && !expandedArtwork.IsArrangeDirty);
            await WaitForLoginFrameAsync();
            VerifyExpandedArtworkPixels(); CaptureUiPreview("artwork-arrives-while-open");

            // A late cover completion cannot overwrite a more recently selected track.
            const string delayedUrl = "https://i1.sndcdn.com/player-artwork-delayed-large.jpg";
            var reply = new TaskCompletionSource<ImageSource?>();
            artworkRequests[delayedUrl] = reply.Task;
            var oldGeneration = playGeneration;
            var delayed = LoadArtworkAsync(track with { Id = 79003, ArtworkUrl = delayedUrl }, oldGeneration);
            playGeneration++;
            var latest = ImageSource.FromBytes(ArtworkFixtureBitmap(300));
            SetPlayerArtwork(latest, playGeneration);
            reply.SetResult(shared); await delayed;
            if (!ReferenceEquals(artwork.Source, latest) || !ReferenceEquals(expandedArtwork.Source, latest))
                throw new InvalidOperationException("A stale cover replaced the selected track.");
            Console.WriteLine("ARTWORK_UI_OK: thumbnail URL upgrades to 500px, shared player/card source, sharp native texture on first/repeated fullscreen and arriving while open, old thumbnail-cache upgrade, deduplicated lookup and stale completion rejection");
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("ARTWORK_UI_FAILED: " + error); }
        finally { player = original; Window.Close(); }
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
