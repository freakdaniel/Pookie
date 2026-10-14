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

    [Fact]
    public void InitializationMapIsResolvedAndAppliesToEveryFollowingFragmentUntilItChanges()
    {
        var playlist = HlsPlaylist.Parse("""
            #EXTM3U
            #EXT-X-MAP:URI="init.mp4?version=1,2",BYTERANGE="44@5"
            #EXTINF:2,
            one.m4s
            #EXTINF:2,
            two.m4s
            #EXT-X-MAP:URI="next-init.mp4"
            #EXTINF:2,
            three.m4s
            #EXT-X-ENDLIST
            """, Origin, Allowed);
        Assert.Equal(6, playlist.Duration);
        var first = playlist.Segments[0].Initialization!;
        Assert.Equal("https://cf-hls-media.sndcdn.com/init.mp4?version=1,2", first.Uri.AbsoluteUri);
        Assert.Equal(5, first.Offset); Assert.Equal(44, first.Length);
        Assert.Equal(first, playlist.Segments[1].Initialization);
        Assert.EndsWith("next-init.mp4", playlist.Segments[2].Initialization!.Uri.AbsoluteUri);
        Assert.Null(playlist.Segments[2].Initialization!.Offset);
    }

    [Theory]
    [InlineData("URI=\"file:///etc/passwd\"")]
    [InlineData("URI=\"https://evil.test/init.mp4\"")]
    [InlineData("BYTERANGE=\"44@0\"")]
    [InlineData("URI=\"init.mp4\",BYTERANGE=\"0@0\"")]
    [InlineData("URI=\"init.mp4\",BYTERANGE=\"44@-1\"")]
    [InlineData("URI=\"init.mp4\",BYTERANGE=\"1048577@0\"")]
    [InlineData("URI=\"init.mp4\",BYTERANGE=\"44@9223372036854775807\"")]
    [InlineData("URI=\"init.mp4\",URI=\"other.mp4\"")]
    public void InitializationMapRejectsInvalidAddressesRangesAndDuplicateAttributes(string map) =>
        Assert.Throws<InvalidOperationException>(() => HlsPlaylist.Parse($"#EXTM3U\n#EXT-X-MAP:{map}\n#EXTINF:2,\nfragment.m4s\n#EXT-X-ENDLIST", Origin, Allowed));

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
