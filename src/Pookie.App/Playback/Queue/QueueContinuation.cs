using Pookie.SoundCloud;
using Pookie.App.Home;

namespace Pookie.App.Playback;

internal readonly record struct RecommendationBatch(SoundCloudTrack[] Tracks, bool Exhausted = false);

// Consume each SoundCloud stream in its original order. A new seed is used only
// after the current stream ends, never to interleave or rerank its remaining pages.
internal sealed class QueueContinuation
{
    private sealed class Stream(long seed)
    {
        internal readonly long Seed = seed;
        internal readonly Queue<SoundCloudTrack> Buffer = new();
        internal readonly HashSet<string> Cursors = new(StringComparer.Ordinal);
        internal string? Cursor;
        internal bool Finished;
    }

    private long version = -1;
    private Stream? stream;
    private readonly HashSet<long> started = [];

    internal async Task<RecommendationBatch> FetchAsync(PlaybackQueueSnapshot state, Func<SoundCloudTrack, bool> allowed,
        Func<long, string?, CancellationToken, Task<TrackPage>> fetch, CancellationToken token)
    {
        if (version != state.SourceVersion)
        { version = state.SourceVersion; stream = null; started.Clear(); }
        var played = state.History.Append(state.Current).OfType<PlaybackEntry>();
        var seeds = state.Context?.Kind == PlaybackContextKind.Radio
            ? state.Source.Take(1).Concat(played.Where(entry => entry.Origin == PlaybackEntryOrigin.Recommendation).Reverse())
            : played.Where(entry => entry.Origin != PlaybackEntryOrigin.Manual).Reverse().Take(3).Concat(state.Source.TakeLast(2).Reverse());
        var reserved = new TrackIdentity(state.Source.Concat(state.History).Concat(state.ManualUpcoming)
            .Concat(state.Forward).Concat(state.Recommendations).Append(state.Current).OfType<PlaybackEntry>().Select(entry => entry.Track));
        var limit = PlaybackQueue.RecommendationLimit - state.Recommendations.Count;
        var result = new List<SoundCloudTrack>();
        var requests = 0;
        while (result.Count < limit)
        {
            token.ThrowIfCancellationRequested();
            if (stream == null || stream.Finished && stream.Buffer.Count == 0)
            {
                var next = seeds.FirstOrDefault(entry => !started.Contains(entry.Track.Id) &&
                    HomeRecommendations.Playable(entry.Track));
                if (next == null) return new(result.ToArray(), Exhausted: true);
                stream = new(next.Track.Id); started.Add(next.Track.Id);
            }
            var active = stream;
            while (active.Buffer.TryDequeue(out var track))
            {
                if (HomeRecommendations.Playable(track) && allowed(track) && reserved.Add(track)) result.Add(track);
                if (result.Count == limit) return new(result.ToArray());
            }
            if (result.Count > 0) return new(result.ToArray());
            if (active.Finished) continue;
            if (requests++ == 8) return new(result.ToArray());
            var page = await fetch(active.Seed, active.Cursor, token);
            token.ThrowIfCancellationRequested();
            if (state.SourceVersion != version || active != stream) return new([]);
            active.Cursor = page.NextHref;
            active.Finished = string.IsNullOrEmpty(active.Cursor) || !active.Cursors.Add(active.Cursor);
            foreach (var item in page.Tracks) active.Buffer.Enqueue(item);
        }
        return new(result.ToArray());
    }
}
