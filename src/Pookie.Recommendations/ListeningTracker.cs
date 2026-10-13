namespace Pookie.Recommendations;

// Monotonic elapsed time caps position deltas. Pauses, buffering and seeks are not listening.
public sealed class ListeningTracker
{
    private WaveTrack? track;
    private Guid playbackId, sessionId;
    private ListeningSource source;
    private WaveMode mode;
    private PlaybackSelection selection;
    private DateTimeOffset startedAt;
    private double position;
    private TimeSpan time;
    private bool audible;
    public double Seconds { get; private set; }

    public void Begin(WaveTrack value, Guid session, ListeningSource origin, WaveMode waveMode,
        DateTimeOffset started, TimeSpan now, PlaybackSelection selection = PlaybackSelection.Unknown)
    {
        if (track != null) throw new InvalidOperationException("Finish the current observation first.");
        if (session == Guid.Empty || value.Id <= 0 || string.IsNullOrWhiteSpace(value.Title) ||
            !double.IsFinite(value.Duration) || value.Duration < 0) throw new ArgumentException("Invalid playback.");
        track = value; playbackId = Guid.NewGuid(); sessionId = session; source = origin; mode = waveMode;
        this.selection = selection;
        startedAt = started; Seconds = position = 0; time = now; audible = false;
    }

    public void Poll(double nextPosition, bool playing, TimeSpan now)
    {
        var elapsed = (now - time).TotalSeconds; var delta = nextPosition - position;
        if (track != null && playing && audible && elapsed is > 0 and < 3 && delta > 0 && delta <= elapsed * 1.5 + .3)
            Seconds += Math.Min(delta, elapsed);
        position = nextPosition; time = now; audible = playing;
    }

    public ListeningObservation? Finish(ListeningEndReason reason, DateTimeOffset now)
    {
        if (track == null) return null;
        var observation = new ListeningObservation(playbackId, sessionId, track, startedAt,
            now < startedAt ? startedAt : now, Seconds, source, reason, mode, Selection: selection);
        track = null; Seconds = 0; audible = false;
        return observation;
    }
}
