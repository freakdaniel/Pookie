using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class TrackArtTests
{
    [Fact]
    public void ReadsTrackArtIndependentlyOfCoverAndPreservesItThroughSerialization()
    {
        using var json = JsonDocument.Parse("""
            [{"id":2088492891,"title":"Track Art","artwork_url":"https://i1.sndcdn.com/cover-large.png",
              "visuals":{"urn":"soundcloud:tracks:2088492891","enabled":true,"visuals":[
                {"entry_time":0,"visual_url":"https://i1.sndcdn.com/visuals-original.jpg"}],"tracking":null}}]
            """);
        var track = Assert.Single(SoundCloudWebClient.ParsePage(json.RootElement).Tracks);
        Assert.Equal("https://i1.sndcdn.com/visuals-original.jpg", track.TrackArtUrl);
        Assert.Equal("https://i1.sndcdn.com/cover-large.png", track.ArtworkUrl);
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(track, SoundCloudJson.Default.SoundCloudTrack), SoundCloudJson.Default.SoundCloudTrack);
        Assert.Equal(track.TrackArtUrl, restored!.TrackArtUrl);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"enabled\":null,\"visuals\":[]}")]
    [InlineData("{\"enabled\":false,\"visuals\":[{\"visual_url\":\"https://i1.sndcdn.com/art.jpg\"}]}")]
    [InlineData("{\"enabled\":true,\"visuals\":null}")]
    [InlineData("{\"enabled\":true,\"visuals\":[]}")]
    [InlineData("{\"enabled\":true,\"visuals\":[null,{\"visual_url\":\" \"}]}")]
    [InlineData("{\"enabled\":true,\"visuals\":[{\"entry_time\":10000,\"visual_url\":\"https://i1.sndcdn.com/art.jpg\"}]}")]
    public void MissingDisabledOrTimedVisualsDoNotBecomeAStaticBackdrop(string visuals)
    {
        using var json = JsonDocument.Parse("[{\"id\":1,\"title\":\"Track\",\"visuals\":" + visuals + "}]");
        Assert.Null(Assert.Single(SoundCloudWebClient.ParsePage(json.RootElement).Tracks).TrackArtUrl);
    }
}
