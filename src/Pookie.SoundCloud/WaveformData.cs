using System.Text.Json;

namespace Pookie.SoundCloud;

public static class WaveformData
{
    // Each output bar represents its whole time interval, including quieter samples.
    // Peak pooling makes dense, mastered tracks look flat at narrow display widths.
    public static float[] Resample(ReadOnlySpan<float> samples, int count)
    {
        if (count is < 1 or > 20000) throw new ArgumentOutOfRangeException(nameof(count));
        if (samples.IsEmpty) return [];
        var bars = new float[count];
        var interval = (double)samples.Length / count;
        for (var bar = 0; bar < count; bar++)
        {
            var start = bar * interval;
            var end = (bar + 1) * interval;
            double amplitude = 0;
            for (var index = (int)start; index < Math.Min(samples.Length, (int)Math.Ceiling(end)); index++)
                amplitude += samples[index] * (Math.Min(end, index + 1) - Math.Max(start, index));
            bars[bar] = (float)(amplitude / interval);
        }
        return bars;
    }

    public static float[] Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("samples", out var values) || values.ValueKind != JsonValueKind.Array ||
            values.GetArrayLength() is 0 or > 20000) throw new JsonException("Invalid waveform samples");
        var samples = new float[values.GetArrayLength()];
        float peak = 0;
        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var sample) || !float.IsFinite(sample) || sample < 0 || sample > 1000000)
                throw new JsonException("Invalid waveform amplitude");
            samples[index++] = sample;
            peak = Math.Max(peak, sample);
        }
        var scale = root.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number && height.TryGetSingle(out var declared) &&
            float.IsFinite(declared) && declared > 0 ? Math.Max(peak, declared) : Math.Max(peak, 1);
        for (index = 0; index < samples.Length; index++) samples[index] /= scale;
        return samples;
    }
}
