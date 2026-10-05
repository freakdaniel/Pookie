using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ProtectedAudioTests
{
    private static readonly Uri Origin = new("https://playback.media-streaming.soundcloud.cloud/audio.m3u8");
    private static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.Host == Origin.Host && uri.UserInfo == "" && uri.Port == 443;

    [Fact]
    public void PlaylistPreservesEmbeddedInitDataAndUsesOnlyTrustedMedia()
    {
        var pssh = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        var playlist = ProtectedHlsPlaylist.Parse(Playlist(pssh), Origin, Allowed);
        Assert.Equal(pssh, playlist.InitData);
        Assert.Equal(12.5, playlist.Duration);
        Assert.Equal("https://playback.media-streaming.soundcloud.cloud/init.mp4", playlist.Initialization.AbsoluteUri);
        Assert.Equal(10, playlist.Segments[1].Start);
    }

    [Theory]
    [InlineData("init.mp4", "https://evil.test/audio.m4s", "#EXT-X-ENDLIST")]
    [InlineData("http://playback.media-streaming.soundcloud.cloud/init.mp4", "audio.m4s", "#EXT-X-ENDLIST")]
    [InlineData("https://secret@playback.media-streaming.soundcloud.cloud/init.mp4", "audio.m4s", "#EXT-X-ENDLIST")]
    [InlineData("init.mp4", "audio.m4s", "")]
    public void ProtectedPlaylistRejectsForeignOriginsAndUnboundedStreams(string map, string segment, string end)
    {
        var text = Playlist(new byte[64]).Replace("init.mp4", map).Replace("seg1.m4s", segment).Replace("#EXT-X-ENDLIST", end);
        Assert.Throws<DrmPlaybackException>(() => ProtectedHlsPlaylist.Parse(text, Origin, Allowed));
    }

    [Fact]
    public void ExternalKeyUrlsAreNotFetchedAsWidevineInitData()
    {
        var text = Playlist(new byte[64]).Replace("data:text/plain;base64," + Convert.ToBase64String(new byte[64]), "https://evil.test/keys");
        Assert.Throws<DrmPlaybackException>(() => ProtectedHlsPlaylist.Parse(text, Origin, Allowed));
    }

    [Fact]
    public void InitializationReadsAudioConfigurationAndTheKeyIdentifier()
    {
        var init = CencAudioInitialization.Parse(Initialization());
        Assert.Equal(1u, init.TrackId);
        Assert.Equal(2, init.Channels);
        Assert.Equal(4, init.FrequencyIndex); // 44100 Hz
        Assert.Equal(16, init.IvSize);
        Assert.Equal(Enumerable.Repeat((byte)0x42, 16), init.KeyId);
    }

    [Fact]
    public void FragmentResolvesMoofRelativeOffsetsAndSubsampleEncryption()
    {
        var init = CencAudioInitialization.Parse(Initialization());
        var samples = CencAudioFragment.Parse(Fragment(subsamples: true), init);
        var sample = Assert.Single(samples);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, sample.Data);
        Assert.Equal(Enumerable.Repeat((byte)0x21, 16), sample.Iv);
        Assert.Equal(new CencSubsample(1, 3), Assert.Single(sample.Subsamples));
    }

    [Fact]
    public void FragmentSupportsWholeSampleEncryption()
    {
        var sample = Assert.Single(CencAudioFragment.Parse(Fragment(), CencAudioInitialization.Parse(Initialization())));
        Assert.Equal(new CencSubsample(0, 4), Assert.Single(sample.Subsamples));
    }

    [Theory]
    [InlineData(5, 0)] // sample overruns mdat
    [InlineData(4, 1)] // subsamples do not cover the sample
    [InlineData(4, 2)] // incorrect auxiliary sample count
    public void CorruptEncryptedSamplesNeverReachTheCdm(int sampleSize, int corruption)
    {
        Assert.Throws<DrmPlaybackException>(() => CencAudioFragment.Parse(Fragment(true, sampleSize, corruption), CencAudioInitialization.Parse(Initialization())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(16)]
    public void TruncatedBoxesAreRejected(int length) => Assert.Throws<DrmPlaybackException>(() => CencAudioInitialization.Parse(Initialization()[..length]));

    [Fact]
    public void AdtsFrameDescribesTheAacPayloadWithoutTranscoding()
    {
        var header = new byte[7];
        CencAudioFragment.WriteAdtsHeader(header, 1000, 4, 2);
        Assert.Equal(0xff, header[0]); Assert.Equal(0xf1, header[1]);
        Assert.Equal(1, header[2] >> 6); // AAC LC profile
        Assert.Equal(4, header[2] >> 2 & 15);
        Assert.Equal(2, (header[2] & 1) << 2 | header[3] >> 6);
        Assert.Equal(1007, (header[3] & 3) << 11 | header[4] << 3 | header[5] >> 5);
        Assert.Throws<DrmPlaybackException>(() => CencAudioFragment.WriteAdtsHeader(header, 8190, 4, 2));
    }

    [Fact]
    public void AudioSourceDoesNotExposeLicenseOrSignedMediaUrl()
    {
        var source = new AudioSource("https://media.test/?signature=secret", AudioTransport.WidevineHls) { LicenseAuthToken = "license-secret" };
        Assert.DoesNotContain("secret", source.ToString());
    }

    private static string Playlist(byte[] pssh) => $"""
        #EXTM3U
        #EXT-X-MAP:URI="init.mp4"
        #EXT-X-KEY:METHOD=SAMPLE-AES,URI="data:text/plain;base64,{Convert.ToBase64String(pssh)}",KEYFORMAT="urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed"
        #EXT-X-KEY:METHOD=SAMPLE-AES-CTR,URI="data:text/plain;base64,other",KEYFORMAT="com.microsoft.playready"
        #EXTINF:10,
        seg1.m4s
        #EXTINF:2.5,
        seg2.m4s
        #EXT-X-ENDLIST
        """;

    private static byte[] Initialization()
    {
        var entry = new byte[28];
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(24), 44100u << 16);
        // ES_Descriptor -> DecoderConfigDescriptor -> DecoderSpecificInfo (AAC-LC 44.1kHz stereo)
        byte[] descriptor = [3, 22, 0, 1, 0, 4, 17, 0x40, 0x15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 2, 0x12, 0x10];
        var tenc = Join(new byte[6], new byte[] { 1, 16 }, Enumerable.Repeat((byte)0x42, 16).ToArray());
        var sinf = Box("sinf", Join(Box("frma", Encoding.ASCII.GetBytes("mp4a")), Box("schm", Join(new byte[4], Encoding.ASCII.GetBytes("cenc"), U32(0x10000))), Box("schi", Box("tenc", tenc))));
        var stsd = Box("stsd", Join(new byte[4], U32(1), Box("enca", Join(entry, Box("esds", Join(new byte[4], descriptor)), sinf))));
        var tkhd = Box("tkhd", Join(new byte[12], U32(1)));
        var hdlr = Box("hdlr", Join(new byte[8], Encoding.ASCII.GetBytes("soun")));
        var mdia = Box("mdia", Join(hdlr, Box("minf", Box("stbl", stsd))));
        var trex = Box("trex", Join(new byte[4], U32(1), U32(1), U32(1024), U32(0), U32(0)));
        return Box("moov", Join(Box("trak", Join(tkhd, mdia)), Box("mvex", trex)));
    }

    private static byte[] Fragment(bool subsamples = false, int sampleSize = 4, int corruption = 0)
    {
        var tfhd = Box("tfhd", Join(U32(0x20000), U32(1)));
        var auxiliary = Join(U32(subsamples ? 2u : 0u), U32(corruption == 2 ? 2u : 1u), Enumerable.Repeat((byte)0x21, 16).ToArray(),
            subsamples ? new byte[] { 0, 1, 0, 1, 0, 0, 0, corruption == 1 ? (byte)2 : (byte)3 } : []);
        var senc = Box("senc", auxiliary);
        byte[] Make(int offset) => Box("moof", Box("traf", Join(tfhd, Box("trun", Join(U32(0x201), U32(1), U32((uint)offset), U32((uint)sampleSize))), senc)));
        var moof = Make(0); moof = Make(moof.Length + 8);
        return Join(moof, Box("mdat", [1, 2, 3, 4]));
    }
    private static byte[] Box(string type, byte[] payload) => Join(U32((uint)payload.Length + 8), Encoding.ASCII.GetBytes(type), payload);
    private static byte[] U32(uint value) { var result = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(result, value); return result; }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
}
