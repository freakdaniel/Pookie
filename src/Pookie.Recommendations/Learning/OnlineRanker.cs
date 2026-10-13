using Pookie.Recommendations.Audio;

namespace Pookie.Recommendations.Learning;

public sealed record FeatureSnapshot(string Version, long ModelSequence, float[] Values, double Prediction,
    WaveMood Mood = WaveMood.Auto, float[]? ContextValues = null, double ContextPrediction = .5);
public sealed record Affinity(double Positive, double Negative);
public sealed record RewardQuota(DateOnly Day, int Count);
public sealed record SkipEvidence(int Sessions, Guid LastSession);
public sealed record RankerState(long Sequence, int Samples, float[] Weights, float[] Gradients,
    Dictionary<string, Affinity> Affinities, Dictionary<long, RewardQuota> Rewards,
    float[][] PositiveAudio, float[]? NegativeAudio, float[]? RecentAudio, double PredictionError, string? AudioModel = null,
    Dictionary<WaveMood, RankerState>? Contexts = null, Dictionary<long, SkipEvidence>? Skips = null,
    Guid? Session = null, float[]? SessionNegativeAudio = null, float[][]? NegativeAudioGroups = null)
{
    public static RankerState Empty() => new(0, 0, new float[OnlineRanker.Dimensions], new float[OnlineRanker.Dimensions],
        [], [], [], null, null, 0);
}

// A small adaptive logistic model. State is copied for publication; only the journal worker learns.
public sealed class OnlineRanker(RankerState? state = null)
{
    public const string FeatureVersion = "wave-features-v2";
    public const int Dimensions = 10;
    private RankerState current = state ?? RankerState.Empty();
    public RankerState Snapshot() => current with { Weights = (float[])current.Weights.Clone(), Gradients = (float[])current.Gradients.Clone(),
        Affinities = new(current.Affinities), Rewards = new(current.Rewards), PositiveAudio = current.PositiveAudio.Select(v => (float[])v.Clone()).ToArray(),
        NegativeAudio = current.NegativeAudio?.ToArray(), RecentAudio = current.RecentAudio?.ToArray(),
        SessionNegativeAudio = current.SessionNegativeAudio?.ToArray(), NegativeAudioGroups = current.NegativeAudioGroups?.Select(v => v.ToArray()).ToArray(),
        Skips = current.Skips == null ? null : new(current.Skips),
        Contexts = current.Contexts?.ToDictionary(pair => pair.Key, pair => new OnlineRanker(pair.Value).Snapshot()) };

    public FeatureSnapshot Evaluate(WaveTrack track, AudioVector? audio, bool liked = false, WaveMood mood = WaveMood.Auto, Guid? session = null)
    {
        var features = EvaluateBase(track, audio, liked, session);
        if (mood == WaveMood.Auto || current.Contexts?.GetValueOrDefault(mood) is not { } context)
            return features with { Mood = mood };
        var contextual = new OnlineRanker(context).EvaluateBase(track, audio, liked, session);
        return features with { Mood = mood, ContextValues = contextual.Values, ContextPrediction = contextual.Prediction };
    }

    // Mood selection biases a personalised score. Unknown mood stays neutral, never excludes a song.
    public double Score(WaveTrack track, AudioVector? audio, bool liked = false, WaveMood mood = WaveMood.Auto, Guid? session = null)
    {
        var features = Evaluate(track, audio, liked, mood, session);
        var context = current.Contexts?.GetValueOrDefault(mood);
        var trust = context == null ? 0 : .3 * Math.Min(1, context.Samples / 12d);
        var score = features.Prediction * (1 - trust) + features.ContextPrediction * trust;
        if (audio?.Model == MusicnnEncoder.ModelId && audio.Mood is { } descriptor && mood != WaveMood.Auto)
            score += .16 * (descriptor.Match(mood) - .5);
        var active = context ?? current;
        var sessionNegative = session == null || active.Session == session ? active.SessionNegativeAudio : null;
        if (audio != null && audio.Model == current.AudioModel && sessionNegative != null)
            score -= .05 * Math.Max(0, AudioVectorCodec.Dot(sessionNegative, audio.Values));
        return Math.Clamp(score, 0, 1);
    }

    private FeatureSnapshot EvaluateBase(WaveTrack track, AudioVector? audio, bool liked, Guid? session = null)
    {
        if (audio?.Model != current.AudioModel) audio = null;
        float Preference(string key) => current.Affinities.TryGetValue(key, out var affinity)
            ? (float)((affinity.Positive - affinity.Negative) / (2 + affinity.Positive + affinity.Negative)) : 0;
        float Similarity(float[]? centroid) => audio != null && centroid != null ? (float)AudioVectorCodec.Dot(centroid, audio.Values) : 0;
        var values = new float[] { 1, (float)Math.Clamp(track.DurationSeconds / 600, 0, 1),
            Preference("artist:" + WaveTaste.ArtistKey(track)), Preference("genre:" + track.Genre.Trim().ToLowerInvariant()),
            audio == null || current.PositiveAudio.Length == 0 ? 0 : current.PositiveAudio.Max(v => Similarity(v)),
            current.NegativeAudioGroups is { Length: > 0 } groups ? groups.Max(v => Similarity(v)) : Similarity(current.NegativeAudio),
            session == null || current.Session == session ? Similarity(current.RecentAudio) : 0, liked ? 1 : 0,
            audio != null ? 1 : 0, Math.Min(1, current.Samples / 100f) };
        double score = 0;
        for (var i = 0; i < Dimensions; i++) score += current.Weights[i] * values[i];
        return new(FeatureVersion, current.Sequence, values, 1 / (1 + Math.Exp(-Math.Clamp(score, -20, 20))));
    }

    public void Observe(long sequence, WaveEvent item, AudioVector? audio)
        => Observe(sequence, item, audio, contextual: false);

    private void Observe(long sequence, WaveEvent item, AudioVector? audio, bool contextual)
    {
        if (sequence <= current.Sequence) return;
        current = current with { Sequence = sequence };
        var track = item.Track;
        if (track == null) return;
        // Explicit rejection cannot also become a reward for having listened long enough.
        if (item.Listening?.EndReason == ListeningEndReason.Rejected) return;
        if (audio != null && audio.Model != current.AudioModel)
            current = current with { AudioModel = audio.Model, PositiveAudio = [], NegativeAudio = null, RecentAudio = null,
                NegativeAudioGroups = null, SessionNegativeAudio = null };

        if (!contextual && item.Mood != WaveMood.Auto)
        {
            var contexts = current.Contexts ?? [];
            var learner = new OnlineRanker(contexts.GetValueOrDefault(item.Mood));
            var capturedContext = item.Listening?.Features;
            var contextFeatures = capturedContext?.ContextValues == null ? null : new FeatureSnapshot(FeatureVersion, capturedContext.ModelSequence,
                capturedContext.ContextValues, capturedContext.ContextPrediction);
            var contextualEvent = item with { Mood = WaveMood.Auto, Listening = item.Listening is { } contextPlay
                ? contextPlay with { Mood = WaveMood.Auto, Features = contextFeatures } : null };
            learner.Observe(sequence, contextualEvent, audio, contextual: true);
            contexts[item.Mood] = learner.Snapshot(); current = current with { Contexts = contexts };
        }
        if (item.Listening is { } sessionPlay && current.Session != sessionPlay.SessionId)
            current = current with { Session = sessionPlay.SessionId, RecentAudio = null, SessionNegativeAudio = null };
        double target, confidence;
        if (item.Kind == WaveEventKind.Like) { target = 1; confidence = 2; }
        else if (item.Kind == WaveEventKind.Block) { target = 0; confidence = 2; }
        else if (item.Listening is { } play)
        {
            if (play.EarlySkip)
            {
                if (audio != null) current = current with { SessionNegativeAudio = Blend(current.SessionNegativeAudio, audio.Values, .2f) };
                if (!contextual && (item.Mood != WaveMood.Auto || play.Features?.Values.ElementAtOrDefault(7) == 1)) return;
                var skips = current.Skips ?? [];
                var previous = skips.GetValueOrDefault(track.Id);
                var sessions = Math.Min(3, (previous?.Sessions ?? 0) + (previous?.LastSession != play.SessionId ? 1 : 0));
                skips[track.Id] = new(sessions, play.SessionId);
                if (skips.Count > 128) skips.Remove(skips.Keys.First());
                current = current with { Skips = skips };
                target = 0; confidence = contextual ? .25 : .05 + .1 * (sessions - 1);
            }
            else if (play.ListenedSeconds >= 15 && play.ListenedFraction >= .6) { target = 1; confidence = .7; }
            else if (play.Selection == PlaybackSelection.TrackSelection && play.ListenedSeconds >= 30) { target = 1; confidence = .25; }
            else return;
        }
        else return; // Unlike and reset are state changes, not evidence of disliking the music.

        var day = DateOnly.FromDateTime(item.OccurredAt.UtcDateTime);
        var quota = current.Rewards.GetValueOrDefault(track.Id);
        var count = quota?.Day == day ? quota.Count : 0;
        if (item.Kind == WaveEventKind.Listening && count >= 3) return;
        current.Rewards[track.Id] = new(day, count + 1);
        if (current.Rewards.Count > 256) current.Rewards.Remove(current.Rewards.MinBy(pair => pair.Value.Day).Key);
        var input = item.Listening?.Features is { Version: FeatureVersion or "wave-features-v1" } captured ? captured : EvaluateBase(track, audio, false);
        if (input.Values.Length != Dimensions || input.Values.Any(value => !float.IsFinite(value)) ||
            !double.IsFinite(input.Prediction) || input.Prediction is < 0 or > 1) throw new InvalidDataException("Invalid training features.");
        var error = target - input.Prediction;
        for (var i = 0; i < Dimensions; i++)
        {
            var gradient = confidence * error * input.Values[i] - .001 * current.Weights[i];
            current.Gradients[i] += (float)(gradient * gradient);
            current.Weights[i] = Math.Clamp(current.Weights[i] + (float)(.1 * gradient / Math.Sqrt(.01 + current.Gradients[i])), -5, 5);
        }
        foreach (var key in new[] { "artist:" + WaveTaste.ArtistKey(track), "genre:" + track.Genre.Trim().ToLowerInvariant() })
        {
            var previous = current.Affinities.GetValueOrDefault(key) ?? new(0, 0);
            current.Affinities[key] = new(Math.Min(100, previous.Positive * .995 + (target == 1 ? confidence : 0)),
                Math.Min(100, previous.Negative * .995 + (target == 0 ? confidence : 0)));
        }
        while (current.Affinities.Count > 512) current.Affinities.Remove(current.Affinities.MinBy(pair => pair.Value.Positive + pair.Value.Negative).Key);
        if (audio != null)
        {
            if (target == 1)
            {
                current = current with { PositiveAudio = UpdateCenters(current.PositiveAudio, audio.Values), RecentAudio = Blend(current.RecentAudio, audio.Values, .2f) };
            }
            else current = current with { NegativeAudioGroups = UpdateCenters(current.NegativeAudioGroups ??
                (current.NegativeAudio == null ? [] : [current.NegativeAudio]), audio.Values) };
        }
        current = current with { Samples = current.Samples + 1,
            PredictionError = current.Samples == 0 ? error * error : current.PredictionError * .99 + .01 * error * error };
    }

    private static float[][] UpdateCenters(float[][] existing, float[] values)
    {
        var centers = existing.ToList();
        var best = centers.Count == 0 ? -1 : Enumerable.Range(0, centers.Count).MaxBy(i => AudioVectorCodec.Dot(centers[i], values));
        if (best < 0 || centers.Count < 3 && AudioVectorCodec.Dot(centers[best], values) < .9) centers.Add(values.ToArray());
        else centers[best] = Blend(centers[best], values, .03f);
        return centers.ToArray();
    }

    private static float[] Blend(float[]? previous, float[] next, float rate)
    {
        if (previous == null) return next.ToArray();
        var values = new float[AudioVectorCodec.Dimensions];
        for (var i = 0; i < values.Length; i++) values[i] = previous[i] * (1 - rate) + next[i] * rate;
        return AudioVectorCodec.Normalize(values);
    }
}
