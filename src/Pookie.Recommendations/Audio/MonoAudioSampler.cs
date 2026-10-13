namespace Pookie.Recommendations.Audio;

// The decoder worker supplies PCM. Readers copy only the published, never modified prefix.
public sealed class MonoAudioSampler
{
    private readonly float[] mono = new float[16000 * 9];
    private readonly int channels;
    private readonly double ratio;
    private double remaining, accumulated;
    private float frame;
    private int channel, count, published, taken;

    public MonoAudioSampler(int sampleRate, int channels)
    {
        if (sampleRate < 16000 || channels is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        this.channels = channels; ratio = sampleRate / 16000d; remaining = ratio;
    }
    public void Append(ReadOnlySpan<float> samples)
    {
        if (count == mono.Length) return;
        foreach (var sample in samples)
        {
            frame += float.IsFinite(sample) ? sample : 0;
            if (++channel < channels) continue;
            var value = frame / channels; frame = 0; channel = 0;
            var available = 1d;
            while (available > 1e-9)
            {
                var used = Math.Min(available, remaining); accumulated += value * used;
                available -= used; remaining -= used;
                if (remaining > 1e-9) continue;
                mono[count++] = (float)(accumulated / ratio); accumulated = 0; remaining = ratio;
                if (count == mono.Length) { Volatile.Write(ref published, count); return; }
            }
        }
        Volatile.Write(ref published, count);
    }
    public AudioSample? Take()
    {
        var available = Volatile.Read(ref published);
        if (available < 48000 || Interlocked.Exchange(ref taken, 1) != 0) return null;
        return new(mono.AsSpan(0, available).ToArray());
    }
}
