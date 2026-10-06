using System.Text.Json;

namespace Pookie.SoundCloud;

public static class WaveformData
{
    // SoundCloud draws one source sample at each bar's time position. Averaging
    // an interval removes the short peaks and dips that define its silhouette.
    public static float[] Resample(ReadOnlySpan<float> samples, int count)
    {
        if (count is < 1 or > 20000) throw new ArgumentOutOfRangeException(nameof(count));
        if (samples.IsEmpty) return [];
        var bars = new float[count];
        for (var bar = 0; bar < count; bar++)
            bars[bar] = samples[(int)((long)bar * samples.Length / count)];
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
        // The website first curves the distance from the top of the waveform,
        // then draws the remaining height. Keep its quantization as well as the
        // nonlinear curve; linear normalization flattens loud, mastered tracks.
        for (index = 0; index < samples.Length; index++)
        {
            var top = Math.Floor(Math.Pow(1 - samples[index] / scale, 2d / 3) * scale + .5);
            samples[index] = (float)Math.Clamp(1 - top / scale, 0, 1);
        }
        return samples;
    }
}
