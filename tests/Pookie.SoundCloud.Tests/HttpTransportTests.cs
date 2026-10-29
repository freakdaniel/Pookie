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
        var browser = new Verification();
        using var transport = new SoundCloudHttpTransport(http, Session, kind == SoundCloudChallengeKind.HardBlock ? browser : null);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => transport.GetJsonAsync(Api()));
        Assert.Equal(kind, error.ChallengeKind); Assert.True(error.RequiresBrowserVerification);
        Assert.DoesNotContain("not-logged", error.Message); Assert.Equal(0, browser.Calls);
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
    public async Task PlaylistAppendDoesNotReplaceExistingTracks()
    {
        var count = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (++count == 1) return Json("{\"id\":11,\"user\":{\"id\":7},\"track_count\":1,\"tracks\":[{\"id\":90}]}");
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/playlists/11/tracks", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            Assert.Equal("soundcloud:tracks:42", body.RootElement.GetProperty("track_urn").GetString());
            Assert.False(body.RootElement.TryGetProperty("tracks", out _)); return Json();
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
    public async Task MobileProfileUsesOwnIdentityCookieAndBatchEndpoint()
    {
        var mobile = new SoundCloudMobileIdentity("0123456789abcdef", "Google", "Pixel Test", "15");
        var account = new WebSession("mobileid", "mobile-token", mobile.UserAgent) { MobileIdentity = mobile, DataDomeClientId = "mobile-cookie", AppLocale = "ru" };
        var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(account, SoundCloudJson.Default.WebSession), SoundCloudJson.Default.WebSession)!;
        Assert.True(roundTrip.IsValid()); Assert.Equal(mobile.Udid, roundTrip.MobileIdentity!.Udid);
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal("SoundCloud/2026.10.14-release (Android 15.0.0; Google Pixel Test)", request.Headers.UserAgent.ToString());
            Assert.Equal(mobile.Udid, request.Headers.GetValues("UDID").Single());
            Assert.Equal("377050", request.Headers.GetValues("App-Version").Single());
            Assert.Equal("datadome=mobile-cookie", request.Headers.GetValues("Cookie").Single());
            Assert.False(request.Headers.Contains("x-datadome-clientid")); Assert.Null(request.Headers.Referrer);
            Assert.Contains("client_id=mobileid", request.RequestUri!.Query); Assert.DoesNotContain("app_version", request.RequestUri.Query);
            Assert.Equal("api-mobile.soundcloud.com", request.RequestUri.Host);
            Assert.Equal("/likes/tracks/create", request.RequestUri.AbsolutePath); Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            Assert.Equal("system_playlist_in_library=true", request.Headers.GetValues("App-Requested-Features").Single());
            Assert.Equal("soundcloud:tracks:42", body.RootElement.GetProperty("likes")[0].GetProperty("target_urn").GetString());
            return Json("""{"collection":[{"target_urn":"soundcloud:tracks:42"}]}""");
        }));
        using var transport = new SoundCloudHttpTransport(http, () => roundTrip);
        await transport.SetLikedAsync(7, 42, true);
        Assert.False((account with { UserAgent = "Chrome/1" }).IsValid());
        Assert.DoesNotContain("0123456789abcdef", mobile.ToString());
    }

    [Fact]
    public async Task MobileTelemetryUsesSuppliedSignalsRealResponseEventsAndNeverOAuth()
    {
        var clock = new ManualClock(); var apiCalls = 0; var telemetryCalls = 0;
        var signals = new SoundCloudMobileSignals("fixture-product", "fixture-device", "fixture-hardware", "fixture-build", "release-keys",
            "VANILLA_ICE_CREAM", 35, 1080, 2400, 3, "{\"auth\":\"false\",\"info\":\"{}\"}");
        var mobile = new SoundCloudMobileIdentity("0123456789abcdef", "Fixture", "Fixture Model", "15") { Signals = signals };
        var account = new WebSession("mobileid", "private-mobile-token", mobile.UserAgent) { MobileIdentity = mobile, DataDomeClientId = "initial" };
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "api-sdk.datadome.co")
            {
                telemetryCalls++; Assert.Null(request.Headers.Authorization);
                var body = await request.Content!.ReadAsStringAsync(token);
                Assert.DoesNotContain("private-mobile-token", body);
                var fields = body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
                Assert.Equal("android-java-manual", fields["inte"]); Assert.Equal("fixture-build", fields["fgp"]);
                Assert.Equal("0123456789abcdef", fields["d_ifv"]); Assert.Equal("3.0.14", fields["ddv"]);
                using var events = JsonDocument.Parse(fields["events"]);
                Assert.All(events.RootElement.EnumerateArray(), item =>
                { Assert.Equal(1, item.GetProperty("id").GetInt32()); Assert.Equal("response validation", item.GetProperty("message").GetString()); });
                return Json("{\"cookie\":\"datadome=telemetry-rotated; Path=/; Domain=.soundcloud.com\"}");
            }
            apiCalls++;
            Assert.Equal(apiCalls == 1 ? "datadome=initial" : "datadome=telemetry-rotated", request.Headers.GetValues("Cookie").Single());
            return Json("{\"id\":7}");
        }));
        using var transport = new SoundCloudHttpTransport(http, () => account, timeProvider: clock);
        for (var index = 0; index < 6; index++) { using var read = await transport.GetJsonAsync(Api()); }
        Assert.Equal(1, telemetryCalls);
        clock.Now = clock.Now.AddSeconds(10);
        using var final = await transport.GetJsonAsync(Api());
        Assert.Equal(2, telemetryCalls); Assert.Equal(7, apiCalls); Assert.Equal("telemetry-rotated", account.DataDomeClientId);
    }

    [Fact]
    public async Task TelemetryFailureDoesNotDiscardSuccessfulApiResponseOrSolveAChallenge()
    {
        var signals = new SoundCloudMobileSignals("p", "d", "h", "f", "release-keys", "V", 35, 1080, 2400, 3, "{}");
        var mobile = new SoundCloudMobileIdentity("0123456789abcdef", "Fixture", "Model", "15") { Signals = signals };
        var account = new WebSession("mobileid", "mobile-token", mobile.UserAgent) { MobileIdentity = mobile };
        var failures = 0;
        using var http = new HttpClient(new Handler((request, _) => request.RequestUri!.Host == "api-sdk.datadome.co"
            ? throw new HttpRequestException("telemetry unavailable") : Task.FromResult(Json("{\"id\":7}"))));
        using var transport = new SoundCloudHttpTransport(http, () => account);
        transport.Observed += observation => { if (observation.Status == 0) failures++; };
        using var me = await transport.GetJsonAsync(Api());
        Assert.Equal(7, me.RootElement.GetProperty("id").GetInt32()); Assert.Equal(1, failures); Assert.Null(account.DataDomeClientId);
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

    [Fact]
    public async Task MobileBrowserFallbackCompletesOnceWithoutFabricatingNativeLikeReceipt()
    {
        var mobile = new SoundCloudMobileIdentity("0123456789abcdef", "Pookie", "Fixture", "15");
        var account = new WebSession("mobileid", "mobile-token", mobile.UserAgent) { MobileIdentity = mobile };
        var calls = 0; var browser = new Verification();
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++; var denied = Json(status: 403); denied.Headers.Add("X-DD-B", "259"); return Task.FromResult(denied);
        }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        await native.SetLikedAsync(7, 42, true);
        Assert.Equal(1, calls); Assert.Equal(1, browser.Calls);
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
