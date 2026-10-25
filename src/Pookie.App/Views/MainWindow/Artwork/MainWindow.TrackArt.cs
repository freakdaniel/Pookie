using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Storage.AsyncResourceCache<string, ImageSource> trackArtRequests = new(128, ArtworkCacheBudget, ArtworkBytes);

    private Task<ImageSource?> GetTrackArtAsync(SoundCloudTrack track)
    {
        if (!Uri.TryCreate(track.TrackArtUrl, UriKind.Absolute, out var uri) || !SoundCloudWebClient.IsMediaUri(uri))
            return Task.FromResult<ImageSource?>(null);
        return trackArtRequests.GetAsync(uri.AbsoluteUri, () => FetchTrackArtAsync(uri.AbsoluteUri), lifetime.Token);
    }

    private async Task<ImageSource?> FetchTrackArtAsync(string url)
    {
        await coverGate.WaitAsync(lifetime.Token);
        try { return await FetchImageAsync(url, lifetime.Token); }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return null; }
        finally { coverGate.Release(); }
    }

    private async Task LoadLikedTrackArtAsync(LikedTrackRow row, SoundCloudTrack track)
    {
        var source = await GetTrackArtAsync(track);
        if (!disposed && ReferenceEquals(row.Track, track)) row.SetTrackArt(source);
    }

    private bool IsTrackDetailArtworkCurrent(SoundCloudTrack track, long generation) => IsTrackDetailCurrent(track.Id, generation) &&
        trackDetailState!.Track.TrackArtUrl == track.TrackArtUrl && trackDetailState.Track.ArtworkUrl == track.ArtworkUrl;

    private async Task LoadTrackDetailBackdropAsync(SoundCloudTrack track, long generation, Task<ImageSource?> cover)
    {
        var art = GetTrackArtAsync(track);
        var source = await cover;
        if (!IsTrackDetailArtworkCurrent(track, generation)) return;
        // Show the normal background while a custom image is still downloading.
        // Metadata can acquire Track Art after the initial sparse track response.
        var availableArt = art.IsCompletedSuccessfully ? art.Result : null;
        var backdrop = availableArt ?? (source == null ? null :
            await Task.Run(() => ArtworkBackdrop.Create(source), lifetime.Token));
        if (!IsTrackDetailArtworkCurrent(track, generation)) return;
        SetTrackDetailBackdrop(backdrop, availableArt != null);
        var custom = await art;
        if (custom != null && IsTrackDetailArtworkCurrent(track, generation)) SetTrackDetailBackdrop(custom, customArt: true);
    }

    private void SetTrackDetailBackdrop(ImageSource? source, bool customArt)
    {
        trackDetailBackdrop.Source = source;
        trackDetailScrim.Background = customArt ? ArtworkBackdrop.TrackArtScrim : ArtworkBackdrop.CardScrim;
        trackDetailWaveform.OnArtwork = customArt;
        trackDetailGlass?.RefreshArtwork();
    }
}
