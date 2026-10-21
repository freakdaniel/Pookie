using Pookie.SoundCloud;
using Pookie.Recommendations;

namespace Pookie.App.Home;

internal sealed class WaveListening
{
    private readonly ListeningTracker tracker = new();
    internal Guid SessionId { get; private set; } = Guid.NewGuid();
    internal void NewSession() => SessionId = Guid.NewGuid();
    internal SoundCloudTrack? Track { get; private set; }
    internal double Seconds => tracker.Seconds;
    internal bool WaveRecommendation { get; private set; }
    internal Guid? PlaybackId { get; private set; }
    private bool learningSuppressed;
    // Keep the clock alive for the independent SoundCloud history policy, while
    // discarding this playback as an implicit training example.
    internal void SuppressLearning() => learningSuppressed = true;
    internal void Begin(SoundCloudTrack track, bool waveRecommendation, TimeSpan now,
        ListeningSource source = ListeningSource.Track, WaveMode mode = WaveMode.Balance, DateTimeOffset? started = null,
        PlaybackSelection selection = PlaybackSelection.Unknown, Pookie.Recommendations.Learning.FeatureSnapshot? features = null,
        bool? familiarAtSelection = null)
    {
        tracker.Begin(WaveTrack.From(track), SessionId, waveRecommendation ? ListeningSource.Wave : source,
            (Pookie.Recommendations.WaveMode)mode, started ?? DateTimeOffset.UtcNow, now, selection);
        Track = track; WaveRecommendation = waveRecommendation;
        learningSuppressed = false;
        this.features = features;
        this.familiarAtSelection = familiarAtSelection;
        PlaybackId = tracker.PlaybackId;
    }
    private Pookie.Recommendations.Learning.FeatureSnapshot? features;
    private bool? familiarAtSelection;
    internal void Poll(double nextPosition, bool playing, TimeSpan now) => tracker.Poll(nextPosition, playing, now);
    internal ListeningObservation? Finish(WaveTaste taste, bool skipped, DateTimeOffset now) =>
        Finish(taste, skipped ? ListeningEndReason.Skip : ListeningEndReason.TrackChanged, now);
    internal ListeningObservation? Finish(WaveTaste taste, ListeningEndReason reason, DateTimeOffset now, bool learn = true)
    {
        var observation = tracker.Finish(reason, now);
        if (Track == null || observation == null) return null;
        if (learn && !learningSuppressed)
            taste.Record(Track, observation.ListenedSeconds, observation.EarlySkip, now, reason == ListeningEndReason.Rejected);
        Track = null; PlaybackId = null;
        if (!learn || learningSuppressed) return null;
        return observation with { Features = features, AlgorithmVersion = "learned-base-v2", FamiliarAtSelection = familiarAtSelection };
    }
}
