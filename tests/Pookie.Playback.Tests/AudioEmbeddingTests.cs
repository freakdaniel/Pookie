using System.Diagnostics;
using System.Text.Json;
using Pookie.Recommendations;
using Pookie.Recommendations.Audio;
using Pookie.Recommendations.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Pookie.Playback.Tests;

public sealed class AudioEmbeddingTests(ITestOutputHelper output)
{
    private static AudioSample Sample() => new(Enumerable.Range(0, 48000).Select(i =>
        (float)(.2 * Math.Sin(2 * Math.PI * 220 * i / 16000) + .1 * Math.Sin(2 * Math.PI * 660 * i / 16000))).ToArray());

    [Fact]
    public void MelFrontendAndEmbeddingMatchTheIndependentNumpyReference()
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "musicnn-reference.json")));
        var expectedFrame = reference.RootElement.GetProperty("frame30").EnumerateArray().Select(item => item.GetSingle()).ToArray();
        var expectedEmbedding = reference.RootElement.GetProperty("embedding").EnumerateArray().Select(item => item.GetSingle()).ToArray();
        var sample = Sample(); var input = MusicnnInput.Transform(sample.Mono);
        for (var i = 0; i < 96; i++) Assert.InRange(Math.Abs(input[30 * 96 + i] - expectedFrame[i]), 0, 1e-5);
        var watch = Stopwatch.StartNew();
        using var encoder = new MusicnnEncoder(); var vector = encoder.Encode(sample)!;
        output.WriteLine($"Cold 3-second embedding: {watch.Elapsed.TotalMilliseconds:F1} ms; {vector.Values.Length * 4} bytes");
        Assert.Equal(MusicnnEncoder.ModelId, vector.Model); Assert.Equal(200, vector.Values.Length);
        Assert.InRange(AudioVectorCodec.Dot(expectedEmbedding, vector.Values), .99999, 1);
        var expectedMood = reference.RootElement.GetProperty("moods").EnumerateArray().Select(item => item.GetSingle()).ToArray();
        Assert.NotNull(vector.Mood);
        Assert.True(vector.Mood.IsCurrent);
        expectedMood[4] = Math.Clamp((expectedMood[4] - 1) / 8, 0, 1);
        expectedMood[5] = Math.Clamp((expectedMood[5] - 1) / 8, 0, 1);
        var actualMood = new[] { vector.Mood.Calm, vector.Mood.Party!.Value, vector.Mood.Melancholic,
            vector.Mood.Aggressive, vector.Mood.Valence!.Value, vector.Mood.Energetic };
        for (var i = 0; i < 6; i++) Assert.InRange(Math.Abs(actualMood[i] - expectedMood[i]), 0, 2e-5);
        watch.Restart(); var repeat = encoder.Encode(sample)!;
        output.WriteLine($"Warm 3-second embedding: {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.InRange(AudioVectorCodec.Dot(vector.Values, repeat.Values), .999999, 1);
        Assert.Null(encoder.Encode(new(new float[48000])));
    }

    [Theory]
    [InlineData(16000, 1)]
    [InlineData(22050, 1)]
    [InlineData(48000, 2)]
    public void PcmCaptureIsBoundedAndHandlesSplitStereoFrames(int rate, int channels)
    {
        var sampler = new MonoAudioSampler(rate, channels);
        var signal = Enumerable.Repeat(.25f, rate * channels * 12).ToArray();
        for (var at = 0; at < signal.Length; at += 4095) sampler.Append(signal.AsSpan(at, Math.Min(4095, signal.Length - at)));
        var sample = sampler.Take()!;
        Assert.Equal(16000, sample.SampleRate); Assert.Equal(144000, sample.Mono.Length);
        Assert.All(sample.Mono, value => Assert.InRange(value, .24999f, .25001f));
        Assert.Null(sampler.Take());
    }

    [Fact]
    public async Task NeuralVectorAndLearnerCheckpointPersistThroughTheBackgroundJournal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pookie-neural-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "events.sqlite3");
        try
        {
            var track = new WaveTrack(1, "Audio fixture", "Artist", 1, "", 180000, null, null, null);
            var item = new WaveEvent(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, WaveEventKind.Like, track);
            await using (var journal = new ListeningJournal(path))
            {
                await journal.AppendAsync(item, Sample());
                var snapshot = await journal.SnapshotAsync(42, [track]);
                Assert.Equal(1, snapshot.Model.Samples); Assert.Single(snapshot.Model.PositiveAudio);
                Assert.Single(snapshot.Vectors); Assert.Equal(1, Assert.Single(snapshot.Neighbors).Track.Id);
                Assert.True(Assert.Single(snapshot.Ranking).Score > .5);
                await journal.AppendAsync(item, Sample());
                Assert.Equal(1, (await journal.SnapshotAsync(42, [track])).Model.Samples);
            }
            using var store = new ListeningEventStore(path);
            Assert.NotNull(store.GetVector(42, 1)?.Mood); Assert.Equal(1, store.Snapshot(42, []).Model.Samples);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailedAudioAnalysisStillCommitsTheListeningEvent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pookie-invalid-pcm-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "events.sqlite3");
        try
        {
            await using var journal = new ListeningJournal(path);
            var failures = 0; journal.AnalysisFailed += _ => failures++;
            var track = new WaveTrack(1, "Fixture", "", 0, "", 180000, null, null, null);
            await journal.AppendAsync(new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, WaveEventKind.Like, track), new AudioSample(new float[48000], 12345));
            using var store = new ListeningEventStore(path);
            Assert.Equal(1, failures); Assert.Single(store.Read(42)); Assert.Null(store.GetVector(42, 1));
        }
        finally { Directory.Delete(directory, true); }
    }
}
