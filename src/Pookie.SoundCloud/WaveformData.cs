using System.Text.Json;

namespace Pookie.SoundCloud;

public static class WaveformData
{
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
