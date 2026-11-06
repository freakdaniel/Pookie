using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class HttpTransportTests
{
    private static WebSession Session() => new("webid", "private-token", "Pookie-Test/1") { DataDomeClientId = "initial", AppVersion = "123", AppLocale = "ru" };
    private static Uri Api(string path = "/me") => new("https://api-v2.soundcloud.com" + path);
    private static HttpResponseMessage Json(string body = "{}", int status = 200) => new((HttpStatusCode)status) { Content = new StringContent(body) };

    [Fact]
    public async Task OrdinaryReadsAndWritesNeverStartBrowserAndRotateProtection()
    {
        var account = Session(); var browser = new Verification(); var changes = 0; var count = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("private-token", request.Headers.Authorization?.Parameter);
            Assert.Equal(++count == 1 ? "initial" : "rotated", request.Headers.GetValues("x-datadome-clientid").Single());
            Assert.Contains("client_id=webid", request.RequestUri!.Query);
            Assert.Equal("api-v2.soundcloud.com", request.RequestUri.Host);
            Assert.Contains("app_version=123", request.RequestUri.Query);
            var response = Json("{\"id\":7}"); response.Headers.Add("x-set-cookie", "datadome=rotated; Path=/; Secure");
            return Task.FromResult(response);
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, browser);
        transport.SessionChanged += () => changes++;
        using var me = await transport.GetJsonAsync(Api());
        await transport.SetLikedAsync(7, 42, true);
        Assert.Equal(0, browser.Calls); Assert.Equal(2, count); Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(259, 403)]
    [InlineData(3, 200)]
    [InlineData(1, 403)]
    [InlineData(2, 403)]
    public async Task OnlyExplicitChallengesRecoverThroughBrowser(int flag, int status)
    {
        var account = Session(); var browser = new Verification();
        using var http = new HttpClient(new Handler((_, _) =>
        { var response = Json("{\"secret\":\"not-logged\"}", status); response.Headers.Add("X-DD-B", flag.ToString()); return Task.FromResult(response); }));
        using var transport = new SoundCloudHttpTransport(http, () => account, browser);
        await transport.SetLikedAsync(7, 42, true);
        Assert.Equal(1, browser.Calls);
    }

    [Theory]
    [InlineData(2, SoundCloudChallengeKind.HardBlock)]
    [InlineData(259, SoundCloudChallengeKind.InvisibleDeviceCheck)]
    public async Task HeaderFlagsAreClassifiedWithoutExposingBodies(int flag, SoundCloudChallengeKind kind)
    {
        using var http = new HttpClient(new Handler((_, _) =>
        { var response = Json("{\"secret\":\"not-logged\"}", 403); response.Headers.Add("X-SF-CC-X-dd-b", flag.ToString()); return Task.FromResult(response); }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.GetJsonAsync(Api()));
        Assert.Equal(kind, error.ChallengeKind); Assert.True(error.RequiresBrowserVerification);
        Assert.DoesNotContain("not-logged", error.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task OrdinaryFailuresNeverReplayWritesOrUseBrowser(int status)
    {
        var calls = 0; var browser = new Verification();
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Json("{\"private\":\"response\"}", status)); }));
        using var transport = new SoundCloudHttpTransport(http, Session, browser);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(7, 42, true));
        Assert.Equal(status, error.StatusCode); Assert.Equal(1, calls); Assert.Equal(0, browser.Calls);
    }

    [Fact]
    public async Task AmbiguousNetworkFailureNeverReplaysAWrite()
    {
        var calls = 0; var browser = new Verification();
        using var http = new HttpClient(new Handler((_, _) => { calls++; throw new HttpRequestException("connection closed after sending"); }));
        using var transport = new SoundCloudHttpTransport(http, Session, browser);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SetLikedAsync(7, 42, true));
        Assert.Equal(1, calls); Assert.Equal(0, browser.Calls);
    }

    [Fact]
    public async Task RetryAfterStopsOtherOperationsUntilDeadlineWithoutSleeping()
    {
        var clock = new ManualClock(); var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            if (++calls > 1) return Task.FromResult(Json());
            var response = Json("{}", 429); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)); return Task.FromResult(response);
        }));
        using var transport = new SoundCloudHttpTransport(http, Session, timeProvider: clock);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(7, 42, true));
        Assert.Equal(clock.GetUtcNow().AddMinutes(2), error.RetryAfterUtc);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetRepostedAsync(42, true));
        Assert.Equal(1, calls);
        clock.Now = clock.Now.AddMinutes(2);
        await transport.SetRepostedAsync(42, true); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConcurrentIdenticalReadsShareRequestAndCancellationBelongsToEachWaiter()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        using var http = new HttpClient(new Handler((_, token) => { Interlocked.Increment(ref calls); entered.TrySetResult(); return response.Task.WaitAsync(token); }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        using var cancel = new CancellationTokenSource();
        var first = transport.GetJsonAsync(Api(), cancel.Token); await entered.Task;
        var second = transport.GetJsonAsync(Api()); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        response.SetResult(Json("{\"id\":7}"));
        using var document = await second;
        Assert.Equal(7, document.RootElement.GetProperty("id").GetInt32()); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RequestsQueuedBehindConcurrencyLimitAreCancelledBeforeSending()
    {
        var calls = 0; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) =>
        { if (Interlocked.Increment(ref calls) == 4) entered.SetResult(); await release.Task.WaitAsync(token); return Json(); }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        var pending = Enumerable.Range(1, 4).Select(id => transport.SetLikedAsync(7, id, true)).ToArray(); await entered.Task;
        using var cancellation = new CancellationTokenSource();
        var queued = transport.SetLikedAsync(7, 100, true, cancellation.Token); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.SetResult(); await Task.WhenAll(pending); Assert.Equal(4, calls);
    }

    [Theory]
    [InlineData("https://evil.test/me")]
    [InlineData("https://api-v2.soundcloud.com:444/me")]
    [InlineData("https://api-v2.soundcloud.com/me#fragment")]
    public async Task UntrustedReadsNeverReceiveCredentials(string url)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Credentials escaped")));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.GetJsonAsync(new(url)));
    }

    [Fact]
    public async Task GraphQlAndCommentsUseNativeTransport()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal("graph.soundcloud.com", request.RequestUri!.Host);
            Assert.Equal("webi", request.Headers.GetValues("apollographql-client-name").Single());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            var variables = json.RootElement.GetProperty("variables");
            if (++requests == 1) Assert.Equal("soundcloud:tracks:42", variables.GetProperty("trackUrn").GetString());
            else Assert.Equal("hello", variables.GetProperty("input").GetProperty("body").GetString());
            return Json("{\"data\":{}}");
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        using var read = await transport.ReadTrackAsync(new("comments", 42));
        using var written = await transport.WriteCommentAsync(new("create", 42, "hello"));
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task PlaylistAppendSavesWebClientOrderWithoutLosingTracks()
    {
        var count = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (++count == 1) return Json("{\"id\":11,\"user\":{\"id\":7},\"track_count\":1,\"tracks\":[{\"id\":90}]}");
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/playlists/11", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            Assert.Equal(new long[] { 90, 42 }, body.RootElement.GetProperty("playlist").GetProperty("tracks").EnumerateArray().Select(t => t.GetInt64()));
            return Json();
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await transport.AddToPlaylistAsync(7, 11, 42); Assert.Equal(2, count);
    }

    [Fact]
    public async Task ChatVerifiesAccountAndDoesNotSendOnMismatch()
    {
        var count = 0;
        using var http = new HttpClient(new Handler((_, _) => { count++; return Task.FromResult(Json("{\"id\":8}")); }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await Assert.ThrowsAsync<SoundCloudException>(() => transport.RequestChatAsync(new("send", 7, 9, "private")));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task HistoryReportsOneRealPlayEventAndReusesAuthenticatedAccount()
    {
        var meReads = 0; var plays = 0; var history = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/me") { meReads++; return Json("{\"id\":7}"); }
            if (request.Method == HttpMethod.Get) return Json("{\"id\":42,\"user_id\":9,\"duration\":180000,\"track_authorization\":\"track-auth\"}");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            if (path == "/me/play-history") { history++; Assert.Equal("soundcloud:tracks:42", body.RootElement.GetProperty("track_urn").GetString()); }
            else
            {
                plays++; var payload = body.RootElement.GetProperty("events")[0].GetProperty("payload");
                Assert.Equal("soundcloud:users:7", payload.GetProperty("user").GetString());
                Assert.Equal("soundcloud:playlists:11", payload.GetProperty("in_playlist").GetString());
                Assert.Equal("track-auth", payload.GetProperty("track_authorization").GetString());
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await transport.RecordPlaybackAsync(42, "soundcloud:playlists:11");
        await transport.RecordPlaybackAsync(42, "soundcloud:playlists:11");
        Assert.Equal(1, meReads); Assert.Equal(2, history); Assert.Equal(2, plays);
    }

    [Fact]
    public async Task RegionalListenUsesFreshAccountAuthorizationAndFullTrackLengthInHistory()
    {
        var plays = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal("private-token", request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("api-v2.soundcloud.com", request.RequestUri.Host);
            if (request.Method == HttpMethod.Get) return path == "/me" ? Json("{\"id\":7}") : Json("""
                {"id":42,"user_id":9,"policy":"SNIP","duration":30000,"full_duration":114625,"track_authorization":"fresh-account-auth"}
                """);
            if (path == "/me")
            {
                plays++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
                Assert.Equal("private-token", body.RootElement.GetProperty("auth_token").GetString());
                var payload = body.RootElement.GetProperty("events")[0].GetProperty("payload");
                Assert.Equal(114625, payload.GetProperty("track_length").GetDouble());
                Assert.Equal("fresh-account-auth", payload.GetProperty("track_authorization").GetString());
                Assert.Equal("soundcloud:users:7", payload.GetProperty("user").GetString());
            }
            return new(HttpStatusCode.NoContent);
        }));
        using var transport = new SoundCloudHttpTransport(http, Session);
        await transport.RecordPlaybackAsync(42);
        Assert.Equal(1, plays);
    }

    [Fact]
    public async Task BrowserSignsFollowButHttpSendsTheMutation()
    {
        var browser = new Verification(); var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++; Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/me/followings/9", request.RequestUri!.AbsolutePath);
            Assert.Contains("signature=fixture-signature", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        using var transport = new SoundCloudHttpTransport(http, Session, browser);
        await transport.SetFollowingAsync(7, 9, true);
        Assert.Equal(1, browser.Signatures); Assert.Equal(0, browser.Calls); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AConcurrentCheckRotatesSessionOnceAndOtherRejectedRequestsResumeNatively()
    {
        var account = Session(); var browser = new Verification { Account = account };
        var bothSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var initial = 0; var retried = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.Headers.GetValues("x-datadome-clientid").Single() == "verified")
            { Interlocked.Increment(ref retried); return Json(); }
            if (Interlocked.Increment(ref initial) == 2) bothSent.SetResult();
            await bothSent.Task.WaitAsync(token);
            var response = Json("{}", 403); response.Headers.Add("X-DD-B", "259"); return response;
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, browser);
        await Task.WhenAll(transport.SetLikedAsync(7, 42, true), transport.SetLikedAsync(7, 43, true));
        Assert.Equal(1, browser.Calls); Assert.Equal(2, initial); Assert.Equal(1, retried);
    }

    [Fact]
    public async Task RejectedNativeSessionDoesNotLoopThroughBrowserForTheSameOperation()
    {
        var browser = new Verification(); var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        { calls++; var response = Json("{}", 403); response.Headers.Add("X-DD-B", "259"); return Task.FromResult(response); }));
        using var transport = new SoundCloudHttpTransport(http, Session, browser);
        await transport.SetLikedAsync(7, 42, true);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.SetLikedAsync(7, 42, true));
        Assert.True(error.RequiresBrowserVerification); Assert.Equal(1, browser.Calls); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AlreadyCancelledReadDoesNotStartHttpOrBrowser()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Cancelled request sent")));
        using var transport = new SoundCloudHttpTransport(http, Session);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.GetJsonAsync(Api(), cancellation.Token));
    }

    [Fact]
    public void ProtectionRotationPreservesPendingActionKeysButAccountChangesDoNot()
    {
        var account = Session();
        var pending = new HashSet<(WebSession Account, long Track)> { (account, 42) };
        account.UpdateDataDomeClientId("rotated");
        Assert.True(pending.Remove((account, 42)));
        Assert.Equal(account, account with { DataDomeClientId = "another-cookie" });
        Assert.NotEqual(account, account with { AccessToken = "other-account-token" });
    }

    [Fact]
    public async Task QueuedMutationCannotMoveToAnotherAccount()
    {
        var account = Session(); var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) =>
        { if (Interlocked.Increment(ref calls) == 4) entered.SetResult(); await release.Task.WaitAsync(token); return Json(); }));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        var pending = Enumerable.Range(1, 4).Select(id => transport.SetLikedAsync(7, id, true)).ToArray(); await entered.Task;
        var queued = transport.SetLikedAsync(7, 100, true);
        account = account with { AccessToken = "another-account-token" };
        release.SetResult(); await Task.WhenAll(pending);
        await Assert.ThrowsAsync<SoundCloudException>(() => queued); Assert.Equal(4, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class Verification : ISoundCloudBrowserTransport
    {
        public int Calls;
        public int Signatures;
        public WebSession? Account;
        public Task<string> GetFollowSignatureAsync(long userId, long artistId, CancellationToken cancellationToken = default)
        { Signatures++; return Task.FromResult("fixture-signature"); }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(JsonDocument.Parse("{}")); }
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default)
        { Calls++; Account?.UpdateDataDomeClientId("verified"); return Task.CompletedTask; }
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
