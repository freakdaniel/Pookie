using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class WebClientTests
{
    [Fact]
    public void WebsiteClientIdIsReadFromHydration()
    {
        Assert.Equal("publicWebsiteId", SoundCloudWebClient.ExtractClientId("window.__sc_hydration = [{\"hydratable\": \"apiClient\", \"data\": {\"id\": \"publicWebsiteId\"}}]"));
        Assert.Null(SoundCloudWebClient.ExtractClientId("<html>changed page</html>"));
    }

    [Fact]
    public void LikesCollectionSkipsPlaylistsAndHandlesNestedTracks()
    {
        using var document = JsonDocument.Parse("""
            {"collection":[{"track":{"id":42,"title":"Track","duration":123456,"user":{"id":7,"username":"Artist"}}},
            {"playlist":{"id":8,"title":"Playlist"}},null,{"id":9,"title":"Another playlist","tracks":[]}],
            "next_href":"https://api-v2.soundcloud.com/users/7/likes?cursor=next"}
            """);
        var page = SoundCloudWebClient.ParsePage(document.RootElement);
        var track = Assert.Single(page.Tracks);
        Assert.Equal("Artist", track.Author);
        Assert.Equal(123.456, track.DurationSeconds, 3);
        Assert.NotNull(page.NextHref);
    }

    [Theory]
    [InlineData("http://api-v2.soundcloud.com/me")]
    [InlineData("https://api-v2.soundcloud.com.attacker.test/me")]
    [InlineData("https://attacker.test/?client_id=secret")]
    [InlineData("https://api-v2.soundcloud.com:444/me")]
    public async Task PaginationCannotSendSessionToAnUntrustedOrigin(string url)
    {
        using var http = new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Untrusted request sent")));
        var api = new SoundCloudWebClient(http) { Session = Session() };
        await Assert.ThrowsAsync<SoundCloudException>(() => api.GetNextPageAsync(url));
    }

    [Fact]
    public async Task WebsiteSearchWorksWithoutRegisteredAppCredentials()
    {
        var requests = new List<HttpRequestMessage>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            if (request.RequestUri!.Host == "soundcloud.com") return Text("[{\"hydratable\":\"apiClient\",\"data\":{\"id\":\"publicid\"}}]");
            Assert.Equal("/search/tracks", request.RequestUri.AbsolutePath);
            Assert.Contains("client_id=publicid", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            return Text("{\"collection\":[{\"id\":42,\"title\":\"Music\"}],\"next_href\":null}");
        }));
        var page = await new SoundCloudWebClient(http).SearchAsync("ambient & birds");
        Assert.Single(page.Tracks);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task SessionIsSentAsWebsiteOAuthHeader()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("api-v2.soundcloud.com", request.RequestUri!.Host);
            Assert.Equal("OAuth", request.Headers.Authorization?.Scheme);
            Assert.Equal("web-token", request.Headers.Authorization?.Parameter);
            return Text("{\"id\":7,\"username\":\"Me\",\"avatar_url\":\"https://i1.sndcdn.com/avatar-large.jpg\"}");
        }));
        var me = await new SoundCloudWebClient(http) { Session = Session() }.GetMeAsync();
        Assert.Equal(7, me.Id);
        Assert.Equal("https://i1.sndcdn.com/avatar-large.jpg", me.AvatarUrl);
    }

    [Fact]
    public async Task UnauthorizedDoesNotExposeResponseBodyOrToken()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("web-token") }));
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http) { Session = Session() }.GetMeAsync());
        Assert.Equal(401, error.StatusCode);
        Assert.DoesNotContain("web-token", error.Message);
        Assert.DoesNotContain("web-token", Session().ToString());
    }

    [Fact]
    public async Task StreamResolutionUsesTrackAuthorizationAndRejectsEncryptedVariants()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/tracks/42") return Text("""
                {"id":42,"title":"Track","access":"playable","track_authorization":"track-auth",
                "media":{"transcodings":[
                {"url":"https://api-v2.soundcloud.com/encrypted","format":{"protocol":"ctr-encrypted-hls"}},
                {"url":"https://api-v2.soundcloud.com/media/stream","snipped":false,"format":{"protocol":"hls","mime_type":"audio/mpeg"}}]}}
                """);
            Assert.Equal("/media/stream", request.RequestUri.AbsolutePath);
            Assert.Contains("track_authorization=track-auth", request.RequestUri.Query);
            return Text("{\"url\":\"https://cf-hls-media.sndcdn.com/test.m3u8?Policy=signed\"}");
        }));
        var stream = await new SoundCloudWebClient(http) { Session = Session() }.GetStreamAsync(new() { Id = 42 });
        Assert.Equal("cf-hls-media.sndcdn.com", stream.Uri.Host);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("blocked")]
    public async Task RestrictedTracksAreNotPlayedAsFullTracks(string access)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/tracks/42", request.RequestUri!.AbsolutePath);
            return Text($"{{\"id\":42,\"access\":\"{access}\"}}");
        }));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http) { Session = Session() }.GetStreamAsync(new() { Id = 42, Access = access }));
    }

    [Theory]
    [InlineData("https://sndcdn.com.evil.test/track.mp3")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://cf-media.sndcdn.com/track.mp3")]
    public void PlayerUrlsMustBeSoundCloudHttpsMedia(string url) => Assert.False(SoundCloudWebClient.IsMediaUri(new Uri(url)));

    [Fact]
    public async Task FeedUsesWebSessionAndParsesTrackOrigins()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/stream", request.RequestUri!.AbsolutePath);
            Assert.Equal("web-token", request.Headers.Authorization?.Parameter);
            return Text("""
                {"collection":[{"type":"track-repost","origin":{"id":42,"title":"New track"}},
                {"type":"playlist","origin":{"id":8,"title":"Album","tracks":[]}}],"next_href":null}
                """);
        }));
        var api = new SoundCloudWebClient(http) { Session = Session() };
        Assert.Equal(42, Assert.Single((await api.GetFeedAsync()).Tracks).Id);
    }

    [Fact]
    public async Task LikeIdsFollowTrustedPaginationAndAcceptNumericStrings()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.Query.Contains("cursor=next")
            ? Text("{\"collection\":[\"43\"],\"next_href\":null}")
            : Text("{\"collection\":[42],\"next_href\":\"https://api-v2.soundcloud.com/me/track_likes/ids?cursor=next\"}")));
        var ids = await new SoundCloudWebClient(http) { Session = Session() }.GetLikedIdsAsync();
        Assert.Equal(new long[] { 42, 43 }, ids.Order().ToArray());
    }

    [Theory]
    [InlineData(true, "PUT")]
    [InlineData(false, "DELETE")]
    public async Task LikeChangesUseWebsiteEndpointAndAcceptEmptySuccess(bool liked, string method)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(method, request.Method.Method);
            Assert.Equal("/users/7/track_likes/42", request.RequestUri!.AbsolutePath);
            Assert.Equal("OAuth", request.Headers.Authorization?.Scheme);
            return new(HttpStatusCode.NoContent);
        }));
        await new SoundCloudWebClient(http) { Session = Session() }.SetLikedAsync(7, 42, liked);
    }

    [Fact]
    public async Task LikeChangesCannotRunWithoutAnAccount()
    {
        using var http = new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Anonymous mutation sent")));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http).SetLikedAsync(7, 42, true));
    }

    [Fact]
    public async Task LikeChangesCarryAndRotateTheWebsiteProtectionSession()
    {
        var session = Session() with { DataDomeClientId = "browser-session~_=", AppVersion = "1790934937", AppLocale = "ru" };
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(calls++ == 0 ? "PUT" : "DELETE", request.Method.Method);
            Assert.Equal(calls == 1 ? "browser-session~_=" : "rotated-session", request.Headers.GetValues("x-datadome-clientid").Single());
            Assert.Contains("client_id=publicid", request.RequestUri!.Query);
            Assert.Contains("app_version=1790934937", request.RequestUri.Query);
            Assert.Contains("app_locale=ru", request.RequestUri.Query);
            Assert.Equal("web-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("https://soundcloud.com/", request.Headers.Referrer?.AbsoluteUri);
            Assert.Null(request.Content);
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.Add("x-set-cookie", "datadome=rotated-session; Path=/; Secure; SameSite=Lax");
            return response;
        }));
        await new SoundCloudWebClient(http) { Session = session }.SetLikedAsync(7, 42, true);
        // A second short-lived API client must use the same updated browser session.
        await new SoundCloudWebClient(http) { Session = session }.SetLikedAsync(7, 42, false);
        Assert.Equal(2, calls);
        Assert.DoesNotContain("rotated-session", session.ToString());
    }

    [Theory]
    [InlineData("{\"url\":\"https://geo.captcha-delivery.com/captcha/?secret=private\"}", true)]
    [InlineData("{\"url\":\"https://captcha-delivery.com.evil.test/captcha/\"}", false)]
    [InlineData("{\"errors\":[{\"error_message\":\"Forbidden\"}]}", false)]
    [InlineData("<html>Forbidden</html>", false)]
    public async Task BrowserChallengesAreDistinguishedFromResourceDenials(string body, bool challenge)
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent(body) }));
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http) { Session = Session() }.SetLikedAsync(7, 42, true));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(challenge, error.RequiresBrowserVerification);
        Assert.DoesNotContain("private", error.Message);
        Assert.DoesNotContain("captcha-delivery", error.Message);
    }

    [Theory]
    [InlineData("invalid\r\nInjected: header")]
    [InlineData("value; another=cookie")]
    public async Task InvalidProtectionSessionsCannotBecomeRequestHeaders(string value)
    {
        using var http = new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Invalid session sent")));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http)
            { Session = Session() with { DataDomeClientId = value } }.SetLikedAsync(7, 42, true));
    }

    [Fact]
    public async Task LikeIdPaginationCannotLeakTokenToAnotherHost()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("api-v2.soundcloud.com", request.RequestUri!.Host);
            return Text("{\"collection\":[42],\"next_href\":\"https://attacker.test/ids\"}");
        }));
        await Assert.ThrowsAsync<SoundCloudException>(() => new SoundCloudWebClient(http) { Session = Session() }.GetLikedIdsAsync());
    }


    [Fact]
    public async Task RequiredBrowserNeverFallsBackToAuthenticatedHttp()
    {
        using var http = new HttpClient(new Handler(_ => throw new Xunit.Sdk.XunitException("Authenticated HTTP escaped")));
        var api = new SoundCloudWebClient(http) { Session = Session(), RequireBrowserTransport = true };
        await Assert.ThrowsAsync<SoundCloudException>(() => api.SearchAsync("test"));
        await Assert.ThrowsAsync<SoundCloudException>(() => api.SetLikedAsync(7, 42, true));
    }

    private static WebSession Session() => new("publicid", "web-token", "Pookie-Test/1.0");
    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
