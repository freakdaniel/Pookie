using Pookie.SoundCloud;

namespace Pookie.App.Home;

// Monotonic wall time caps position deltas. Seeking, buffering and pauses never become listened time.
internal sealed class WaveListening
{
    internal SoundCloudTrack? Track { get; private set; }
    internal double Seconds { get; private set; }
    private double position;
    private TimeSpan time;
    private bool audible;
    internal bool WaveRecommendation { get; private set; }
    internal void Begin(SoundCloudTrack track, bool waveRecommendation, TimeSpan now)
    { Track = track; WaveRecommendation = waveRecommendation; Seconds = position = 0; time = now; audible = false; }
    internal void Poll(double nextPosition, bool playing, TimeSpan now)
    {
        var elapsed = (now - time).TotalSeconds; var delta = nextPosition - position;
        if (Track != null && playing && audible && elapsed is > 0 and < 3 && delta > 0 && delta <= elapsed * 1.5 + .3)
            Seconds += Math.Min(delta, elapsed);
        position = nextPosition; time = now; audible = playing;
    }
    internal void Finish(WaveTaste taste, bool skipped, DateTimeOffset now)
    {
        if (Track == null) return;
        var early = skipped && WaveRecommendation && Seconds < Math.Min(20, Track.DurationSeconds * .25);
        taste.Record(Track, Seconds, early, now);
        Track = null; Seconds = 0; audible = false;
    }
}
