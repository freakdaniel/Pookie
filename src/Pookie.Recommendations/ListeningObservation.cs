namespace Pookie.Recommendations;

public enum ListeningSource { Wave, Manual, Likes, Playlist, Album, Artist, Feed, Recommendations, History, Recent, Track }
public enum ListeningEndReason { TrackChanged, NaturalEnd, Skip, Previous, Rejected, Stopped, Shutdown, AccountChanged, PlaybackError }
public enum PlaybackSelection { Unknown, TrackSelection, QueueAdvance }
public enum WaveEventKind { Listening, Like, Unlike, Block, ResetBlocks, Unblock }

public sealed record ListeningObservation(Guid PlaybackId, Guid SessionId, WaveTrack Track,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, double ListenedSeconds,
    ListeningSource Source, ListeningEndReason EndReason, WaveMode Mode, string AlgorithmVersion = "rules-v1",
    PlaybackSelection Selection = PlaybackSelection.Unknown, Learning.FeatureSnapshot? Features = null, WaveMood Mood = WaveMood.Auto,
    bool? FamiliarAtSelection = null)
{
    public double ListenedFraction => Track.DurationSeconds > 0 ? Math.Clamp(ListenedSeconds / Track.DurationSeconds, 0, 1) : 0;
    public bool EarlySkip => Source == ListeningSource.Wave && EndReason == ListeningEndReason.Skip &&
        ListenedSeconds < Math.Min(20, Track.DurationSeconds * .25);
}

// Events contain a projection of public metadata, never a web session or stream credentials.
public sealed record WaveEvent(Guid EventId, long AccountId, DateTimeOffset OccurredAt, WaveEventKind Kind,
    WaveTrack? Track = null, ListeningObservation? Listening = null, WaveMood Mood = WaveMood.Auto, Guid? PlaybackId = null)
{
    public static WaveEvent FromListening(long accountId, ListeningObservation observation) =>
        new(observation.PlaybackId, accountId, observation.FinishedAt, WaveEventKind.Listening, observation.Track, observation, observation.Mood);

    public void Validate()
    {
        if (EventId == Guid.Empty || PlaybackId == Guid.Empty || AccountId <= 0 || !Enum.IsDefined(Kind) || !Enum.IsDefined(Mood))
            throw new ArgumentException("Invalid event identity.");
        if (Kind == WaveEventKind.ResetBlocks)
        {
            if (Track != null || Listening != null) throw new ArgumentException("Reset events have no track.");
            return;
        }
        if (Track is not { Id: > 0 } || string.IsNullOrWhiteSpace(Track.Title) ||
            !double.IsFinite(Track.Duration) || Track.Duration < 0)
            throw new ArgumentException("Invalid event track.");
        if (Kind != WaveEventKind.Listening)
        {
            if (Listening != null) throw new ArgumentException("Preference events have no listening observation.");
            return;
        }
        if (Listening is not { } value || value.PlaybackId != EventId || value.SessionId == Guid.Empty ||
            value.Track != Track || !double.IsFinite(value.ListenedSeconds) || value.ListenedSeconds < 0 ||
            value.FinishedAt < value.StartedAt || value.FinishedAt != OccurredAt ||
            !Enum.IsDefined(value.Source) || !Enum.IsDefined(value.EndReason) || !Enum.IsDefined(value.Mode) || !Enum.IsDefined(value.Selection) || !Enum.IsDefined(value.Mood))
            throw new ArgumentException("Invalid listening observation.");
        if (value.Features is { } features && (features.Values.Length != Learning.OnlineRanker.Dimensions || !Enum.IsDefined(features.Mood) ||
            features.ContextValues is { } context && (context.Length != Learning.OnlineRanker.Dimensions || context.Any(number => !float.IsFinite(number))) ||
            !double.IsFinite(features.ContextPrediction) || features.ContextPrediction is < 0 or > 1 ||
            features.Values.Any(number => !float.IsFinite(number)) || !double.IsFinite(features.Prediction) || features.Prediction is < 0 or > 1))
            throw new ArgumentException("Invalid captured recommendation features.");
    }
}
