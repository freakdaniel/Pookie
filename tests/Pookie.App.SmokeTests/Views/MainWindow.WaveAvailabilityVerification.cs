using System.Text.Json;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyWaveAvailabilityAsync()
    {
        var originalPlayer = player; var originalTransport = api.BrowserTransport; var originalSession = api.Session;
        var transport = new AvailabilityFixtureTransport();
        timer.Stop(); ResetWaveCandidateAnalysis();
        try
        {
            player = new QueueVerificationPlayer(); api.BrowserTransport = transport;
            api.Session = new("availabilityfixture", "fixture-token", "Test");
            EnsureWaveTaste(); waveTaste.SetMode(Pookie.App.Home.WaveMode.Balance); waveCandidates.Clear(); waveLikesCursor = null;
            foreach (var mix in HomeItems("mixes")) waveMixesLoaded.Add(mix.Key);
            SoundCloudTrack Track(long id) => new() { Id = id, Title = "Availability fixture " + id, Duration = 12000 };
            void Start(params SoundCloudTrack[] data)
            {
                waveCandidates.Clear(); AddWaveCandidates(data);
                StartQueueContext(new(WaveQueueKey, "Wave fixture", "", Kind: PlaybackContextKind.Wave), data, data[0]);
            }
            var missing = Track(9601); var missingAgain = Track(9602); var healthy = Track(9603);
            transport.Missing.UnionWith([missing.Id, missingAgain.Id]);
            Start(missing, missingAgain, healthy);
            await PlayAsync(missing);
            if (current?.Id != healthy.Id || !audioReady || !isPlaying.Value || playbackLoading.Value ||
                !waveTaste.IsUnavailable(missing) || !waveTaste.IsUnavailable(missingAgain) ||
                waveTaste.IsBlocked(missing) || waveTaste.Snapshot().Tracks.Any(item => item.Track.Id is 9601 or 9602 && (item.Plays > 0 || item.EarlySkips > 0)))
                throw new InvalidOperationException("Missing Wave tracks did not advance to healthy audio without teaching a dislike/skip.");
            AddWaveCandidates([missing, missingAgain]);
            SaveWaveProfile();
            if (waveCandidates.ContainsKey(missing.Id) || !waveStore.Load(waveAccount).IsUnavailable(missing))
                throw new InvalidOperationException("Missing Wave metadata returned through a stale import or profile reload.");
            var transient = Track(9604); transport.Transient = transient.Id;
            Start(transient, healthy);
            try { await PlayAsync(transient); throw new InvalidOperationException("Expected transient fixture error."); }
            catch (SoundCloudException error) when (error.StatusCode == 429) { }
            if (waveTaste.IsUnavailable(transient) || current?.Id != transient.Id)
                throw new InvalidOperationException("A rate limit poisoned availability or silently changed the selected track.");
            var stale = Track(9605); transport.Delayed = stale.Id;
            Start(stale, healthy); var late = PlayAsync(stale);
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await PlayAsync(healthy); transport.Release.SetResult(); await late;
            if (current?.Id != healthy.Id || !audioReady || waveTaste.IsUnavailable(stale))
                throw new InvalidOperationException("A stale unavailable response changed the newer selection.");
            var allMissing = Enumerable.Range(9701, 12).Select(id => Track(id)).ToArray();
            transport.Missing.UnionWith(allMissing.Select(track => track.Id)); Start(allMissing);
            var before = transport.Reads;
            await PlayAsync(allMissing[0]);
            if (transport.Reads - before != 6 || audioReady || isPlaying.Value || playbackLoading.Value)
                throw new InvalidOperationException("Unavailable Wave retries were unbounded or left the player loading.");
            Console.WriteLine("WAVE_AVAILABILITY_OK: missing-track auto advance, persistent exclusion, stale imports, no taste penalty, transient errors preserved, stale response protection and bounded retries");
        }
        finally { transport.Release.TrySetResult(); player = originalPlayer; api.BrowserTransport = originalTransport; api.Session = originalSession; }
    }

    private sealed class AvailabilityFixtureTransport : ISoundCloudBrowserTransport
    {
        internal HashSet<long> Missing { get; } = [];
        internal long Transient, Delayed;
        internal int Reads;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Availability must not change SoundCloud likes.");
        public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath.EndsWith("/related")) return JsonDocument.Parse("""{"collection":[],"next_href":null}""");
            if (uri.AbsolutePath.StartsWith("/tracks/"))
            {
                var id = long.Parse(uri.AbsolutePath.Split('/')[2]); Reads++;
                if (id == Delayed) { Started.TrySetResult(); await Release.Task; throw new SoundCloudException("Stale fixture track", 404); }
                if (Missing.Contains(id)) throw new SoundCloudException("Deleted fixture track", 404);
                if (id == Transient) throw new SoundCloudException("Fixture rate limit", 429);
                return JsonDocument.Parse($$$"""{"id":{{{id}}},"title":"Available fixture","duration":12000,"media":{"transcodings":[{"url":"https://api-v2.soundcloud.com/fixture-stream","format":{"protocol":"progressive"}}]}}""");
            }
            if (uri.AbsolutePath == "/fixture-stream") return JsonDocument.Parse("""{"url":"https://cf-media.sndcdn.com/fixture"}""");
            throw new InvalidOperationException("Unexpected availability fixture request: " + uri.AbsolutePath);
        }
    }
}
