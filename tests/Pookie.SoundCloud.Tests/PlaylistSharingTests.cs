using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class PlaylistSharingTests
{
    private static SoundCloudPlaylist Playlist => new() { Id = 11, User = new() { Id = 42 }, Sharing = "private", PermalinkUrl = "https://soundcloud.com/artist/sets/mix" };

    [Fact]
    public void PrivatePlaylistNeverFallsBackToPublicAddress()
    {
        Assert.Null(PlaylistSharing.ShareUri(Playlist));
        Assert.Null(PlaylistSharing.EmbedUri(Playlist));
        Assert.Equal(Playlist.PermalinkUrl, PlaylistSharing.ShareUri(Playlist with { Sharing = "public" })?.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://soundcloud.com/artist/sets/mix")]
    [InlineData("https://soundcloud.com/artist/sets/mix/s-old")]
    [InlineData("https://soundcloud.com/artist/sets/mix?secret_token=s-old")]
    public void FreshTokenReplacesOldTokenForLinksAndWidgets(string permalink)
    {
        var playlist = Playlist with { PermalinkUrl = permalink, SecretToken = "s-new" };
        Assert.Equal("https://soundcloud.com/artist/sets/mix/s-new", PlaylistSharing.ShareUri(playlist)?.AbsoluteUri);
        Assert.Equal("https://api.soundcloud.com/playlists/11?secret_token=s-new", PlaylistSharing.EmbedUri(playlist)?.AbsoluteUri);
    }

    [Fact]
    public void ReadsTokensFromSoundCloudApiUriAndAlreadySecretPermalinks()
    {
        using var json = JsonDocument.Parse("""{"id":11,"sharing":"private","permalink_url":"https://soundcloud.com/artist/sets/mix","uri":"https://api.soundcloud.com/playlists/11?secret_token=s-fromUri"}""");
        Assert.EndsWith("/s-fromUri", PlaylistSharing.ShareUri(LibraryData.ParsePlaylist(json.RootElement))!.AbsoluteUri);
        Assert.EndsWith("/s-path", PlaylistSharing.ShareUri(Playlist with { PermalinkUrl = Playlist.PermalinkUrl + "/s-path" })!.AbsoluteUri);
        Assert.Null(PlaylistSharing.ShareUri(Playlist with { SecretToken = "s-token?injected=1" }));
    }

    [Theory]
    [InlineData("success", 3, 1)]
    [InlineData("public", 1, 0)]
    [InlineData("foreign", 1, 0)]
    [InlineData("lost", 2, 1)]
    [InlineData("unchanged", 3, 1)]
    public async Task ResetChecksOwnershipPrivacyAndConfirmationWithoutReplaying(string mode, int expectedReadsAndWrites, int expectedWrites)
    {
        var calls = 0; var writes = 0;
        var current = Playlist with { SecretToken = "s-old", Sharing = mode == "public" ? "public" : "private", User = new() { Id = mode == "foreign" ? 9 : 42 } };
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Post)
            {
                writes++;
                Assert.Equal("/soundcloud:playlists:11/secret_token/reset", request.RequestUri!.AbsolutePath);
                Assert.Null(request.Content);
                if (mode == "lost") throw new HttpRequestException("Lost acknowledgement");
                if (mode != "unchanged") current = current with { SecretToken = "s-new" };
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"secret_token":"s-new"}""") };
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(current, SoundCloudJson.Default.SoundCloudPlaylist)) };
        }));
        using var transport = new SoundCloudHttpTransport(http, () => new("web", "fixture-token", "Test"));
        if (mode == "success")
        {
            using var result = await transport.EditPlaylistAsync(new("reset-secret", 42, 11));
            Assert.Equal("s-new", LibraryData.ParsePlaylist(result.RootElement).SecretToken);
        }
        else await Assert.ThrowsAnyAsync<Exception>(() => transport.EditPlaylistAsync(new("reset-secret", 42, 11)));
        Assert.Equal(expectedReadsAndWrites, calls); Assert.Equal(expectedWrites, writes);
    }

    [Fact]
    public async Task SharedPrivatePlaylistRetainsTheAccessTokenUsedForItsRead()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Contains("secret_token=s-invitation", request.RequestUri!.Query);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Playlist, SoundCloudJson.Default.SoundCloudPlaylist)) };
        }));
        var session = new WebSession("web", "fixture-token", "Test");
        using var transport = new SoundCloudHttpTransport(http, () => session);
        var api = new SoundCloudWebClient(http) { Session = session, Transport = transport };
        var playlist = await api.GetPlaylistForSharingAsync(11, secretToken: "s-invitation");
        Assert.EndsWith("/s-invitation", PlaylistSharing.ShareUri(playlist)!.AbsoluteUri);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
