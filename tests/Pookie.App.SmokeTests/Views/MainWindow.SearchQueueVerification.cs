using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySearchQueueUiAsync()
    {
        var previousPlayer = player;
        timer.Stop(); player = new QueueVerificationPlayer();
        try
        {
            await ShowWorkspaceAsync();
            Window.WindowSize = WindowSize.Resizable(1160, 1000, minWidth: 1000, minHeight: 680);
            api.Session = new("fixture", "fixture-token", "Test");
            var transport = new SearchQueueFixture(); api.BrowserTransport = transport;
            shuffle.Value = false; playbackQueue.SetRepeat(RepeatMode.Off); playbackQueue.SetAutoContinue(false);
            var data = Enumerable.Range(1, 9).Select(id => new SoundCloudTrack
            { Id = id, Title = "Search recording " + id, Duration = 12000, User = new() { Id = 77, Username = id == 4 ? "Lyric match" : "nyan.mp3" } }).ToArray();
            var art = ImageSource.FromBytes(ArtworkFixtureBitmap(184));
            foreach (var track in data) libraryCoverCache[track.Id] = art;
            var user = new SoundCloudUser { Id = 77, Username = "nyan.mp3" };
            LibraryItem[] items = [LibraryItem.FromTrack(data[0]), UserItem(user), LibraryItem.FromTrack(data[1]),
                LibraryItem.FromTrack(data[2]), LibraryItem.FromTrack(data[3]) with { MatchedByLyrics = true }, LibraryItem.FromTrack(data[4])];

            // Real waveform-card click: ignore an unsubmitted edit in the search field.
            await Show(SearchSection.Tracks, "nyan.mp3", items, SearchQueueFixture.Cursor(0));
            query.Value = "unsubmitted text";
            await Click(2, SearchBlockKind.LibraryTrack);
            await WaitForLikedLayoutAsync(() => current?.Id == 2 && audioReady && transport.Calls >= 1);
            AssertSource([1, 2, 3, 4, 5]);
            if (playbackQueue.Context is not { Kind: PlaybackContextKind.Search, Title: "Поиск «nyan.mp3»", Filter: null } ||
                playbackQueue.Context.NextHref != SearchQueueFixture.Cursor(0) || playbackQueue.Snapshot.CanLoadRecommendations ||
                playbackQueue.Snapshot.ContextUpcoming.Select(e => e.Track.Id).SequenceEqual([3L, 4L, 5L]) == false)
                throw new InvalidOperationException("Search playback lost its displayed query, ranking, tail or pagination context.");
            var version = playbackQueue.SourceVersion;
            transport.Release.SetResult();
            await queueLoader.EnsureAheadAsync(force: true);
            AssertSource([1, 2, 3, 4, 5, 6]);
            // Visible pagination and another search must not mutate the captured source.
            SetSearchResults(new([.. items, LibraryItem.FromTrack(data[8])], null));
            await Show(SearchSection.Tracks, "different query", [LibraryItem.FromTrack(data[8])], null);
            BeginNavigation(new(Page.Home));
            if (playbackQueue.SourceVersion != version || playbackQueue.Context?.Title != "Поиск «nyan.mp3»")
                throw new InvalidOperationException("Navigation/new search replaced the active search queue.");
            foreach (var expected in new long[] { 3, 4, 5, 6, 7 })
            {
                await SkipAsync(1);
                if (current?.Id != expected || playbackQueue.Current?.Origin != PlaybackEntryOrigin.Context)
                    throw new InvalidOperationException("Search Next skipped a ranked result, an entity-only page or entered recommendations early.");
            }
            AssertSource([1, 2, 3, 4, 5, 6, 7]);
            // The visible list may independently prefetch the same API page.
            if (transport.Pages.Count != 8 || playbackQueue.Context?.NextHref != null)
                throw new InvalidOperationException("Search pagination did not exhaust the original query.");
            await SkipAsync(-1);
            if (current?.Id != 6) throw new InvalidOperationException("Search Previous lost source history.");
            playbackQueue.SetAutoContinue(true);
            if (!playbackQueue.AppendRecommendations(version, [data[7]]))
                throw new InvalidOperationException("Exhausted search could not resume normal continuation.");
            await SkipAsync(1);
            if (current?.Id != 7) throw new InvalidOperationException("Recommendation overtook the remaining search result.");
            await SkipAsync(1);
            if (current?.Id != 8 || playbackQueue.Current?.Origin != PlaybackEntryOrigin.Recommendation)
                throw new InvalidOperationException("Search continuation did not follow the exhausted source.");
            playbackQueue.SetAutoContinue(false);

            // Both overview compact rows and the best-result play button use the same source.
            await Show(SearchSection.All, "nyan.mp3", items, null);
            await Click(2, SearchBlockKind.Hero);
            AssertSource([1, 2, 3, 4, 5]);
            var hero = searchViews.Values.Single(v => v.Block?.Kind == SearchBlockKind.Hero).Hero!;
            RouteWaveformClick(hero.Card.FindVisualChild<Button>()!.Bounds.Center);
            await WaitForLikedLayoutAsync(() => current?.Id == 1 && audioReady);
            AssertSource([1, 2, 3, 4, 5]);
            shellScroll.SetScrollOffsets(0, SmoothScroll.Maximum(shellScroll));
            await Click(5, SearchBlockKind.Track);
            AssertSource([1, 2, 3, 4, 5]);

            // The active recording belongs to the player on every page. Editing
            // the query/category must not turn pause/seek into a source change.
            await Show(SearchSection.Tracks, "new query", [LibraryItem.FromTrack(data[4]), LibraryItem.FromTrack(data[8])], null);
            var oldVersion = playbackQueue.SourceVersion;
            await Click(5, SearchBlockKind.LibraryTrack);
            await WaitForLikedLayoutAsync(() => !isPlaying.Value);
            if (playbackQueue.SourceVersion != oldVersion) throw new InvalidOperationException("Pausing the globally active search recording replaced its source.");
            AssertSource([1, 2, 3, 4, 5]);
            await Click(9, SearchBlockKind.LibraryTrack);
            AssertSource([5, 9]);
            oldVersion = playbackQueue.SourceVersion;
            await Click(9, SearchBlockKind.LibraryTrack);
            await WaitForLikedLayoutAsync(() => !isPlaying.Value);
            if (playbackQueue.SourceVersion != oldVersion) throw new InvalidOperationException("Pausing search rebuilt its queue.");
            await Show(SearchSection.All, "new query", items, null);
            await SeekLikedTrackAsync(data[4], .25);
            AssertSource([1, 2, 3, 4, 5]);
            if (playbackQueue.SourceVersion == oldVersion || playbackQueue.Context?.Key != SearchPlaybackKey)
                throw new InvalidOperationException("Seeking another recording did not select the displayed search source.");
            oldVersion = playbackQueue.SourceVersion;
            var retainedContext = playbackQueue.Context;
            await Show(SearchSection.Tracks, "third query", [LibraryItem.FromTrack(data[4]), LibraryItem.FromTrack(data[8])], null);
            await SeekLikedTrackAsync(data[4], .5);
            if (playbackQueue.SourceVersion != oldVersion || playbackQueue.Context != retainedContext)
                throw new InvalidOperationException("Seeking the active recording from another category replaced its source.");
            AssertSource([1, 2, 3, 4, 5]);
            Console.WriteLine("SEARCH_QUEUE_UI_OK: real play clicks, merged ranking, query snapshot, eight-page dedup after navigation, next/previous, recommendation priority, global pause/seek preserving source and different-recording source selection");
        }
        finally { player = previousPlayer; }

        async Task Show(SearchSection section, string text, LibraryItem[] results, string? cursor)
        {
            await NavigateRouteAsync(new(Page.Search, Search: text, Section: section), _ =>
            {
                query.Value = text; searchSection.Value = section; searchHeading.Value = $"Результаты для «{text}»";
                SetSearchResults(new(results, cursor)); return Task.CompletedTask;
            });
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
        }
        async Task Click(long id, SearchBlockKind kind)
        {
            Button? button = null; Image? cover = null;
            await WaitForLikedLayoutAsync(() =>
            {
                foreach (var view in searchViews.Values.Where(v => v.Block?.Kind == kind))
                {
                    if (kind == SearchBlockKind.LibraryTrack && view.LibraryTrack?.Track?.Id == id)
                    { button = view.LibraryTrack.PlayButton; cover = null; }
                    var compact = kind == SearchBlockKind.Hero ? view.Hero?.Tracks.FirstOrDefault(r => r.Track?.Id == id) : view.Track.Track?.Id == id ? view.Track : null;
                    if (compact != null) { button = compact.PlaybackButton; cover = compact.Cover; }
                }
                return button is { ActualWidth: > 0 } && button.Bounds.IntersectsWith(shellScroll.Bounds);
            });
            RouteWaveformPointer(cover?.Bounds.Center ?? button!.Bounds.Center);
            if (cover != null) await WaitForLikedLayoutAsync(() => button!.IsHitTestVisible);
            RouteWaveformClick(button!.Bounds.Center);
            await WaitForLikedLayoutAsync(() => current?.Id == id && audioReady);
        }
        void AssertSource(long[] ids)
        {
            if (!playbackQueue.Snapshot.Source.Select(e => e.Track.Id).SequenceEqual(ids))
                throw new InvalidOperationException("Search source differs from the displayed track order: " + string.Join(',', playbackQueue.Snapshot.Source.Select(e => e.Track.Id)));
        }
    }

    private sealed class SearchQueueFixture : ISoundCloudBrowserTransport
    {
        internal int Calls;
        internal readonly HashSet<int> Pages = [];
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal static string Cursor(int page) => $"https://api-v2.soundcloud.com/search/tracks?q=nyan.mp3&cursor={page}";
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken token = default)
        {
            if (uri.AbsolutePath != "/search/tracks" || !uri.Query.Contains("q=nyan.mp3"))
                throw new InvalidOperationException("Queue requested a different search: " + uri.AbsolutePath);
            var page = int.Parse(uri.Query.Split("cursor=")[1]); ++Calls; Pages.Add(page);
            if (page == 0) await Release.Task.WaitAsync(token);
            var track = page == 0 ? ",{\"kind\":\"track\",\"id\":5,\"title\":\"Duplicate\"},{\"kind\":\"track\",\"id\":6,\"title\":\"Next track\",\"duration\":12000}" :
                page == 7 ? ",{\"kind\":\"track\",\"id\":7,\"title\":\"Final track\",\"duration\":12000}" : "";
            return JsonDocument.Parse("{\"collection\":[{\"kind\":\"user\",\"id\":77,\"username\":\"nyan.mp3\"}" + track +
                "],\"next_href\":" + JsonSerializer.Serialize(page < 7 ? Cursor(page + 1) : null) + "}");
        }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken token = default) => Task.CompletedTask;
    }
}
