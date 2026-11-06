using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class WebProfileTests
{
    [Theory]
    [InlineData("https://api-mobile.soundcloud.com/me")]
    [InlineData("https://api-v2.soundcloud.com/configuration/android")]
    [InlineData("https://api-v2.soundcloud.com/tracks/17/streams/https")]
    public void RemovedMobileRoutesCannotBeSentThroughBrowser(string url) => Assert.False(BrowserRequestCommand.IsReadUrl(url));

    [Fact]
    public void OldAndroidIdentityCannotBecomeAWebSessionByDroppingUnknownJsonFields()
    {
        const string json = """{"client_id":"fixture","access_token":"private-token","user_agent":"SoundCloud/2026.11 (Android 15; Fixture)","mobile_identity":{"android_id":"ignored"}}""";
        Assert.False(JsonSerializer.Deserialize(json, SoundCloudJson.Default.WebSession)!.IsValid());
    }

    [Fact]
    public async Task PlaylistEditorUsesWebTransportAndConfirmsReturnedMetadata()
    {
        var transport = new Fixture();
        using var http = new HttpClient(new DeniedHttp());
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "token", "Test"), Transport = transport };
        var result = await api.UpdatePlaylistAsync(new(42, 77, "Title", "Description", "private"));
        Assert.Equal("Title", result.Title); Assert.Equal("update", Assert.Single(transport.Edits).Kind);
        transport.WrongOwner = true;
        await Assert.ThrowsAsync<SoundCloudException>(() => api.UpdatePlaylistAsync(new(42, 77, "Title", "Description", "private")));
        Assert.Equal(2, transport.Edits.Count);
    }

    [Fact]
    public async Task InvalidArtworkCannotReachWebsiteOrHttp()
    {
        var transport = new Fixture();
        using var http = new HttpClient(new DeniedHttp());
        var api = new SoundCloudWebClient(http) { Session = new("fixture", "token", "Test"), Transport = transport };
        await Assert.ThrowsAsync<ArgumentException>(() => api.SetPlaylistArtworkAsync(42, 77, [1, 2, 3, 4]));
        Assert.Empty(transport.Edits);
    }

    private sealed class Fixture : ISoundCloudTransport
    {
        internal readonly List<PlaylistEditRequest> Edits = [];
        internal bool WrongOwner;
        public Task<JsonDocument> EditPlaylistAsync(PlaylistEditRequest request, CancellationToken cancellationToken = default)
        {
            Edits.Add(request);
            return Task.FromResult(JsonSerializer.SerializeToDocument(new { id = 77, user = new { id = WrongOwner ? 9 : 42 },
                title = "Title", description = "Description", sharing = "private", track_count = 0, tracks = Array.Empty<object>() }));
        }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected read.");
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected write.");
    }
    private sealed class DeniedHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Web profile reached native HTTP.");
    }
}
