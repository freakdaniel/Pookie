using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class BrowserRequestProtocolTests
{
    private const string Origin = "https://soundcloud.com";
    private static string Message(string data, string pairing = "pair") => "{\"id\":\"pookie:web-api\",\"version\":1,\"pookie_pairing\":\"" + pairing + "\",\"data\":" + data + "}";

    [Fact]
    public void EventsRequireExactOriginAndPairing()
    {
        var raw = Message("{\"kind\":\"checking\",\"interactive\":true}");
        Assert.True(BrowserRequestProtocol.Parse(raw, Origin + "/", Origin, "pair")!.Interactive);
        foreach (var source in new[] { "https://soundcloud.com.evil.test/", "https://soundcloud.com:8443/", "http://soundcloud.com/", "https://evil@soundcloud.com/" })
            Assert.Null(BrowserRequestProtocol.Parse(raw, source, Origin, "pair"));
        Assert.Null(BrowserRequestProtocol.Parse(raw, Origin, Origin, "other"));
    }

    [Theory]
    [InlineData("{\"kind\":\"execute\"}")]
    [InlineData("{\"kind\":\"complete\",\"request_id\":\"wrong\"}")]
    [InlineData("{\"kind\":\"ready\",\"status\":999}")]
    public void InvalidEventsAreIgnored(string data) => Assert.Null(BrowserRequestProtocol.Parse(Message(data), Origin, Origin));

    [Fact]
    public void HardBlockRequiresItsTypeAndTrustedOrigin()
    {
        var raw = Message("{\"kind\":\"blocked\",\"interactive\":true,\"challenge_type\":\"hard_block\"}");
        Assert.Equal("blocked", BrowserRequestProtocol.Parse(raw, Origin, Origin)!.Kind);
        Assert.Null(BrowserRequestProtocol.Parse(raw, "https://evil.test", Origin));
        Assert.Null(BrowserRequestProtocol.Parse(raw.Replace("hard_block", "device_check"), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(raw.Replace("true", "false"), Origin, Origin));
    }

    [Fact]
    public void CommandsValidateBrowserSafeIdsAndOperations()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.True(new BrowserRequestCommand(id, "like", 1, 42).IsValid());
        Assert.True(new BrowserRequestCommand(id, "me").IsValid());
        Assert.True(new BrowserRequestCommand(id, "liked-ids").IsValid());
        Assert.False(new BrowserRequestCommand(id, "execute").IsValid());
        Assert.False(new BrowserRequestCommand(id, "like", -1, 42).IsValid());
        Assert.False(new BrowserRequestCommand(id, "like", 1, long.MaxValue).IsValid());
        var json = JsonSerializer.Serialize(new BrowserRequestCommand(id, "like", 1, 42, true), SoundCloudJson.Default.BrowserRequestCommand);
        Assert.Contains("\"track_id\":42", json);
        Assert.Contains("\"liked\":true", json);
    }

    [Fact]
    public void IdBatchesAreBoundedAndRequireARequestId()
    {
        var id = Guid.NewGuid().ToString("N");
        var data = "{\"kind\":\"ids\",\"request_id\":\"" + id + "\",\"ids\":[42,90]}";
        Assert.Equal(new long[] { 42, 90 }, BrowserRequestProtocol.Parse(Message(data), Origin, Origin)!.Ids);
        Assert.Null(BrowserRequestProtocol.Parse(Message(data.Replace("[42,90]", "[-1]")), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Message(data.Replace(id, "wrong")), Origin, Origin));
        var oversized = JsonSerializer.Serialize(Enumerable.Range(1, 201).ToArray());
        Assert.Null(BrowserRequestProtocol.Parse(Message(data.Replace("[42,90]", oversized)), Origin, Origin));
    }
    [Fact]
    public void ProtectionRotationRequiresAValidBoundedTokenAndTrustedOrigin()
    {
        var data = "{\"kind\":\"protection-session\",\"data_dome_client_id\":\"fixture-rotated\"}";
        Assert.Equal("fixture-rotated", BrowserRequestProtocol.Parse(Message(data), Origin, Origin)!.DataDomeClientId);
        Assert.Null(BrowserRequestProtocol.Parse(Message(data), "https://evil.test", Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Message(data.Replace("fixture-rotated", "cookie; other=value")), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Message(data.Replace("fixture-rotated", new string('a', 4097))), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Message("{\"kind\":\"protection-session\"}"), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Message(data.Replace("protection-session", "passed")), Origin, Origin));
    }
    [Theory]
    [InlineData("https://api-v2.soundcloud.com/search/tracks?q=test", true)]
    [InlineData("https://api-v2.soundcloud.com/users/42/likes?cursor=2", true)]
    [InlineData("https://api-v2.soundcloud.com/media/soundcloud:tracks:42/uuid/stream/hls", true)]
    [InlineData("https://evil.test/me", false)]
    [InlineData("https://user@api-v2.soundcloud.com/me", false)]
    [InlineData("https://api-v2.soundcloud.com/me#secret", false)]
    [InlineData("https://api-v2.soundcloud.com/users/42/track_likes/90", false)]
    public void ReadCommandsValidateOriginAndReadOnlyEndpoint(string url, bool valid)
    {
        var command = new BrowserRequestCommand(Guid.NewGuid().ToString("N"), "api-get", Url: url);
        Assert.Equal(valid, command.IsValid());
        Assert.DoesNotContain(url, command.ToString());
    }

    [Fact]
    public void JsonChunksAreBoundedAndParentAssemblyIsNeverSerialized()
    {
        var value = new BrowserRequestEvent("json-chunk", Guid.NewGuid().ToString("N"), Chunk: "Привет 🎵") { Json = "parent-only" };
        string Raw(BrowserRequestEvent message) => Message(JsonSerializer.Serialize(message, SoundCloudJson.Default.BrowserRequestEvent));
        Assert.NotNull(BrowserRequestProtocol.Parse(Raw(value), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { Chunk = new string('x', 1025) }), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { ChunkIndex = -1 }), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { RequestId = "bad" }), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { Kind = "passed" }), Origin, Origin));
        Assert.DoesNotContain("parent-only", Raw(value));
    }

    [Fact]
    public void AudioCommandsRestrictCredentialsAndMediaOrigin()
    {
        var id = Guid.NewGuid().ToString("N");
        var audio = new BrowserAudioCommand("start", id, "https://cf-hls-media.sndcdn.com/track.m3u8", "private-license-token");
        bool Valid(BrowserAudioCommand value) => new BrowserRequestCommand(id, "audio", Audio: value).IsValid();
        Assert.True(Valid(audio));
        foreach (var source in new[] { "https://sndcdn.com.evil.test/file", "http://sndcdn.com/file", "https://user@sndcdn.com/file", "https://sndcdn.com:8443/file", "https://sndcdn.com/file#fragment" })
            Assert.False(Valid(audio with { Source = source }));
        Assert.False(Valid(audio with { Authorization = "token\nheader" }));
        Assert.False(Valid(audio with { Authorization = new string('a', 16385) }));
        Assert.False(Valid(audio with { Position = double.NaN }));
        Assert.False(Valid(audio with { Volume = 101 }));
        Assert.False(Valid(audio with { PlaybackId = "bad" }));
        Assert.False(Valid(audio with { Action = "execute" }));
        Assert.False(Valid(audio with { Action = "stop" }));
        Assert.True(Valid(new("seek", id, Position: 25)));
        Assert.False(new BrowserRequestCommand(id, "me", Audio: audio).IsValid());
        Assert.DoesNotContain("private-license-token", audio.ToString());
    }

    [Fact]
    public void AudioStateRequiresTrustedSourceAndBoundedFiniteValues()
    {
        var value = new BrowserRequestEvent("audio-state", Guid.NewGuid().ToString("N"), 200,
            Audio: new(10, 200, true, false, false));
        string Raw(BrowserRequestEvent message) => Message(JsonSerializer.Serialize(message, SoundCloudJson.Default.BrowserRequestEvent));
        Assert.NotNull(BrowserRequestProtocol.Parse(Raw(value), Origin, Origin, "pair"));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value), "https://evil.test", Origin, "pair"));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value), Origin, Origin, "wrong"));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { RequestId = "bad" }), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { Audio = null }), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { Audio = value.Audio! with { Position = -1 } }), Origin, Origin));
        Assert.Null(BrowserRequestProtocol.Parse(Raw(value with { Audio = value.Audio! with { Error = "https://secret-token" } }), Origin, Origin));
        Assert.False(new BrowserAudioState(double.NaN, 2, false, false, false).IsValid());
        var buffered = value.Audio! with { BufferedStart = 10, BufferedEnd = 45 };
        Assert.Equal(buffered, BrowserRequestProtocol.Parse(Raw(value with { Audio = buffered }), Origin, Origin)!.Audio);
        var normalized = buffered with { NormalizationGainDb = -10 };
        Assert.Equal(normalized, BrowserRequestProtocol.Parse(Raw(value with { Audio = normalized }), Origin, Origin)!.Audio);
        foreach (var invalid in new[] {
            normalized with { NormalizationGainDb = double.PositiveInfinity }, normalized with { NormalizationGainDb = 10 } })
            Assert.False(invalid.IsValid());
        foreach (var invalid in new[] {
            buffered with { BufferedStart = -1 }, buffered with { BufferedEnd = 9 },
            buffered with { BufferedEnd = 201 }, buffered with { BufferedEnd = double.PositiveInfinity } })
            Assert.False(invalid.IsValid());
    }

}
