using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyCompactArtworkBudgetAsync()
    {
        const string fullUrl = "https://i1.sndcdn.com/compact-budget-t500x500.jpg";
        const string smallUrl = "https://i1.sndcdn.com/compact-budget-large.jpg";
        var smallBytes = ArtworkFixtureBitmap(100);
        await imageDiskCache.WriteAsync(smallUrl, smallBytes, lifetime.Token);
        await imageDiskCache.WriteAsync(fullUrl, ArtworkFixtureBitmap(500), lifetime.Token);
        var track = new SoundCloudTrack { Id = 78003, Title = "Compact artwork", ArtworkUrl = fullUrl };
        var rows = new[] { CreateCompactTrackRow(), CreateCompactTrackRow(dense: true), CreateCompactTrackRow(miniature: true) };
        try
        {
            foreach (var row in rows) BindCompactTrackRow(row, track);
            var full = await GetLibraryArtworkAsync(track);
            var sides = rows.Select(row => CompactArtworkSide(row.Cover.Width)).ToArray();
            await WaitForLikedLayoutAsync(() => rows.All(row => row.Cover.Source is ImageSource));
            for (var i = 0; i < rows.Length; i++)
            {
                var source = (ImageSource)rows[i].Cover.Source!;
                var side = sides[i];
                if (source.PixelWidth != side || source.PixelHeight != side ||
                    !ReferenceEquals(source, await CompactArtworkAsync(fullUrl, side)))
                    throw new InvalidOperationException("Compact row used an oversized cover or failed to share a same-size request.");
            }
            var expected = Storage.ArtworkImage.Prepare(smallBytes, sides[0]);
            var actualPixels = new byte[sides[0] * sides[0] * 4];
            var expectedPixels = new byte[actualPixels.Length];
            ((ImageSource)rows[0].Cover.Source!).CopyPixels(actualPixels, sides[0] * 4);
            expected.CopyPixels(expectedPixels, sides[0] * 4);
            if (full is not { PixelWidth: 500, PixelHeight: 500 } || !actualPixels.AsSpan().SequenceEqual(expectedPixels) ||
                await CompactArtworkAsync(fullUrl, 132) is not { PixelWidth: 132, PixelHeight: 132 })
                throw new InvalidOperationException("Compact artwork selected the wrong CDN size or altered the full cover.");

            const string cachedFullUrl = "https://i1.sndcdn.com/compact-offline-t500x500.jpg";
            await imageDiskCache.WriteAsync(cachedFullUrl, ArtworkFixtureBitmap(500), lifetime.Token);
            if (await CompactArtworkAsync("https://i1.sndcdn.com/compact-offline-large.jpg", 44) is not { PixelWidth: 44, PixelHeight: 44 })
                throw new InvalidOperationException("Compact artwork missed an existing full-size disk cache entry.");

            const string pendingUrl = "https://i1.sndcdn.com/compact-pending-large.jpg";
            var pending = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            compactArtworkRequests[(pendingUrl, sides[0])] = pending.Task;
            var reused = rows[0];
            BindCompactTrackRow(reused, track with { Id = 78004, ArtworkUrl = pendingUrl });
            BindCompactTrackRow(reused, null);
            pending.SetResult(expected);
            await WaitForLoginFrameAsync();
            if (reused.Cover.Source != null)
                throw new InvalidOperationException("A recycled compact row published a late thumbnail.");
            Console.WriteLine($"COMPACT_ARTWORK_BUDGET_OK: full=500, list/dense/miniature={string.Join('/', sides)}, high-DPI=132, shared per-size cache and smaller CDN variant");
        }
        finally
        {
            foreach (var row in rows)
            {
                BindCompactTrackRow(row, null);
                compactTrackRows.Remove(row);
                row.Root.Dispose();
            }
        }
    }

    private async Task VerifyHistoryArtworkAsync(SoundCloudTrack[] fixtures)
    {
        var track = fixtures[1];
        var pixels = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var offset = (y * 64 + x) * 4;
                pixels[offset] = (byte)(x < 32 ? 160 : 70);
                pixels[offset + 1] = (byte)(y < 32 ? 110 : 50);
                pixels[offset + 2] = (byte)(x < 32 ? 70 : 180);
                pixels[offset + 3] = 255;
            }
        var source = ImageSource.FromBgraPixels(64, 64, pixels);
        libraryCoverCache[track.Id] = source;
        ShowLibraryTracks();
        await WaitForLikedLayoutAsync(() => likedTiles.Values.Any(tile => tile.Track?.Id == track.Id && tile.Cover.ActualWidth > 100));
        var likes = likedTiles.Values.Single(tile => tile.Track?.Id == track.Id);
        await LoadLikedArtworkAsync(likes.Cover, track);
        if (!ReferenceEquals(likes.Cover.Source, source))
            throw new InvalidOperationException("Likes did not bind their cached cover");

        await ShowLibrarySectionAsync(Page.LibraryHistory);
        await WaitForLikedLayoutAsync(() => collectionCards.Values.Any(card => collectionGrid.IsAncestorOf(card.Frame) &&
            card.Item.Track?.Id == track.Id && card.Cover.ActualWidth > 100));
        var history = collectionCards.Values.Single(card => collectionGrid.IsAncestorOf(card.Frame) && card.Item.Track?.Id == track.Id);
        if (history.Item.ArtworkUrl != null || history.Item.Track!.ArtworkUrl != null ||
            !ReferenceEquals(history.Cover.Source, likes.Cover.Source) || ArtworkSkeleton(history.Cover).IsVisible)
            throw new InvalidOperationException("Sparse history metadata lost the same track's cached likes cover");
        await Task.Delay(300, lifetime.Token);
        history = collectionCards.Values.Single(card => collectionGrid.IsAncestorOf(card.Frame) && card.Item.Track?.Id == track.Id);
        if (!ReferenceEquals(history.Cover.Source, source))
            throw new InvalidOperationException($"History artwork completion replaced the cached cover: cached={ReferenceEquals(libraryCoverCache.GetValueOrDefault(track.Id), source)}, current={current?.Id}, url={CollectionArtworkUrl(history.Item)}");
        if (current?.Id == track.Id && (history.Playback.ShowsPause != isPlaying.Value || history.Playback.Opacity < .999))
            throw new InvalidOperationException("History artwork completion hid the active playback overlay");
        CaptureUiPreview("history-cached-artwork");

        const string avatarUrl = "https://i1.sndcdn.com/history-avatar-large.jpg";
        artworkRequests[avatarUrl] = Task.FromResult<ImageSource?>(source);
        var avatarTrack = track with { Id = 78001, ArtworkUrl = null, User = track.User! with { AvatarUrl = avatarUrl } };
        if (!ReferenceEquals(await GetCollectionArtworkAsync(LibraryItem.FromTrack(avatarTrack)), source))
            throw new InvalidOperationException("History did not use its author's avatar fallback");

        const string coverUrl = "https://i1.sndcdn.com/history-nested-large.jpg";
        var gate = new TaskCompletionSource<ImageSource?>();
        artworkRequests[coverUrl] = gate.Task;
        var nested = LibraryItem.FromTrack(track with { Id = 78002, ArtworkUrl = coverUrl }) with { ArtworkUrl = null };
        var cover = new Image(); _ = ArtworkLayer(cover);
        SetCollectionArtwork(cover, nested);
        var request = GetCollectionArtworkAsync(nested);
        if (request.IsCompleted || cover.Source != null || !ArtworkSkeleton(cover).IsVisible)
            throw new InvalidOperationException("History did not retain a skeleton while nested track artwork was pending");
        gate.SetResult(source);
        SetCollectionArtwork(cover, nested, await request, finished: true);
        if (!ReferenceEquals(cover.Source, source) || ArtworkSkeleton(cover).IsVisible ||
            !ReferenceEquals(libraryCoverCache[nested.Track!.Id], source))
            throw new InvalidOperationException("History did not resolve nested artwork directly into the shared track cache");
        artworkLoading.Remove(cover);
        Console.WriteLine("UI_HISTORY_ARTWORK_OK: sparse history reuses the likes cover by track ID, keeps its active overlay, resolves author avatars and nested track artwork, and replaces shimmer directly with the cover");
    }
}
