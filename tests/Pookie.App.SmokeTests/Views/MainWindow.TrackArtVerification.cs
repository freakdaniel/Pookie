using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private static ImageSource TrackArtFixture()
    {
        const int width = 1240, height = 400;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i] = (byte)(x % 160 < 80 ? 40 : 200);
            pixels[i + 1] = (byte)(y % 100 < 50 ? 50 : 170);
            pixels[i + 2] = (byte)(x % 160 < 80 ? 235 : 60); pixels[i + 3] = 255;
        }
        return ImageSource.FromBgraPixels(width, height, pixels, hasAlpha: false);
    }

    private async Task VerifyTrackArtRowAsync(LikedTrackRow row, SoundCloudTrack track, ImageSource source)
    {
        await WaitForLikedLayoutAsync(() => ReferenceEquals(row.TrackArt.Source, source) && row.Waveform.HasSamples && row.TrackArt.ActualWidth > 100);
        var cover = row.Cover.Source; var height = row.Root.ActualHeight;
        var actions = FindLikeAction(row.Root);
        if (!row.Waveform.OnArtwork || row.TrackArt.Bounds.X <= row.Cover.Bounds.Right ||
            Math.Abs(row.TrackArt.Bounds.Y - row.Cover.Bounds.Y) > .5 || Math.Abs(row.TrackArt.Bounds.Bottom - row.Cover.Bounds.Bottom) > .5)
            throw new InvalidOperationException("Track Art does not align with the cover's top and bottom.");
        if (Math.Abs(row.PlayButton.Bounds.Y - row.TrackArt.Bounds.Y - TrackRowLayout.VerticalInset) > .5 ||
            Math.Abs(row.TrackArt.Bounds.Bottom - actions.Bounds.Bottom - TrackRowLayout.VerticalInset) > .5 ||
            Math.Abs(row.Waveform.Bounds.X - row.TrackArt.Bounds.X - TrackRowLayout.ContentInset) > .5 ||
            Math.Abs(row.TrackArt.Bounds.Right - row.Waveform.Bounds.Right - TrackRowLayout.ContentInset) > .5 ||
            Math.Abs(row.Waveform.Bounds.Y + row.Waveform.ActualHeight / 2 - row.Cover.Bounds.Y - row.Cover.ActualHeight / 2) > .5)
            throw new InvalidOperationException("Track controls did not stay centered inside the cover-height background.");
        var point = new Point(row.Waveform.Bounds.X + 40, row.Waveform.Bounds.Y + 20);
        if (((UIElement)Window.Content!).HitTest(point) != row.Waveform)
            throw new InvalidOperationException("Track Art intercepted waveform input.");
        await VerifyCardGlassSourceAsync(row.TrackArtGlass, row.Cover, row.TrackArt, actions);
        CaptureUiPreview("track-art-list");
        const string lateUrl = "https://i1.sndcdn.com/track-art-late-row.jpg";
        var late = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        trackArtRequests[lateUrl] = late.Task;
        var delayedTrack = track with { Visuals = new(true, [new(lateUrl)]) };
        row.Bind(delayedTrack); var pending = LoadLikedTrackArtAsync(row, delayedTrack);
        row.Bind(track with { Visuals = null });
        late.SetResult(source); await pending;
        if (row.TrackArt.Source != null || row.Waveform.OnArtwork)
            throw new InvalidOperationException("A recycled row kept the previous track's custom background.");
        BindLibraryTrackRow(row, track);
        await WaitForLikedLayoutAsync(() => ReferenceEquals(row.TrackArt.Source, source) && !row.Root.IsArrangeDirty);
        if (Math.Abs(height - row.Root.ActualHeight) > .1 || !ReferenceEquals(row.Cover.Source, cover))
            throw new InvalidOperationException("Adding Track Art changed the row height or square cover.");
        if (!ReferenceEquals(GetTrackArtAsync(track), GetTrackArtAsync(track)))
            throw new InvalidOperationException("Custom background requests were not shared.");
        Console.WriteLine("TRACK_ART_ROWS_OK: sharp separate image, unchanged layout, unobstructed waveform, shared loading and recycled-row protection");
    }

    private async Task VerifyTrackArtHeroAsync(ImageSource source)
    {
        await WaitForLikedLayoutAsync(() => ReferenceEquals(trackDetailBackdrop.Source, source));
        var state = trackDetailState!; var cover = trackDetailCover.Source;
        if (!trackDetailWaveform.OnArtwork) throw new InvalidOperationException("Custom hero did not improve waveform contrast.");
        CaptureUiPreview("track-art-hero");
        try
        {
            const string lateUrl = "https://i1.sndcdn.com/track-art-late-hero.jpg";
            var late = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            trackArtRequests[lateUrl] = late.Task;
            var delayedTrack = state.Track with { Visuals = new(true, [new(lateUrl)]) };
            trackDetailState = state with { Track = delayedTrack };
            var pending = LoadTrackDetailArtworkAsync(delayedTrack, navigationGeneration);
            var ordinary = state.Track with { Visuals = null };
            trackDetailState = state with { Track = ordinary };
            await LoadTrackDetailArtworkAsync(ordinary, navigationGeneration);
            var fallback = trackDetailBackdrop.Source;
            if (fallback == null || ReferenceEquals(fallback, source) || trackDetailWaveform.OnArtwork)
                throw new InvalidOperationException("Track without custom artwork lost its normal blurred-cover fallback.");
            late.SetResult(source); await pending;
            if (!ReferenceEquals(trackDetailBackdrop.Source, fallback))
                throw new InvalidOperationException("Stale track metadata replaced the current hero background.");
            const string failedUrl = "https://i1.sndcdn.com/track-art-missing.jpg";
            trackArtRequests[failedUrl] = Task.FromResult<ImageSource?>(null);
            var unavailable = state.Track with { Visuals = new(true, [new(failedUrl)]) };
            trackDetailState = state with { Track = unavailable };
            await LoadTrackDetailArtworkAsync(unavailable, navigationGeneration);
            if (trackDetailBackdrop.Source == null || ReferenceEquals(trackDetailBackdrop.Source, source) ||
                !ReferenceEquals(trackDetailCover.Source, cover) || trackDetailWaveform.OnArtwork)
                throw new InvalidOperationException("Unavailable Track Art did not retain the cover and normal fallback.");
        }
        finally
        {
            trackDetailState = state;
            await LoadTrackDetailArtworkAsync(state.Track, navigationGeneration);
        }
        Console.WriteLine("TRACK_ART_HERO_OK: original image without blur, normal/failed fallback, independent cover, increased waveform contrast and stale metadata protection");
    }
}
