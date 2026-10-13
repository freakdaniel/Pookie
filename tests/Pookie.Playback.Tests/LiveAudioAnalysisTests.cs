using Pookie.Recommendations;
using Pookie.Recommendations.Audio;
using Pookie.Recommendations.Storage;
using Xunit;

namespace Pookie.Playback.Tests;

public sealed class LiveAudioAnalysisTests
{
    [Fact]
    public async Task EarlyAudioAnalysisUpdatesTheCacheWithoutInventingListeningOrRewardingTheUser()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pookie-live-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "events.sqlite3");
            var track = new WaveTrack(1, "Fixture", "Artist", 1, "", 180000, null, null, null);
            using (var old = new ListeningEventStore(path))
            {
                var values = new float[200]; values[0] = 1;
                old.PutVector(42, track, new(MusicnnEncoder.ModelId, values, new(.8f, .8f, .1f, .1f)), DateTimeOffset.UtcNow);
            }
            var sample = new AudioSample(Enumerable.Range(0, 48000).Select(i => (float)(.2 * Math.Sin(2 * Math.PI * 220 * i / 16000))).ToArray());
            await using var journal = new ListeningJournal(path);
            Assert.True(await journal.AnalyzeAsync(42, track, sample));
            var snapshot = await journal.SnapshotAsync(42, [track]);
            Assert.True(snapshot.Vectors[1].Mood!.IsCurrent); Assert.Equal(0, snapshot.Model.Samples);
            Assert.Equal(0, snapshot.Model.Sequence); Assert.Empty(snapshot.Model.PositiveAudio);
            using var reader = new ListeningEventStore(path); Assert.Empty(reader.Read(42));
            Assert.True(await journal.AnalyzeAsync(42, track, sample));
            Assert.Equal(snapshot.Vectors[1].Mood, (await journal.SnapshotAsync(42, [track])).Vectors[1].Mood);
        }
        finally { Directory.Delete(directory, true); }
    }
}
