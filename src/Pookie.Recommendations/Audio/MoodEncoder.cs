using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pookie.Recommendations.Audio;

// Shares the raw embedding output with MusiCNN. Never feed the normalised search vector to this head.
internal sealed class MoodEncoder : IDisposable
{
    private readonly InferenceSession session;
    internal MoodEncoder()
    {
        const string resource = "Pookie.Recommendations.Models.musicnn-moods";
        using var manifestStream = typeof(MoodEncoder).Assembly.GetManifestResourceStream(resource + ".json")
            ?? throw new InvalidOperationException("The mood manifest is missing.");
        using var manifest = JsonDocument.Parse(manifestStream);
        if (manifest.RootElement.GetProperty("analysis_version").GetString() != AudioMood.AnalysisVersion)
            throw new InvalidDataException("The mood analysis version differs.");
        using var stream = typeof(MoodEncoder).Assembly.GetManifestResourceStream(resource + ".onnx")
            ?? throw new InvalidOperationException("The mood model is missing.");
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(manifest.RootElement.GetProperty("onnx_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The mood model checksum differs.");
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, EnableCpuMemArena = false };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
        session = new(bytes, options);
    }
    internal AudioMood Encode(float[] raw, int patches)
    {
        using var output = session.Run([NamedOnnxValue.CreateFromTensor("embedding:0", new DenseTensor<float>(raw, [patches, 200]))]);
        var values = output.First().AsTensor<float>().ToArray();
        if (values.Length != patches * 6 || values.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Invalid mood model output.");
        var mean = new float[6];
        for (var patch = 0; patch < patches; patch++)
        for (var i = 0; i < 6; i++) mean[i] += values[patch * 6 + i] / patches;
        // DEAM reports valence/arousal on [1,9], not Softmax probabilities.
        var result = new AudioMood(mean[0], Math.Clamp((mean[5] - 1) / 8, 0, 1), mean[2], mean[3],
            Math.Clamp((mean[4] - 1) / 8, 0, 1), mean[1], AudioMood.AnalysisVersion);
        result.Validate(); return result;
    }
    public void Dispose() => session.Dispose();
}
