using Xunit;

namespace Pookie.Audio.Tests;

public sealed class HlsTests
{
    private static readonly Uri Origin = new("https://cf-hls-media.sndcdn.com/playlist.m3u8");
    private static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.Host.EndsWith(".sndcdn.com");

    [Fact]
    public void PlaylistUsesInvariantDurationsAndResolvesRelativeByteRanges()
    {
        var playlist = HlsPlaylist.Parse("""
            #EXTM3U
            #EXTINF:1.25,
            #EXT-X-BYTERANGE:100@0
            audio.mp3
            #EXTINF:2.5,
            #EXT-X-BYTERANGE:200
            audio.mp3
            #EXT-X-ENDLIST
            """, Origin, Allowed);
        Assert.Equal(3.75, playlist.Duration);
        Assert.Equal(1.25, playlist.Segments[1].Start);
        Assert.Equal(100, playlist.Segments[1].Offset);
        Assert.Equal(200, playlist.Segments[1].Length);
        Assert.Equal("https://cf-hls-media.sndcdn.com/audio.mp3", playlist.Segments[1].Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("#EXT-X-KEY:METHOD=AES-128,URI=\"secret.key\"\n#EXTINF:1,\nseg.mp3\n#EXT-X-ENDLIST")]
    [InlineData("#EXTINF:1,\nhttp://evil.test/seg.mp3\n#EXT-X-ENDLIST")]
    [InlineData("#EXTINF:1,\nfile:///etc/passwd\n#EXT-X-ENDLIST")]
    [InlineData("#EXTINF:1,\nseg.mp3")]
    [InlineData("#EXTINF:NaN,\nseg.mp3\n#EXT-X-ENDLIST")]
    [InlineData("#EXTINF:1,\n#EXT-X-BYTERANGE:100\nseg.mp3\n#EXT-X-ENDLIST")]
    [InlineData("#EXT-X-STREAM-INF:BANDWIDTH=1000\nplaylist.m3u8\n#EXT-X-ENDLIST")]
    public void PlaylistRejectsEncryptionLiveStreamsAndInvalidMedia(string content) =>
        Assert.Throws<InvalidOperationException>(() => HlsPlaylist.Parse("#EXTM3U\n" + content, Origin, Allowed));
}
