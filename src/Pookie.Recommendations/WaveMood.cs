namespace Pookie.Recommendations;

public enum WaveMood { Auto, Calm, Energetic, Melancholic, Aggressive }

// Independent probabilities: an energetic song may also be melancholic.
public sealed record AudioMood(float Calm, float Energetic, float Melancholic, float Aggressive)
{
    public double Match(WaveMood mood) => mood switch {
        WaveMood.Calm => Calm, WaveMood.Energetic => Energetic,
        WaveMood.Melancholic => Melancholic, WaveMood.Aggressive => Aggressive, _ => .5
    };
    public void Validate()
    {
        if (new[] { Calm, Energetic, Melancholic, Aggressive }.Any(value => !float.IsFinite(value) || value is < 0 or > 1))
            throw new InvalidDataException("Invalid mood probabilities.");
    }
}
