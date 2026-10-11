using Pookie.App.Home;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Task NavigateHomeAsync(bool force = false) => NavigateRouteAsync(new(Page.Home), _ => LoadHomeAsync(force));

    private async Task LoadHomeAsync(bool force = false)
    {
        heading.Value = "Главная"; eyebrow.Value = ""; status.Value = "";
        if (demo)
        {
            homeContent ??= new(tracks.Take(6).Select(LibraryItem.FromTrack).ToArray(), [], [], [], []);
            RenderHome(); return;
        }
        if (api.Session is not { } session || me is not { } user) { ClearHome(); return; }
        if (homeSession != session) ClearHome();
        homeSession = session;
        if (!force && homeContent != null && DateTimeOffset.UtcNow - homeLoadedAt < TimeSpan.FromMinutes(10)) { RefreshCachedHome(); return; }
        var token = loading!.Token; var generation = navigationGeneration;
        var previous = homeContent;
        bool Current() => !disposed && !token.IsCancellationRequested && generation == navigationGeneration && api.Session == session && page.Value == Page.Home;
        homePending.UnionWith(["recent", "recommendations", "releases", "mixes"]); homeErrors.Clear(); RenderHome();
        LibraryItem[] recent = [], history = [], mixes = [];
        SoundCloudTrack[] likes = [], seeds = [], feed = [];
        TrackPage[] related = [];
        HashSet<long> following = [];
        HashSet<long> knownLikes = [];
        void Update(string section)
        {
            if (!Current()) return;
            homePending.Remove(section);
            ImportWaveHomeData(likes, knownLikes, history.Concat(recent).ToArray(), related.SelectMany(result => result.Tracks).Concat(feed));
            for (var index = 0; index < Math.Min(seeds.Length, related.Length); index++) waveTaste.Relate(seeds[index], related[index].Tracks);
            var content = HomeRecommendations.Build(localRecent.Concat(recent).ToArray(), history, likes, seeds, related, feed, following, mixes, DateTimeOffset.UtcNow, knownLikes);
            homeContent = previous == null ? content : content with
            {
                Recent = homePending.Contains("recent") ? previous.Recent : content.Recent,
                Recommendations = homePending.Contains("recommendations") ? previous.Recommendations : content.Recommendations,
                Releases = homePending.Contains("releases") ? previous.Releases : content.Releases,
                Mixes = homePending.Contains("mixes") ? previous.Mixes : content.Mixes,
                Seeds = homePending.Contains("recommendations") ? previous.Seeds : content.Seeds
            };
            RenderHome();
        }
        async Task<T> Read<T>(string section, Func<Task<T>> fetch, T fallback)
        {
            try { return await fetch(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (SoundCloudException error) when (error.StatusCode == 401) { throw; }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException or OperationCanceledException or NotSupportedException)
            {
                if (Current()) homeErrors.Add(section);
                Pookie.Logging.AppLog.For("Pookie.Home").Debug(error, "Не удалось загрузить секцию {Section}", section);
                return fallback;
            }
        }
        var recentTask = Read("recent", () => api.GetLibraryAsync("recent", user.Id, token), new LibraryPage([], null));
        var historyTask = Read("recommendations", () => api.GetLibraryAsync("history", user.Id, token), new LibraryPage([], null));
        var likesTask = Read("recommendations", () => libraryLikes != null && !force ? Task.FromResult(libraryLikes) : api.GetLikesAsync(user.Id, token), new TrackPage([], null));
        var knownLikesTask = Read("recommendations", () => likedIdsReady && !force ? Task.FromResult(new HashSet<long>(likedIds)) : api.GetLikedIdsAsync(token), new HashSet<long>());
        async Task Recent()
        {
            recent = (await recentTask).Items; history = (await historyTask).Items;
            Update("recent");
        }
        async Task Recommendations()
        {
            likes = (await likesTask).Tracks; history = (await historyTask).Items;
            if (Current()) { EnsureWaveTaste(); waveLikesCursor = (await likesTask).NextHref; }
            knownLikes = await knownLikesTask;
            if (Current()) { EnsureWaveTaste(); homeLikesCount = homeErrors.Contains("recommendations") ? null : knownLikes.Count;
                waveTaste.SyncLikes(likedIdsReady ? likedIds : knownLikes); }
            seeds = HomeRecommendations.Seeds(likes, history);
            related = await Task.WhenAll(seeds.Select(track => Read("recommendations", () => api.GetRelatedTracksAsync(track.Id, token), new TrackPage([], null))));
            Update("recommendations");
        }
        async Task Releases()
        {
            var feedTask = Read("releases", () => api.GetFeedAsync(token), new TrackPage([], null));
            var followingTask = Read("releases", () => api.GetFollowingIdsAsync(user.Id, token), new HashSet<long>());
            await Task.WhenAll(feedTask, followingTask);
            feed = (await feedTask).Tracks; following = await followingTask; Update("releases");
        }
        async Task Mixes()
        {
            mixes = await Read("mixes", () => api.GetPersonalMixesAsync(user.Id, token), Array.Empty<LibraryItem>());
            Update("mixes");
        }
        await Task.WhenAll(Recent(), Recommendations(), Releases(), Mixes());
        if (!Current()) return;
        homeLoadedAt = homeErrors.Count == 0 ? DateTimeOffset.UtcNow : default;
        homePending.Clear(); RenderHome();
    }
}
