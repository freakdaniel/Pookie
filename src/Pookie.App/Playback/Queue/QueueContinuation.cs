using Pookie.SoundCloud;
using Pookie.App.Home;

namespace Pookie.App.Playback;

// Network candidates stay scoped to the original playback source, not the page.
internal sealed class QueueContinuation
{
    private long version = -1;
    private readonly Dictionary<long, SoundCloudTrack> candidates = [];
    private readonly Dictionary<long, string?> cursors = [];

    internal static SoundCloudTrack[] Seeds(PlaybackQueueSnapshot state)
    {
        var played = state.History.Concat(state.Current != null ? [state.Current] : []).ToArray();
        var original = played.Where(entry => entry.Origin == PlaybackEntryOrigin.Context).TakeLast(2).Select(entry => entry.Track);
        if (!original.Any()) original = state.Source.TakeLast(2).Select(entry => entry.Track);
        var latest = played.LastOrDefault(entry => entry.Origin == PlaybackEntryOrigin.Recommendation)?.Track;
        return original.Concat(latest != null ? [latest] : []).Where(HomeRecommendations.Playable).DistinctBy(track => track.Id).Take(3).ToArray();
    }

    internal async Task<SoundCloudTrack[]> FetchAsync(PlaybackQueueSnapshot state, WaveTaste taste,
        Func<long, string?, CancellationToken, Task<TrackPage>> fetch, CancellationToken token)
    {
        if (version != state.SourceVersion) { version = state.SourceVersion; candidates.Clear(); cursors.Clear(); }
        var requestedVersion = version;
        var seeds = Seeds(state).Where(track => !taste.IsBlocked(track) && !taste.IsUnavailable(track)).ToArray();
        var reserved = state.Source.Concat(state.History).Concat(state.ManualUpcoming).Concat(state.Forward)
            .Concat(state.Recommendations).Concat(state.Current != null ? [state.Current] : []).Select(entry => entry.Track).ToArray();
        SoundCloudTrack[] Rank() => taste.RankContinuation(candidates.Values,
            state.History.Select(entry => entry.Track).Concat(state.Current != null ? [state.Current.Track] : []),
            reserved, PlaybackQueue.WaveUpcomingLimit - state.Recommendations.Count, DateTimeOffset.UtcNow);
        for (var round = 0; round < 3; round++)
        {
            var fetched = false;
            foreach (var seed in seeds)
            {
                token.ThrowIfCancellationRequested();
                if (cursors.TryGetValue(seed.Id, out var cursor) && cursor == null) continue;
                var page = await fetch(seed.Id, cursor, token);
                token.ThrowIfCancellationRequested();
                if (requestedVersion != version) return [];
                fetched = true; cursors[seed.Id] = page.NextHref == cursor ? null : page.NextHref;
                foreach (var track in page.Tracks.Where(HomeRecommendations.Playable)) candidates[track.Id] = track;
            }
            if (candidates.Count > 800)
                foreach (var id in candidates.Keys.Take(candidates.Count - 800).ToArray()) candidates.Remove(id);
            var result = Rank();
            if (result.Length > 0 || !fetched) return result;
        }
        return [];
    }
}
