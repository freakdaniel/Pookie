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
            .ItemHeight(TrackRowLayout.Stride).FixedHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .ItemPadding(new Thickness(0, 0, 18, TrackRowLayout.Gap));
        result.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var row = CreateLibraryTrackRow();
            context.Register("row", row.Root);
            return CreateLikedItemMotion(row.Root, result, context);
        }, (_, track, _, context) =>
        {
            BindLikedItemMotion(context, track.Id);
            BindLibraryTrackRow(likedRows[context.Get<Grid>("row")], track);
        },
            (_, _, _, context) =>
            {
                ClearLikedItemMotion(context);
                ClearLibraryTrackRow(likedRows[context.Get<Grid>("row")]);
            });
        return result;
    }

    private LikedTrackRow CreateLibraryTrackRow()
    {
        var row = new LikedTrackRow(SelectLibraryTrack, track => Run(() => ToggleTrackLikeAsync(track)),
            CopyTrackLink, track => Run(() => ToggleTrackRepostAsync(track)), track => Run(() => OpenPlaylistPickerAsync(track)), (track, fraction) => Run(() => SeekLikedTrackAsync(track, fraction)), ArtworkLayer);
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
        row.RefreshRepost(repostedIds.Contains(track.Id), CanRepostTrack(track), IsRepostPending(track));
        var selected = current?.Id == track.Id;
        row.Refresh(selected, isPlaying.Value, likedIds.Contains(track.Id) || !likedIdsReady && tracks.Any(t => t.Id == track.Id) &&
            page.Value is Page.Library or Page.LibraryTracks, CanLikeTrack(track),
            selected ? likedPlaybackPosition : 0, IsLikePending(track));
    }
    private void RefreshLikedRows(double? seconds = null)
    {
        if (seconds is { } value) likedPlaybackPosition = value;
        foreach (var row in likedRows.Values) RefreshLikedRow(row);
        RefreshSearchPlayback();
        RefreshTrackDetailPlayback(); RefreshCollectionPlayback();
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
        if (TrackLink(track.PermalinkUrl) is { } url) { CopyText(url.AbsoluteUri); return; }
        Run(async () =>
        {
            if (track.Id <= 0) { status.Value = "У этого трека нет ссылки."; return; }
            var resolved = await api.GetTrackAsync(track.Id, lifetime.Token);
            if (TrackLink(resolved.PermalinkUrl) is { } link) CopyText(link.AbsoluteUri);
            else status.Value = "Не удалось получить ссылку на трек.";
        });
    }

    private static Uri? TrackLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme is "https" or "http" && url.Host is "soundcloud.com" or "www.soundcloud.com" && url.UserInfo == ""
            ? new UriBuilder(url) { Scheme = "https", Port = -1 }.Uri : null;

    private void CopyText(string text)
    {
        var clipboard = (Aprillz.MewUI.Platform.IClipboardService?)windowClipboard ?? nativeClipboard ?? Application.Current.PlatformServices.Clipboard;
        status.Value = clipboard?.TrySetText(text) == true ? "Скопировано." : "Не удалось скопировать.";
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
