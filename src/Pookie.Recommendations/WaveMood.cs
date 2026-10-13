namespace Pookie.Recommendations;

public enum WaveMood { Auto, Calm, Energetic, Melancholic, Aggressive }

// Independent probabilities: an energetic song may also be melancholic.
public sealed record AudioMood(float Calm, float Energetic, float Melancholic, float Aggressive,
    float? Valence = null, float? Party = null, string? Model = null)
{
    public const string AnalysisVersion = "musicnn-moods-arousal-v2";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCurrent => Model == AnalysisVersion && Valence.HasValue && Party.HasValue;
    public double Match(WaveMood mood) => mood switch {
        WaveMood.Calm => IsCurrent ? .65 * Calm + .35 * (1 - Energetic) : Calm,
        // Arousal drives energy. A relaxed dance/party song is not automatically energising.
        WaveMood.Energetic => IsCurrent ? (.85 * Energetic + .1 * Aggressive + .05 * Party!.Value) * (1 - .35 * Calm) : .5,
        WaveMood.Melancholic => IsCurrent ? .7 * Melancholic + .3 * (1 - Valence!.Value) : Melancholic,
        WaveMood.Aggressive => IsCurrent ? .8 * Aggressive + .2 * Energetic : Aggressive, _ => .5
    };
    public void Validate()
    {
        if (new[] { Calm, Energetic, Melancholic, Aggressive, Valence ?? .5f, Party ?? .5f }.Any(value => !float.IsFinite(value) || value is < 0 or > 1))
            throw new InvalidDataException("Invalid mood probabilities.");
    }
}
