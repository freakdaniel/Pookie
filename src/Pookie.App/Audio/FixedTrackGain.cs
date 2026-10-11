namespace Pookie.Audio;

// The track correction is chosen before playback and never learned from PCM.
// A linked sample-peak limiter only protects boosted transients from clipping.
internal sealed class FixedTrackGain
{
    private const double Ceiling = .8413951416451951;
    private readonly int channels;
    private readonly double gain;
    private readonly double release;
    private double limiter = 1;
    public double GainDb { get; }

    public FixedTrackGain(int sampleRate, int channels, double gainDb)
    {
        if (!double.IsFinite(gainDb) || gainDb is < -24 or > 6) throw new ArgumentOutOfRangeException(nameof(gainDb));
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        this.channels = channels;
        GainDb = gainDb; gain = Math.Pow(10, gainDb / 20);
        release = 1 - Math.Exp(-128d / (sampleRate * .5));
    }
    public void Apply(Span<float> pcm)
    {
        if (pcm.Length % channels != 0) throw new ArgumentException("Incomplete PCM frame.", nameof(pcm));
        if (gain <= 1)
        {
            for (var i = 0; i < pcm.Length; i++) pcm[i] = float.IsFinite(pcm[i]) ? pcm[i] * (float)gain : 0;
            return;
        }
        for (var start = 0; start < pcm.Length; start += 128 * channels)
        {
            var end = Math.Min(pcm.Length, start + 128 * channels);
            double peak = 0;
            for (var i = start; i < end; i++)
            {
                if (!float.IsFinite(pcm[i])) pcm[i] = 0;
                peak = Math.Max(peak, Math.Abs(pcm[i]));
            }
            var limit = peak == 0 ? 1 : Math.Min(1, Ceiling / (peak * gain));
            limiter = limit < limiter ? limit : limiter + (limit - limiter) * release;
            var multiplier = (float)(gain * limiter);
            for (var i = start; i < end; i++) pcm[i] *= multiplier;
        }
    }
}
