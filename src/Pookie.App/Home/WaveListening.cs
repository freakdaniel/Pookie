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
    internal void Begin(SoundCloudTrack track, bool waveRecommendation, TimeSpan now,
        ListeningSource source = ListeningSource.Track, WaveMode mode = WaveMode.Balance, DateTimeOffset? started = null,
        PlaybackSelection selection = PlaybackSelection.Unknown, Pookie.Recommendations.Learning.FeatureSnapshot? features = null,
        Pookie.Recommendations.WaveMood mood = Pookie.Recommendations.WaveMood.Auto)
    {
        tracker.Begin(WaveTrack.From(track), SessionId, waveRecommendation ? ListeningSource.Wave : source,
            (Pookie.Recommendations.WaveMode)mode, started ?? DateTimeOffset.UtcNow, now, selection);
        Track = track; WaveRecommendation = waveRecommendation;
        this.features = features;
        this.mood = waveRecommendation ? mood : Pookie.Recommendations.WaveMood.Auto;
    }
    private Pookie.Recommendations.Learning.FeatureSnapshot? features;
    private Pookie.Recommendations.WaveMood mood;
    internal void Poll(double nextPosition, bool playing, TimeSpan now) => tracker.Poll(nextPosition, playing, now);
    internal ListeningObservation? Finish(WaveTaste taste, bool skipped, DateTimeOffset now) =>
        Finish(taste, skipped ? ListeningEndReason.Skip : ListeningEndReason.TrackChanged, now);
    internal ListeningObservation? Finish(WaveTaste taste, ListeningEndReason reason, DateTimeOffset now)
    {
        var observation = tracker.Finish(reason, now);
        if (Track == null || observation == null) return null;
        taste.Record(Track, observation.ListenedSeconds, observation.EarlySkip, now, reason == ListeningEndReason.Rejected);
        Track = null;
        return observation with { Features = features, AlgorithmVersion = "learned-v2", Mood = mood };
    }
}
