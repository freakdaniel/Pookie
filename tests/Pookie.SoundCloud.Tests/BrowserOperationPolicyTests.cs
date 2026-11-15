using System.Text.Json;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class BrowserOperationPolicyTests
{
    [Fact]
    public void EveryMutationHasOneSharedCancellationPolicy()
    {
        foreach (var operation in BrowserOperationPolicy.WriteOperations.Split(' '))
            Assert.True(BrowserOperationPolicy.IsWrite(new("fixture", operation)));
        Assert.True(BrowserOperationPolicy.IsWrite(new("fixture", "playlist-edit")));
        Assert.True(BrowserOperationPolicy.IsWrite(new("fixture", "chat", Chat: new("send", 1, 2, "hello"))));
        Assert.True(BrowserOperationPolicy.IsWrite(new("fixture", "chat", Chat: new("read", 1, 2))));
        foreach (var operation in new[] { "api-get", "me", "liked-ids", "track-read", "follow-signature", "audio", "cancel" })
            Assert.False(BrowserOperationPolicy.IsWrite(new("fixture", operation)));
        Assert.False(BrowserOperationPolicy.IsWrite(new("fixture", "chat", Chat: new("thread", 1, 2))));
    }

    [Fact]
    public void OptionalTransportFeaturesAreDiscoverableWithoutInvokingThem()
    {
        ISoundCloudTransport readOnly = new Fixture();
        Assert.True(readOnly.Supports(SoundCloudTransportCapabilities.Read));
        Assert.False(readOnly.Supports(SoundCloudTransportCapabilities.Chats));
        Assert.Throws<SoundCloudException>(() => readOnly.Require(SoundCloudTransportCapabilities.Chats));
        using var http = new HttpClient();
        using var native = new SoundCloudHttpTransport(http, () => new("fixture", "token", "Fixture"));
        Assert.True(native.Supports(SoundCloudTransportCapabilities.Read | SoundCloudTransportCapabilities.Comments | SoundCloudTransportCapabilities.Chats));
        Assert.False(native.Supports(SoundCloudTransportCapabilities.FollowSigning));
    }

    private sealed class Fixture : ISoundCloudTransport
    {
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
