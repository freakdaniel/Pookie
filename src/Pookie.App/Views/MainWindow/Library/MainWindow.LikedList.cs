using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<Grid, LikedTrackRow> likedRows = [];
    private readonly Dictionary<string, Task<float[]?>> waveformRequests = [];
    private readonly SemaphoreSlim waveformGate = new(3);
    private double likedPlaybackPosition;

    private ItemsControl CreateLikedList()
    {
        var result = new ItemsControl().Items(Array.Empty<SoundCloudTrack>(), track => track.Title)
            .ItemHeight(196).FixedHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .ItemPadding(new Thickness(0, 0, 18, 36));
        result.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var row = CreateLibraryTrackRow();
            context.Register("row", row.Root);
            return row.Root;
        }, (_, track, _, context) => BindLibraryTrackRow(likedRows[context.Get<Grid>("row")], track),
            (_, _, _, context) => ClearLibraryTrackRow(likedRows[context.Get<Grid>("row")]));
        return result;
    }

    private LikedTrackRow CreateLibraryTrackRow()
    {
        var row = new LikedTrackRow(SelectLibraryTrack, track => Run(() => ToggleTrackLikeAsync(track)),
            CopyTrackLink, (track, fraction) => Run(() => SeekLikedTrackAsync(track, fraction)), ArtworkLayer);
        likedRows.Add(row.Root, row);
        AttachTrackQueueMenu(row.Root, () => row.Track);
        AttachTrackTitle(row.Title, () => row.Track);
        return row;
    }

    private void BindLibraryTrackRow(LikedTrackRow row, SoundCloudTrack track)
    {
        row.Bind(track);
        SetCardArtwork(row.Cover, libraryCoverCache.GetValueOrDefault(track.Id), track.ArtworkUrl ?? track.User?.AvatarUrl);
        likedArtworkTracks[row.Cover] = track.Id;
        Run(() => LoadLikedArtworkAsync(row.Cover, track));
        Run(() => LoadLikedWaveformAsync(row, track));
        RefreshLikedRow(row);
    }

    private void ClearLibraryTrackRow(LikedTrackRow row)
    {
        likedArtworkTracks.Remove(row.Cover);
        StopCardArtwork(row.Cover);
        row.Reset();
    }

    private void RefreshLikedRow(LikedTrackRow row)
    {
        if (row.Track is not { } track) return;
        var selected = current?.Id == track.Id;
        row.Refresh(selected, isPlaying.Value, likedIds.Contains(track.Id) || !likedIdsReady && tracks.Any(t => t.Id == track.Id) &&
            page.Value is Page.Library or Page.LibraryTracks, me != null && !likeBusy && !demo,
            selected ? likedPlaybackPosition : 0);
    }
    private void RefreshLikedRows(double? seconds = null)
    {
        if (seconds is { } value) likedPlaybackPosition = value;
        foreach (var row in likedRows.Values) RefreshLikedRow(row);
        RefreshSearchPlayback();
        RefreshTrackDetailPlayback();
    }

    private async Task SeekLikedTrackAsync(SoundCloudTrack track, double fraction)
    {
        if (current?.Id != track.Id)
        {
            SetQueue(track);
            await PlayAsync(track);
        }
        if (current?.Id != track.Id || !audioReady || disposed) return;
        progress.Value = Math.Clamp(fraction, 0, 1) * progress.Maximum;
        FlushSeek();
    }

    private void CopyTrackLink(SoundCloudTrack track)
    {
        if (!Uri.TryCreate(track.PermalinkUrl, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.Host is not ("soundcloud.com" or "www.soundcloud.com") || url.UserInfo != "") return;
        var clipboard = nativeClipboard ?? Application.Current.PlatformServices.Clipboard;
        status.Value = clipboard?.TrySetText(url.AbsoluteUri) == true ? "Ссылка на трек скопирована." : "Не удалось скопировать ссылку.";
    }

    private async Task LoadLikedWaveformAsync(LikedTrackRow row, SoundCloudTrack track)
    {
        var samples = await GetTrackWaveformAsync(track);
        if (samples != null && !disposed && ReferenceEquals(row.Track, track)) row.Waveform.SetSamples(samples);
    }

    private async Task<float[]?> GetTrackWaveformAsync(SoundCloudTrack track)
    {
        if (!Uri.TryCreate(track.WaveformUrl, UriKind.Absolute, out var uri) || !SoundCloudWebClient.IsMediaUri(uri)) return null;
        if (uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            uri = new UriBuilder(uri) { Path = uri.AbsolutePath[..^4] + ".json" }.Uri;
        if (!uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
        if (!waveformRequests.TryGetValue(uri.AbsoluteUri, out var request))
        {
            if (waveformRequests.Count >= 256)
            {
                var expired = waveformRequests.FirstOrDefault(entry => entry.Value.IsCompleted);
                if (expired.Key != null) waveformRequests.Remove(expired.Key);
            }
            waveformRequests[uri.AbsoluteUri] = request = FetchWaveformAsync(uri);
        }
        return await request;
    }

    private Task<float[]?> FetchWaveformAsync(Uri uri) => Task.Run(() => FetchWaveformCoreAsync(uri), lifetime.Token);

    private async Task<float[]?> FetchWaveformCoreAsync(Uri uri)
    {
        await waveformGate.WaitAsync(lifetime.Token);
        try
        {
            var bytes = await new Pookie.App.Playback.WaveformGainResolver(http, dataPaths).ReadAsync(uri, lifetime.Token);
            if (bytes == null) return null;
            using var json = JsonDocument.Parse(bytes);
            var samples = WaveformData.Parse(json.RootElement);
            return samples;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException) { return null; }
        finally { waveformGate.Release(); }
    }
}
