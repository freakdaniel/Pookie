using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLikesFilterUiAsync()
    {
        string? invalidFrame = null;
        void Sample()
        {
            if (!FilteringLikes) return;
            if (likedEmpty.Value && (paginationLoading.Value || nextHref != null || likesLoadingView.Skeleton.IsVisible))
                invalidFrame = "Empty-result text appeared before all likes were checked.";
            if (pageItems[likedGrid].LoadingCount != 0 || pageItems[likedList].LoadingCount != 0)
                invalidFrame = "Filtered metadata loading displayed unrelated track skeletons.";
        }
        try
        {
            var transport = new LikesFilterTransport();
            api.Session = new("fixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Likes filter fixture" }; signedIn.Value = true;
            await ShowWorkspaceAsync(); likedIdsReady = true;
            Window.FrameRendered += Sample;
            static SoundCloudTrack Track(long id, string title = "Другой трек", string author = "Library artist") =>
                new() { Id = id, Title = title, Duration = 120000, User = new() { Username = author } };
            var initial = Enumerable.Range(1, 30).Select(i => Track(95000 + i)).ToArray();
            string Cursor(string value) => "https://api-v2.soundcloud.com/users/42/likes?cursor=" + value;
            void Start(string cursor)
            {
                likedFilter.Text = "";
                libraryLikes = new(initial, Cursor(cursor));
                ShowLibraryTracks();
                likedFilter.Text = "ВШБ";
            }
            async Task Pending(int count)
            {
                await WaitForLikedLayoutAsync(() => transport.Requests.Count == count && paginationLoading.Value);
                if (transport.MaxConcurrent != 1 || likedEmpty.Value || !likedFilterNotice.IsVisible || likedFilterRetry.IsVisible)
                    throw new InvalidOperationException("Library filtering overlapped requests or lost its incomplete-search state.");
            }
            foreach (var listView in new[] { false, true })
            {
                likesAsList.Value = listView;
                var before = transport.Requests.Count;
                Start((listView ? "list" : "grid") + "-1");
                // Three empty metadata pages must not end the search. The fourth
                // contains tracks but still no match, reproducing old-liked songs.
                for (var i = 1; i <= 4; i++)
                {
                    await Pending(before + i);
                    transport.Reply(i < 4 ? [] : Enumerable.Range(1, 30).Select(n => Track(96000 + n)).ToArray(), Cursor("older-" + (i + 1)));
                }
                await Pending(before + 5);
                transport.Reply(Enumerable.Range(1, 20).Select(n => Track(97000 + n, "ВШБ mix " + n)).ToArray(), Cursor("oldest"));
                // Continue even after matches fill the viewport: this is a search
                // of the whole library, rather than its first matching screenful.
                await Pending(before + 6);
                if (pageItems[likedList].DataCount != 20)
                    throw new InvalidOperationException("Matches were not displayed while older likes were being checked.");
                transport.Reply([Track(98001, "ВШБ (feat. CUPSIZE)", "GRILLYAZH")], null);
                await WaitForLikedLayoutAsync(() => nextHref == null && !paginationLoading.Value && pageItems[likedList].DataCount == 21);
                if (likedEmpty.Value || likedFilterNotice.IsVisible || libraryLikes?.Tracks.Length != 81)
                    throw new InvalidOperationException("The oldest liked track was not retained in the completed library search.");
                likedFilter.Text = "grillyazh";
                if (pageItems[likedList].DataCount != 1 || !tracks.Any(track => track.Id == 98001))
                    throw new InvalidOperationException("Author filtering lost an older liked track.");
                likedFilter.Text = "совсем отсутствующий трек";
                if (!likedEmpty.Value || likedFilterNotice.IsVisible || transport.Requests.Count != before + 6)
                    throw new InvalidOperationException("A completed library search did not distinguish an absent song from loading.");
                likedFilter.Text = "";
                if (pageItems[likedList].DataCount != 81 || transport.Requests.Count != before + 6)
                    throw new InvalidOperationException("Clearing the filter discarded loaded likes or fetched the library again.");
                Console.WriteLine($"LIKES_FILTER_UI_OK: {(listView ? "list" : "grid")}; six metadata pages, three empty pages, full-viewport continuation, oldest title/author match, final empty result and cached reuse");
            }
            likedFilter.Text = "нет такого трека";
            SetLikesLoading(true); ReplaceTracks(new([], null));
            await WaitForLoginFrameAsync();
            SetLikesLoading(false);
            if (likedEmpty.Value)
                throw new InvalidOperationException("An empty result overlapped the initial skeleton's fade-out.");
            await WaitForLikedLayoutAsync(() => !likesLoadingView.Skeleton.IsVisible && likedEmpty.Value);

            var calls = transport.Requests.Count;
            Start("error"); await Pending(calls + 1);
            transport.Fail();
            await WaitForLikedLayoutAsync(() => !paginationLoading.Value && likedFilterRetry.IsVisible);
            for (var i = 0; i < 3; i++) await WaitForLoginFrameAsync();
            if (likedEmpty.Value || transport.Requests.Count != calls + 1)
                throw new InvalidOperationException("A failed library scan claimed no matches or retried automatically.");
            RetryLikedFilter(); await Pending(calls + 2);
            // A changed query uses the response against its current filter.
            likedFilter.Text = "grillyazh";
            transport.Reply([Track(98001, "ВШБ (feat. CUPSIZE)", "GRILLYAZH")], null);
            await WaitForLikedLayoutAsync(() => !paginationLoading.Value && nextHref == null && pageItems[likedList].DataCount == 1);
            if (likedFilterRetry.IsVisible || likedFilterNotice.IsVisible)
                throw new InvalidOperationException("A successful retry retained the failed-filter notice.");

            calls = transport.Requests.Count;
            Start("clear"); await Pending(calls + 1);
            likedFilter.Text = "";
            transport.Reply(Enumerable.Range(1, 30).Select(n => Track(99000 + n)).ToArray(), Cursor("after-clear"));
            await WaitForLikedLayoutAsync(() => !paginationLoading.Value && tracks.Count == 60);
            for (var i = 0; i < 3; i++) await WaitForLoginFrameAsync();
            if (transport.Requests.Count != calls + 1 || likedFilterNotice.IsVisible)
                throw new InvalidOperationException("Clearing the query kept scanning the remaining library.");

            calls = transport.Requests.Count;
            Start("cycle"); await Pending(calls + 1);
            transport.Reply([], Cursor("cycle"));
            await WaitForLikedLayoutAsync(() => !paginationLoading.Value && likedFilterRetry.IsVisible);
            for (var i = 0; i < 3; i++) await WaitForLoginFrameAsync();
            if (transport.Requests.Count != calls + 1 || likedEmpty.Value)
                throw new InvalidOperationException("A repeated library cursor looped or pretended the search was complete.");

            calls = transport.Requests.Count;
            Start("late"); await Pending(calls + 1);
            await NavigateAsync(Page.Feed);
            transport.Reply([Track(99999, "ВШБ late")], null);
            await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
            if (page.Value != Page.Feed || tracks.Any(track => track.Id == 99999) || paginationLoading.Value || invalidFrame != null)
                throw new InvalidOperationException(invalidFrame ?? "A cancelled library response changed another page.");
            Console.WriteLine("LIKES_FILTER_LIFECYCLE_OK: no empty/skeleton overlap, sequential requests, failure/retry, live query changes, clear stops scanning, cursor-cycle guard, navigation cancellation; no global search or writes");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("LIKES_FILTER_UI_FAILED: " + error); }
        finally { Window.FrameRendered -= Sample; Window.Close(); }
    }

    private sealed class LikesFilterTransport : ISoundCloudBrowserTransport
    {
        internal List<TaskCompletionSource<string>> Requests { get; } = [];
        private int concurrent;
        internal int MaxConcurrent { get; private set; }
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath.Contains("search", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Library filtering called global search.");
            if (uri.AbsolutePath != "/users/42/likes") return JsonDocument.Parse("{\"collection\":[],\"next_href\":null}");
            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(reply); MaxConcurrent = Math.Max(MaxConcurrent, ++concurrent);
            try { return JsonDocument.Parse(await reply.Task.WaitAsync(cancellationToken)); }
            finally { --concurrent; }
        }
        internal void Reply(SoundCloudTrack[] tracks, string? next) => Requests[^1].TrySetResult(JsonSerializer.Serialize(new
        {
            collection = tracks.Select(track => new { track = new
            { id = track.Id, title = track.Title, duration = track.Duration, user = new { username = track.Author } } }),
            next_href = next
        }));
        internal void Fail() => Requests[^1].SetException(new HttpRequestException("Fixture library page failure"));
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Library filtering wrote a like.");
    }
}
