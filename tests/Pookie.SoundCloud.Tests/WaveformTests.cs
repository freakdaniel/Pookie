using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class WaveformTests
{
    [Fact]
    public void SamplesUseDeclaredHeightAndPreserveSilence()
    {
        using var document = JsonDocument.Parse("{\"height\":140,\"samples\":[0,35,70,140]}");
        Assert.Equal(new float[] { 0, .25f, .5f, 1 }, WaveformData.Parse(document.RootElement));
    }

    [Fact]
    public void MissingOrUnderreportedHeightCannotProduceOversizedBars()
    {
        foreach (var raw in new[] { "{\"samples\":[0,10,20]}", "{\"height\":5,\"samples\":[0,10,20]}" })
        {
            using var document = JsonDocument.Parse(raw);
            Assert.Equal(new float[] { 0, .5f, 1 }, WaveformData.Parse(document.RootElement));
        }
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
