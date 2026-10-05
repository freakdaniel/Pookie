using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public class LoginSessionMessageTests
{
    private const string Message = """
        {"id":"pookie:web-session","command":"Post","version":2,"data":{"client_id":"publicid","access_token":"fake-token","user_agent":"WebKit"}}
        """;

    [Fact]
    public void AcceptsValidatedSessionFromSoundCloud()
    {
        Assert.Equal(new WebSession("publicid", "fake-token", "WebKit"), LoginSessionMessage.Parse(Message, "https://soundcloud.com/you/likes"));
    }

    [Theory]
    [InlineData("http://soundcloud.com/")]
    [InlineData("https://soundcloud.com.evil.test/")]
    [InlineData("https://soundcloud.com:8443/")]
    [InlineData("https://evil.test/")]
    [InlineData("https://user@soundcloud.com/")]
    [InlineData(null)]
    public void RejectsUntrustedMessageOrigins(string? origin)
    { Assert.Null(LoginSessionMessage.Parse(Message, origin)); }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"id\":null}")]
    [InlineData("{\"id\":\"pookie:web-session\",\"command\":\"Post\",\"version\":\"2\"}")]
    public void RejectsMalformedMessages(string message)
    { Assert.Null(LoginSessionMessage.Parse(message, "https://soundcloud.com/")); }

    [Fact]
    public void WebKitBridgeRequiresItsPerWindowPairingCapability()
    {
        const string pairing = "private-window-capability";
        Assert.Null(LoginSessionMessage.Parse(Message, "https://soundcloud.com/", expectedPairing: pairing));
        var paired = Message[..^1] + ",\"pookie_pairing\":\"" + pairing + "\"}";
        Assert.NotNull(LoginSessionMessage.Parse(paired, "https://soundcloud.com/", expectedPairing: pairing));
        Assert.Null(LoginSessionMessage.Parse(paired, "https://soundcloud.com/", expectedPairing: "wrong-capability"));
        Assert.Null(LoginSessionMessage.Parse(paired, "https://evil.test/", expectedPairing: pairing));
    }

    [Fact]
    public void RejectsMalformedTokensAndWrongProtocolVersion()
    {
        Assert.Null(LoginSessionMessage.Parse(Message.Replace("fake-token", "bad token"), "https://soundcloud.com/"));
        Assert.Null(LoginSessionMessage.Parse(Message.Replace("\"version\":2", "\"version\":1"), "https://soundcloud.com/"));
        Assert.Null(LoginSessionMessage.Parse(new string('x', 10001), "https://soundcloud.com/"));
    }

}
