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

}
