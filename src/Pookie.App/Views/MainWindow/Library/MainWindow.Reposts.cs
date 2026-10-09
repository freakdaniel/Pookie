using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly HashSet<long> repostedIds = [];
    private readonly HashSet<(WebSession Session, long Track)> pendingReposts = [];
    private CancellationTokenSource? repostLoading;
    private WebSession? repostSession;
    private Task? repostTask;
    private bool repostedIdsReady;

    private bool CanRepostTrack(SoundCloudTrack track) => !demo && me != null && api.Session != null && track.Id > 0 &&
        track.User?.Id != me.Id && !pendingReposts.Contains((api.Session, track.Id));
    private bool IsRepostPending(SoundCloudTrack track) => api.Session is { } session && pendingReposts.Contains((session, track.Id));

    private void ResetReposts()
    {
        repostLoading?.Cancel(); repostLoading?.Dispose(); repostLoading = null;
        repostTask = null; repostSession = null; repostedIdsReady = false; repostedIds.Clear();
    }

    private Task LoadRepostedIdsAsync()
    {
        if (demo || api.Session is not { } session) return Task.CompletedTask;
        if (repostSession == session && (repostedIdsReady || repostTask is { IsCompleted: false })) return repostTask ?? Task.CompletedTask;
        ResetReposts(); repostSession = session;
        repostLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        return repostTask = ReadRepostedIdsAsync(session, repostLoading.Token);
    }

    private async Task ReadRepostedIdsAsync(WebSession session, CancellationToken token)
    {
        if (api.BrowserTransport == null) AttachBrowser(new NativeBrowserSession(session));
        var ids = await api.GetRepostedIdsAsync(token);
        token.ThrowIfCancellationRequested();
        if (disposed || api.Session != session || repostSession != session) return;
        repostedIds.UnionWith(ids); repostedIdsReady = true; RefreshRepostState();
    }

    private void RefreshRepostState()
    {
        foreach (var row in likedRows.Values)
            if (row.Track is { } track) row.RefreshRepost(repostedIds.Contains(track.Id), CanRepostTrack(track), IsRepostPending(track));
        if (trackDetailState is not { } state) return;
        var reposted = repostedIds.Contains(state.Track.Id);
        TrackButtons.SetReposted(trackDetailRepost, reposted, TrackButtons.Glass);
        TrackButtons.SetGlyph(trackDetailRepostIcon, "repeat", reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface);
        TrackButtons.SetAvailability(trackDetailRepost, CanRepostTrack(state.Track), IsRepostPending(state.Track));
        trackDetailRepost.ToolTip(me != null && state.Track.User?.Id == me.Id ? "Это твой трек" : reposted ? "Убрать репост" : "Сделать репост");
    }

    private async Task ToggleTrackRepostAsync(SoundCloudTrack track)
    {
        if (!CanRepostTrack(track) || api.Session is not { } session || !pendingReposts.Add((session, track.Id))) return;
        RefreshRepostState();
        try
        {
            await LoadRepostedIdsAsync();
            if (disposed || api.Session != session) return;
            var reposted = !repostedIds.Contains(track.Id);
            await api.SetRepostedAsync(track.Id, reposted, repostLoading?.Token ?? lifetime.Token);
            if (disposed || api.Session != session) return;
            if (reposted) repostedIds.Add(track.Id); else repostedIds.Remove(track.Id);
            if (trackDetailState is { } detail && detail.Track.Id == track.Id)
            {
                var sections = new Dictionary<TrackSection, LibraryPage>(detail.Sections);
                sections.Remove(TrackSection.Reposts);
                trackDetailState = detail with { Track = detail.Track with {
                    RepostsCount = Math.Max(0, (detail.Track.RepostsCount ?? 0) + (reposted ? 1 : -1))
                }, Sections = sections };
                RenderTrackDetailHeader();
                if (trackDetailSection.Value == TrackSection.Reposts)
                    Run(() => LoadTrackSectionAsync(TrackSection.Reposts, navigationGeneration, loading!.Token));
            }
            status.Value = reposted ? "Трек появился в твоём профиле." : "Репост убран.";
        }
        finally { pendingReposts.Remove((session, track.Id)); if (!disposed) RefreshRepostState(); }
    }
}
