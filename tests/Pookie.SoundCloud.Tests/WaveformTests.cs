using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class WaveformTests
{
    [Fact]
    public void BarsUseIndividualTimeSamplesInsteadOfAveragingPeaksAndDips()
    {
        Assert.Equal(new[] { .1f, .2f, .3f }, WaveformData.Resample([.1f, .9f, .2f, .8f, .3f, .7f], 3));
        Assert.Equal(new[] { 0f, 0f, 0f }, WaveformData.Resample([0, 0, 0, 0, 0, 0], 3));
    }

    [Fact]
    public void FractionalWidthsAndUpsamplingRetainSourceTimePositions()
    {
        Assert.Equal(new[] { 0f, 1f }, WaveformData.Resample([0, 1, 0], 2));
        Assert.Equal(new[] { 0f, 0f, 1f, 1f }, WaveformData.Resample([0, 1], 4));
        Assert.Equal(new[] { 0f, .4f, 1f }, WaveformData.Resample([0, .4f, 1], 3));
    }

    [Fact]
    public void SamplesUseSoundCloudsNonlinearHeightCurveAndPreserveSilence()
    {
        using var document = JsonDocument.Parse("{\"height\":140,\"samples\":[0,35,70,140]}");
        var samples = WaveformData.Parse(document.RootElement);
        Assert.Equal(0, samples[0]); Assert.Equal(1, samples[3]);
        Assert.Equal(24f / 140, samples[1], 6);
        Assert.Equal(52f / 140, samples[2], 6);
    }

    [Fact]
    public void MissingOrUnderreportedHeightCannotProduceOversizedBars()
    {
        foreach (var raw in new[] { "{\"samples\":[0,10,20]}", "{\"height\":5,\"samples\":[0,10,20]}" })
        {
            using var document = JsonDocument.Parse(raw);
            Assert.Equal(new float[] { 0, .35f, 1 }, WaveformData.Parse(document.RootElement));
        }
    }

    [Fact]
    public void RealTrackMatchesWebsiteHeightsAtNarrowAndFullWidths()
    {
        using var stream = typeof(WaveformTests).Assembly.GetManifestResourceStream("Pookie.TestFixtures.MalchikWaveform.json")!;
        using var document = JsonDocument.Parse(stream);
        var samples = WaveformData.Parse(document.RootElement);
        Assert.Equal(1800, samples.Length);
        Assert.Equal(15f / 140, samples[0], 6);
        Assert.Equal(125f / 140, samples[300], 6);
        Assert.Equal(9f / 140, samples[^1], 6);
        var bars = WaveformData.Resample(samples, 216);
        int[] positions = [0, 1, 2, 3, 4, 5, 20, 50, 100, 150, 200, 215];
        int[] heights = [15, 67, 68, 48, 68, 68, 98, 94, 100, 63, 118, 39];
        for (var index = 0; index < positions.Length; index++)
            Assert.Equal(heights[index] / 140f, bars[positions[index]], 6);
        Assert.Equal(samples, WaveformData.Resample(samples, 1800));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"samples\":[]}")]
    [InlineData("{\"samples\":[-1]}")]
    [InlineData("{\"samples\":[\"bad\"]}")]
    [InlineData("{\"samples\":[1000001]}")]
    public void InvalidSamplesAreRejected(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        Assert.Throws<JsonException>(() => WaveformData.Parse(document.RootElement));
    }

    [Fact]
    public void ListMetadataSurvivesTrackDeserialization()
    {
        var track = JsonSerializer.Deserialize("""
            {"id":42,"title":"Track","waveform_url":"https://wave.sndcdn.com/test_m.json",
            "created_at":"2026-10-03T12:00:00Z","genre":"Ambient","playback_count":1234,"likes_count":56,"comment_count":7}
            """, SoundCloudJson.Default.SoundCloudTrack)!;
        Assert.Equal("https://wave.sndcdn.com/test_m.json", track.WaveformUrl);
        Assert.Equal(1234, track.PlaybackCount);
        Assert.Equal(56, track.LikesCount);
        Assert.Equal(7, track.CommentCount);
        Assert.Equal("Ambient", track.Genre);
    }
}
