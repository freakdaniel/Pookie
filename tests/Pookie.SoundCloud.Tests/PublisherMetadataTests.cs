using System.Text.Json;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class PublisherMetadataTests
{
    [Fact]
    public void OptionalPublisherFieldsSurviveDeserializationWithoutChangingUploader()
    {
        var track = JsonSerializer.Deserialize("""
            {"id":42,"title":"Track","user":{"username":"Uploader"},
             "publisher_metadata":{"artist":"Recording artist","album_title":"Album","isrc":"USXXX2600001"}}
            """, SoundCloudJson.Default.SoundCloudTrack)!;
        Assert.Equal("Uploader", track.Author);
        Assert.Equal("Recording artist", track.PublisherMetadata!.Artist);
        Assert.Equal("Album", track.PublisherMetadata.AlbumTitle);
        Assert.Equal("USXXX2600001", track.PublisherMetadata.Isrc);
        Assert.Null(JsonSerializer.Deserialize("""{"id":43,"title":"Other"}""", SoundCloudJson.Default.SoundCloudTrack)!.PublisherMetadata);
    }
}
