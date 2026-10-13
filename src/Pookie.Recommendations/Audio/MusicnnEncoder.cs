using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pookie.Recommendations.Audio;

public sealed class MusicnnEncoder : IDisposable
{
    private static readonly string Resource = "Pookie.Recommendations.Models.musicnn-msd";
    private const string ModelHash = "5e828561ea880f6b90d17b01e05a7c92fa5fde14a19a0b11a1700300b0a11dfd";
    public const string ModelId = "musicnn-msd-pcm-v1:" + ModelHash;
    private readonly InferenceSession session;
    private MoodEncoder? moods;

    public MusicnnEncoder()
    {
        if (ReadHash() != ModelHash) throw new InvalidDataException("The embedding manifest has a different model version.");
        using var stream = typeof(MusicnnEncoder).Assembly.GetManifestResourceStream(Resource + ".onnx")
            ?? throw new InvalidOperationException("The music embedding model is missing.");
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(ModelHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The music embedding model checksum differs.");
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, EnableCpuMemArena = false };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
        session = new(bytes, options);
    }

    public AudioVector? Encode(AudioSample sample)
    {
        if (sample.SampleRate != 16000) throw new ArgumentException("The model needs 16 kHz mono PCM.");
        if (sample.Mono.Length > 144000) throw new ArgumentException("The audio analysis budget is nine seconds.");
        if (sample.Mono.Length < 48000) return null;
        double power = 0;
        foreach (var value in sample.Mono) { if (!float.IsFinite(value)) throw new ArgumentException("Non-finite PCM."); power += value * (double)value; }
        if (power / sample.Mono.Length < 1e-8) return null;
        var input = MusicnnInput.Transform(sample.Mono);
        var patches = input.Length / (MusicnnInput.Frames * MusicnnInput.Bands);
        var tensor = new DenseTensor<float>(input, [patches, MusicnnInput.Frames, MusicnnInput.Bands]);
        using var output = session.Run([NamedOnnxValue.CreateFromTensor("model/Placeholder:0", tensor)]);
        var data = output.First().AsTensor<float>().ToArray();
        if (data.Length != patches * AudioVectorCodec.Dimensions) throw new InvalidDataException("Invalid model output.");
        var mean = new float[AudioVectorCodec.Dimensions];
        for (var patch = 0; patch < patches; patch++)
        for (var i = 0; i < mean.Length; i++) mean[i] += data[patch * mean.Length + i] / patches;
        moods ??= new();
        return new(ModelId, AudioVectorCodec.Normalize(mean), moods.Encode(data, patches));
    }

    private static string ReadHash()
    {
        using var stream = typeof(MusicnnEncoder).Assembly.GetManifestResourceStream(Resource + ".json")
            ?? throw new InvalidOperationException("The music embedding manifest is missing.");
        using var manifest = JsonDocument.Parse(stream);
        return manifest.RootElement.GetProperty("onnx_sha256").GetString() ?? throw new InvalidDataException("Model checksum is missing.");
    }
    public void Dispose() { moods?.Dispose(); session.Dispose(); }
}
