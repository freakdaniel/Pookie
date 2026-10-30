using System.Text.Json;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyQueueContinuationAsync()
    {
        var originalPlayer = player;
        try
        {
            timer.Stop(); player = new QueueVerificationPlayer();
            api.Session = new("continuationfixture", "fixture-token", "Test");
            var transport = new ContinuationFixtureTransport(); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Continuation fixture" }; signedIn.Value = true; likedIdsReady = true;
            EnsureWaveTaste(); await ShowWorkspaceAsync();
            static SoundCloudTrack Track(long id) => new() { Id = id, Title = "Fixture " + id, Duration = 12000, User = new() { Id = id, Username = "Artist " + id } };
            var source = new[] { Track(101), Track(102) };
            StartQueueContext(new("fixture:playlist", "Исходный плейлист", "Сейчас играет из плейлиста", Kind: PlaybackContextKind.Playlist), source, source[0]);
            var version = playbackQueue.SourceVersion;
            await PlayAsync(source[0]); await queueLoader.EnsureAheadAsync();
            if (AssertRecommendations().Single().Track.Id != 201 || !playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).SequenceEqual(new long[] { 101, 102 }))
                throw new InvalidOperationException("Default continuation replaced the original source or was not prefetched.");
            var manual = playbackQueue.Enqueue(Track(800)); RefreshQueue();
            await SkipAsync(1, naturalEnd: true);
            if (playbackQueue.Current?.EntryId != manual.EntryId) throw new InvalidOperationException("Continuation jumped ahead of a manual addition.");
            await SkipAsync(1, naturalEnd: true);
            if (current?.Id != 102) throw new InvalidOperationException("Continuation jumped ahead of the source tail.");
            await SkipAsync(1, naturalEnd: true); await queueLoader.EnsureAheadAsync();
            if (playbackQueue.Current?.Origin != PlaybackEntryOrigin.Recommendation || current?.Id is 101 or 102 or 201 or 800 ||
                !audioReady || !isPlaying.Value || !waveTaste.IsUnavailable(Track(201)) || playbackQueue.SourceVersion != version ||
                playbackQueue.Context?.Title != "Исходный плейлист" || playbackHistoryContext != null ||
                WaveListeningSource(playbackQueue.Current) != Pookie.Recommendations.ListeningSource.Autoplay)
                throw new InvalidOperationException("Source completion or missing recommendation did not continue with independent playback/history identity.");
            AssertRecommendations();
            queueOpen.Value = true;
            await WaitForLikedLayoutAsync(() => popupQueueData.Any(block => block.Key == "recommendations" && block.Title == "Похожие дальше") &&
                shellMenus.Single(menu => ReferenceEquals(menu.State, queueOpen)).Layer.Reveal > .999);
            CaptureUiPreview("queue-continuation");
            SetExpandedPlayer(true); ToggleExpandedPanel(PlayerPanel.Queue);
            await WaitForLikedLayoutAsync(() => !expandedAnimation.IsRunning && !panelAnimation.IsRunning && panelReveal > .999);
            if (!expandedQueueData.Any(block => block.Key == "recommendations" && block.Title == "Похожие дальше") ||
                expandedQueueData.Single(block => block.Key == "source").Title != "Исходный плейлист" ||
                expandedQueueData.Any(block => block.Key == "upcoming") || expandedQueueData.Any(block => block.Track?.Id == 201))
                throw new InvalidOperationException("Expanded queue did not distinguish continuation from the original playlist.");
            CaptureUiPreview("queue-continuation-expanded");
            SetExpandedPlayer(false);
            var retained = AssertRecommendations().ToArray();
            CycleRepeat();
            await queueLoader.EnsureAheadAsync();
            if (!playbackQueue.Snapshot.Recommendations.SequenceEqual(retained) || playbackQueue.Snapshot.CanLoadRecommendations ||
                !playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).SequenceEqual(new long[] { 101, 102 }))
                throw new InvalidOperationException("Repeat cleared retained recommendations, kept refilling them or replaced the original source.");
            await SkipAsync(1, naturalEnd: true);
            if (current?.Id != 101 || playbackQueue.Current?.Origin != PlaybackEntryOrigin.Context ||
                !playbackQueue.Snapshot.Recommendations.SequenceEqual(retained))
                throw new InvalidOperationException("Repeat played a retained recommendation instead of restarting the original source.");
            await HandleMediaCommandAsync(new(Pookie.Media.MediaAction.Stop));
            if (waveListening.Track != null || audioReady || isPlaying.Value)
                throw new InvalidOperationException("System Stop did not finish the listening observation.");
            Console.WriteLine("UI_QUEUE_CONTINUATION_OK: default prefetch, manual/source/recommendation priority, missing-track advance, retained source/history identity, popup/expanded sections and repeat priority");
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            Console.Error.WriteLine("UI_QUEUE_CONTINUATION_FAILED: " + error);
        }
        finally { player = originalPlayer; Window.Close(); }

        IReadOnlyList<PlaybackEntry> AssertRecommendations()
        {
            var state = playbackQueue.Snapshot;
            if (state.Recommendations.Count == 0 || state.Recommendations.Any(entry => entry.Origin != PlaybackEntryOrigin.Recommendation ||
                state.Source.Concat(state.History).Any(previous => previous.Track.Id == entry.Track.Id)))
                throw new InvalidOperationException("Continuation is empty or repeats the original/played tracks.");
            return state.Recommendations;
        }
    }

    private sealed class ContinuationFixtureTransport : ISoundCloudBrowserTransport
    {
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Continuation must not modify SoundCloud likes.");
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath.EndsWith("/related"))
            {
                var id = long.Parse(uri.AbsolutePath.Split('/')[2]);
                var ids = id == 101 ? new long[] { 201 } : Enumerable.Range(202, 14).Select(id => (long)id).ToArray();
                return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(new { collection = ids.Select(id => new { id, title = "Fixture " + id,
                    duration = 12000, user = new { id, username = "Artist " + id } }), next_href = (string?)null })));
            }
            if (uri.AbsolutePath.StartsWith("/tracks/"))
            {
                var id = long.Parse(uri.AbsolutePath.Split('/')[2]);
                if (id == 201) throw new SoundCloudException("Deleted fixture recommendation", 404);
                return Task.FromResult(JsonDocument.Parse($$$"""{"id":{{{id}}},"title":"Fixture {{{id}}}","duration":12000,"user":{"id":{{{id}}},"username":"Artist {{{id}}}"},"media":{"transcodings":[{"url":"https://api-v2.soundcloud.com/fixture-stream","format":{"protocol":"progressive"}}]}}"""));
            }
            if (uri.AbsolutePath == "/fixture-stream") return Task.FromResult(JsonDocument.Parse("""{"url":"https://cf-media.sndcdn.com/fixture"}"""));
            throw new InvalidOperationException("Unexpected continuation fixture request: " + uri.AbsolutePath);
        }
    }
}
