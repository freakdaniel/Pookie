using System.Numerics;

namespace Pookie.Recommendations.Audio;

// Slaney mel, 512-point Hann STFT, power bands, log10(1 + 10000 * mel power).
// These are the public preprocessing parameters of MSD MusiCNN, not text features.
public static class MusicnnInput
{
    public const int Frames = 187, Bands = 96, FrameSize = 512, Hop = 256;
    private static readonly double[] Window = Enumerable.Range(0, FrameSize).Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / (FrameSize - 1))).ToArray();
    private static readonly double[][] Filters = MakeFilters();

    public static float[] Transform(ReadOnlySpan<float> mono)
    {
        if (mono.Length < 16000 * 3 || mono.Length > 16000 * 9) throw new ArgumentException("Use three to nine seconds of 16 kHz mono audio.");
        var patches = Math.Min(3, mono.Length / 48000);
        var result = new float[patches * Frames * Bands];
        var fft = new Complex[FrameSize];
        var power = new double[FrameSize / 2 + 1];
        for (var patch = 0; patch < patches; patch++)
        for (var frame = 0; frame < Frames; frame++)
        {
            var start = patch * 48000 + frame * Hop - FrameSize / 2;
            for (var i = 0; i < FrameSize; i++)
            {
                var at = start + i;
                var sample = at >= 0 && at < mono.Length ? mono[at] : 0;
                if (!float.IsFinite(sample)) throw new ArgumentException("Non-finite PCM.");
                fft[i] = new(sample * Window[i], 0);
            }
            Forward(fft);
            for (var i = 0; i < power.Length; i++) power[i] = fft[i].Real * fft[i].Real + fft[i].Imaginary * fft[i].Imaginary;
            for (var band = 0; band < Bands; band++)
            {
                double energy = 0;
                for (var i = 0; i < power.Length; i++) energy += power[i] * Filters[band][i];
                result[(patch * Frames + frame) * Bands + band] = (float)Math.Log10(1 + 10000 * energy);
            }
        }
        return result;
    }

    private static double[][] MakeFilters()
    {
        static double Mel(double hz) => hz < 1000 ? hz / (200d / 3) : 15 + Math.Log(hz / 1000) / (Math.Log(6.4) / 27);
        static double Hertz(double mel) => mel < 15 ? mel * (200d / 3) : 1000 * Math.Exp((mel - 15) * Math.Log(6.4) / 27);
        var edges = Enumerable.Range(0, Bands + 2).Select(i => Hertz(i * Mel(8000) / (Bands + 1))).ToArray();
        return Enumerable.Range(0, Bands).Select(band => Enumerable.Range(0, FrameSize / 2 + 1).Select(bin => {
            var hz = bin * 16000d / FrameSize;
            return Math.Max(0, Math.Min((hz - edges[band]) / (edges[band + 1] - edges[band]),
                (edges[band + 2] - hz) / (edges[band + 2] - edges[band + 1]))) * 2 / (edges[band + 2] - edges[band]);
        }).ToArray()).ToArray();
    }

    private static void Forward(Complex[] data)
    {
        for (int i = 1, j = 0; i < data.Length; i++)
        {
            var bit = data.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var size = 2; size <= data.Length; size <<= 1)
        {
            var angle = -2 * Math.PI / size;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < data.Length; start += size)
            {
                var factor = Complex.One;
                for (var i = 0; i < size / 2; i++)
                {
                    var even = data[start + i]; var odd = data[start + i + size / 2] * factor;
                    data[start + i] = even + odd; data[start + i + size / 2] = even - odd; factor *= step;
                }
            }
        }
    }
}
