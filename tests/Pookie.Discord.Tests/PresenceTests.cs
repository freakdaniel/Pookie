using Xunit;

namespace Pookie.Discord.Tests;

public sealed class PresenceTests
{
    [Fact]
    public void PlaybackUsesTrackMetadataAndSeekAdjustedTimestamps()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var presence = PresenceService.CreatePresence(new("Трек", "Исполнитель", "https://i1.sndcdn.com/art.jpg",
            "https://soundcloud.com/artist/song", 40, 180, true, "https://soundcloud.com/artist"), now)!;
        Assert.Equal(PresenceActivityType.Listening, presence.Type);
        Assert.Equal("Трек", presence.Details);
        Assert.Equal("Исполнитель", presence.State);
        Assert.Equal(now.AddSeconds(-40), presence.Timestamps!.Start);
        Assert.Equal(now.AddSeconds(140), presence.Timestamps.End);
        Assert.Equal("https://soundcloud.com/artist/song", presence.DetailsUrl);
        Assert.Equal("https://soundcloud.com/artist", presence.StateUrl);
        Assert.Equal(presence.DetailsUrl, presence.Assets.LargeImageUrl);
        Assert.Equal("https://soundcloud.com", presence.Assets.SmallImageUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)", "https://evil.test/art.jpg")]
    [InlineData("https://soundcloud.com.evil.test/song", "https://i1.sndcdn.com:8443/art.jpg")]
    [InlineData("https://user@soundcloud.com/song", "http://i1.sndcdn.com/art.jpg")]
    public void DiscordLinksAndArtworkRejectForeignOrUnsafeUrls(string url, string artwork)
    {
        var presence = PresenceService.CreatePresence(new("Song", "Artist", artwork, url, 0, 10, true, url), DateTime.UtcNow)!;
        Assert.Null(presence.DetailsUrl);
        Assert.Null(presence.StateUrl);
        Assert.Null(presence.Assets.LargeImageUrl);
        Assert.Equal("soundcloud-logo", presence.Assets.LargeImageKey);
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void LinksRespectDiscordFieldUrlLimit(int length, bool hasFieldLink)
    {
        const string prefix = "https://soundcloud.com/artist/";
        var url = prefix + new string('a', length - prefix.Length);
        var presence = PresenceService.CreatePresence(new("Song", "Artist", null, url, 0, 10, true, url), DateTime.UtcNow)!;
        Assert.Equal(hasFieldLink ? url : null, presence.DetailsUrl);
        Assert.Equal(hasFieldLink ? url : null, presence.StateUrl);
        Assert.Equal(hasFieldLink ? url : null, presence.Assets.LargeImageUrl);
    }

    [Fact]
    public void InvalidPlaybackDoesNotGeneratePresence()
    {
        Assert.Null(PresenceService.CreatePresence(new("", "Artist", null, null, 0, 10, true), DateTime.UtcNow));
        Assert.Null(PresenceService.CreatePresence(new("Song", "Artist", null, null, double.NaN, 10, true), DateTime.UtcNow));
        Assert.Null(PresenceService.CreatePresence(new("Song", "Artist", null, null, 0, double.PositiveInfinity, true), DateTime.UtcNow));
    }
}
