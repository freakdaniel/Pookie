using System.Text.Json;

namespace Pookie.SoundCloud;

// A fixed, whole-track envelope estimate. Waveform pixels do not contain
// frequency information or calibrated PCM levels, so this is NOT measured LUFS.
public static class WaveformNormalization
{
    public static double EstimateGainDb(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("height", out var height) || height.ValueKind != JsonValueKind.Number ||
            !height.TryGetDouble(out var scale) || !double.IsFinite(scale) || scale is <= 0 or > 1000000 ||
            !root.TryGetProperty("samples", out var values) || values.ValueKind != JsonValueKind.Array ||
            values.GetArrayLength() is 0 or > 20000) throw new JsonException("Invalid waveform envelope.");
        var powers = new double[values.GetArrayLength()];
        double energy = 0;
        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var sample) || !double.IsFinite(sample) || sample < 0 || sample > scale)
                throw new JsonException("Invalid waveform amplitude.");
            var amplitude = sample / scale;
            powers[index++] = amplitude * amplitude;
            energy += amplitude * amplitude;
        }
        // Exclude silence and sections >10 dB below the initial envelope mean.
        // Use the original values, before the UI's nonlinear height transform.
        var gate = Math.Max(.0001, energy / powers.Length * .1);
        energy = 0; var count = 0;
        foreach (var power in powers) if (power >= gate) { energy += power; count++; }
        if (count == 0) return 0;
        const double referenceEnvelope = .25;
        return Math.Clamp(20 * Math.Log10(referenceEnvelope / Math.Sqrt(energy / count)), -24, 6);
    }
}
