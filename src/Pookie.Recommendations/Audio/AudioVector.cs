using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Pookie.Recommendations.Audio;

public sealed record AudioSample(float[] Mono, int SampleRate = 16000);
public sealed record AudioVector(string Model, float[] Values, AudioMood? Mood = null);

public static class AudioVectorCodec
{
    public const int Dimensions = 200;
    public static float[] Normalize(ReadOnlySpan<float> values)
    {
        if (values.Length != Dimensions) throw new ArgumentException("Unexpected audio vector dimensions.");
        double length = 0;
        foreach (var value in values)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Non-finite audio vector.");
            length += value * (double)value;
        }
        if (length < 1e-16) throw new ArgumentException("An audio vector cannot be zero.");
        var result = values.ToArray();
        var scale = (float)(1 / Math.Sqrt(length));
        for (var i = 0; i < result.Length; i++) result[i] *= scale;
        return result;
    }
    public static byte[] Encode(ReadOnlySpan<float> vector)
    {
        var normalized = Normalize(vector);
        var bytes = new byte[Dimensions * sizeof(float)];
        for (var i = 0; i < Dimensions; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), normalized[i]);
        return bytes;
    }
    public static float[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Dimensions * 4) throw new InvalidDataException("Invalid audio vector BLOB.");
        var values = new float[Dimensions];
        for (var i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(i * 4)..]);
        return Normalize(values);
    }
    public static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != Dimensions || right.Length != Dimensions) throw new ArgumentException("Vector dimensions differ.");
        double score = 0;
        for (var i = 0; i < Dimensions; i++) score += left[i] * (double)right[i];
        return Math.Clamp(score, -1, 1);
    }
    public static double BlobDot(byte[] left, byte[] right)
        => BlobDotCore(left, right);

    public static double BlobMaxDot(byte[] vector, byte[] centers)
    {
        const int bytes = Dimensions * 4;
        if (centers.Length is < bytes or > bytes * 15 || centers.Length % bytes != 0)
            throw new InvalidDataException("Invalid audio search centers.");
        var best = -1d;
        for (var at = 0; at < centers.Length; at += bytes)
            best = Math.Max(best, BlobDotCore(vector, centers.AsSpan(at, bytes)));
        return best;
    }

    private static double BlobDotCore(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != Dimensions * 4 || right.Length != Dimensions * 4) throw new InvalidDataException("Invalid vector BLOB.");
        double score = 0;
        if (BitConverter.IsLittleEndian)
        {
            var a = MemoryMarshal.Cast<byte, float>(left); var b = MemoryMarshal.Cast<byte, float>(right);
            var sum = Vector<float>.Zero; var at = 0;
            for (; at <= Dimensions - Vector<float>.Count; at += Vector<float>.Count)
                sum += new Vector<float>(a.Slice(at)) * new Vector<float>(b.Slice(at));
            score = Vector.Sum(sum);
            for (; at < Dimensions; at++) score += a[at] * (double)b[at];
        }
        else
            for (var i = 0; i < Dimensions; i++) score += BinaryPrimitives.ReadSingleLittleEndian(left[(i * 4)..]) *
                (double)BinaryPrimitives.ReadSingleLittleEndian(right[(i * 4)..]);
        if (!double.IsFinite(score)) throw new InvalidDataException("Non-finite vector BLOB.");
        return Math.Clamp(score, -1, 1);
    }
}
