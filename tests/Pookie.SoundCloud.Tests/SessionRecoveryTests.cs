using System.Net;
using System.Text.Json;
using Pookie.SoundCloud;
using Xunit;

namespace Pookie.SoundCloud.Tests;

public sealed class SessionRecoveryTests
{
    private static WebSession Session() => new("webid", "fixture-token", "Fixture/1") { DataDomeClientId = "initial" };
    private static HttpResponseMessage Reply(int status = 204, int? challenge = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}") };
        if (challenge != null) response.Headers.Add("X-DD-B", challenge.Value.ToString());
        return response;
    }

    [Fact]
    public async Task PreparationTransfersProtectionAndBothLikesStayNative()
    {
        var account = Session(); var attempts = 0; var writes = 0;
        var browser = new Browser(_ => { account.UpdateDataDomeClientId("prepared"); return Task.CompletedTask; });
        using var http = new HttpClient(new Handler((request, _) =>
        {
            attempts++;
            if (request.Headers.GetValues("x-datadome-clientid").Single() == "initial") return Task.FromResult(Reply(403, 259));
            writes++; return Task.FromResult(Reply());
        }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        var stages = new List<SoundCloudRecoveryStage>(); native.RecoveryObserved += message => stages.Add(message.Stage);
        await native.SetLikedAsync(7, 42, true);
        await native.SetLikedAsync(7, 43, true);
        Assert.Equal(3, attempts); Assert.Equal(2, writes); Assert.Equal(1, browser.Preparations); Assert.Equal(0, browser.Writes);
        Assert.Equal([SoundCloudRecoveryStage.PreparingSession, SoundCloudRecoveryStage.SessionPrepared,
            SoundCloudRecoveryStage.NativeRetry, SoundCloudRecoveryStage.NativeAccepted], stages);
    }

    [Fact]
    public async Task ConcurrentUnauthorizedRequestsShareTokenRotationAndRetryOnce()
    {
        var account = Session() with { VerifiedUserId = 7 };
        var rejected = 0;
        var accepted = 0;
        var bothRejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new Browser(_ => { account = account with { AccessToken = "rotated-token" }; return Task.CompletedTask; });
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.Headers.Authorization?.Parameter == "rotated-token")
            {
                Interlocked.Increment(ref accepted);
                return Reply();
            }
            if (Interlocked.Increment(ref rejected) == 2) bothRejected.SetResult();
            await bothRejected.Task.WaitAsync(token);
            return Reply(401);
        }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(native.SetLikedAsync(7, 42, true, timeout.Token), native.SetLikedAsync(7, 43, true, timeout.Token));
        Assert.Equal(2, rejected);
        Assert.Equal(2, accepted);
        Assert.Equal(1, browser.Preparations);
        Assert.Equal(0, browser.Writes);
    }

    [Fact]
    public async Task BrowserFallbackNeverReplaysCompletedMutationAndPreparationIsNotRepeated()
    {
        var account = Session(); var attempts = 0;
        var browser = new Browser(_ => Task.CompletedTask);
        using var http = new HttpClient(new Handler((_, _) => { attempts++; return Task.FromResult(Reply(403, 259)); }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        var stages = new List<SoundCloudRecoveryStage>(); native.RecoveryObserved += message => stages.Add(message.Stage);
        await native.SetLikedAsync(7, 42, true);
        Assert.Equal(2, attempts); Assert.Equal(1, browser.Writes);
        await Assert.ThrowsAsync<SoundCloudException>(() => native.SetLikedAsync(7, 42, true));
        await native.SetLikedAsync(7, 43, true);
        Assert.Equal(4, attempts); Assert.Equal(2, browser.Writes); Assert.Equal(1, browser.Preparations);
        Assert.Equal(1, stages.Count(stage => stage == SoundCloudRecoveryStage.NativeRetry));
        Assert.Equal(2, stages.Count(stage => stage == SoundCloudRecoveryStage.BrowserCompleted));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task OrdinaryFailureAfterPreparationDoesNotSendBrowserMutation(int status)
    {
        var account = Session(); var attempts = 0;
        var browser = new Browser(_ => Task.CompletedTask);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(++attempts == 1 ? Reply(403, 259) : Reply(status))));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => native.SetLikedAsync(7, 42, true));
        Assert.Equal(status, error.StatusCode); Assert.Equal(2, attempts); Assert.Equal(0, browser.Writes);
    }

    [Fact]
    public async Task AmbiguousNetworkFailureAfterPreparationNeverFallsBackToBrowser()
    {
        var account = Session(); var attempts = 0;
        var browser = new Browser(_ => Task.CompletedTask);
        using var http = new HttpClient(new Handler((_, _) => ++attempts == 1
            ? Task.FromResult(Reply(403, 259)) : throw new HttpRequestException("Write may have arrived.")));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        await Assert.ThrowsAsync<HttpRequestException>(() => native.SetLikedAsync(7, 42, true));
        Assert.Equal(2, attempts); Assert.Equal(0, browser.Writes);
    }

    [Fact]
    public async Task PreparationCancellationStopsBeforeNativeRetryOrBrowserWrite()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var account = Session(); var attempts = 0;
        var browser = new Browser(token => { entered.SetResult(); return pending.Task.WaitAsync(token); });
        using var http = new HttpClient(new Handler((_, _) => { attempts++; return Task.FromResult(Reply(403, 259)); }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        using var cancellation = new CancellationTokenSource();
        var action = native.SetLikedAsync(7, 42, true, cancellation.Token);
        await entered.Task; cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action);
        Assert.Equal(1, attempts); Assert.Equal(0, browser.Writes);
    }

    [Fact]
    public async Task PreparationCannotMovePendingMutationToAnotherAccount()
    {
        var account = Session(); var attempts = 0;
        var browser = new Browser(_ => { account = account with { AccessToken = "another-account" }; return Task.CompletedTask; });
        using var http = new HttpClient(new Handler((_, _) => { attempts++; return Task.FromResult(Reply(403, 259)); }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        await Assert.ThrowsAsync<SoundCloudException>(() => native.SetLikedAsync(7, 42, true));
        Assert.Equal(1, attempts); Assert.Equal(0, browser.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentRejectedWritesSharePreparationAndBothResumeNatively(bool rotateCookie)
    {
        var account = Session(); var initial = 0; var written = 0; var warmed = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new Browser(_ =>
        {
            if (rotateCookie) account.UpdateDataDomeClientId("prepared");
            Volatile.Write(ref warmed, true); return Task.CompletedTask;
        });
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (Volatile.Read(ref warmed))
            { Interlocked.Increment(ref written); return Reply(); }
            if (Interlocked.Increment(ref initial) == 2) entered.SetResult();
            await entered.Task.WaitAsync(token); return Reply(403, 259);
        }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        await Task.WhenAll(native.SetLikedAsync(7, 42, true), native.SetLikedAsync(7, 43, true));
        Assert.Equal(2, initial); Assert.Equal(2, written); Assert.Equal(1, browser.Preparations); Assert.Equal(0, browser.Writes);
    }

    [Fact]
    public async Task NativeBlockAfterPreparationAllowsTheBrowserToCompleteTheAction()
    {
        var account = Session(); var attempts = 0;
        var browser = new Browser(_ => Task.CompletedTask);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Reply(403, ++attempts == 1 ? 259 : 2))));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        await native.SetLikedAsync(7, 42, true);
        Assert.Equal(2, attempts); Assert.Equal(1, browser.Preparations); Assert.Equal(1, browser.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeBlockStillPreparesTheBrowserAfterProtectionRotates(bool rotateProtection)
    {
        var account = Session(); var attempts = 0;
        var browser = new Browser(_ => Task.CompletedTask);
        using var http = new HttpClient(new Handler((_, _) =>
        {
            var response = Reply(403, ++attempts == 1 && rotateProtection ? 259 : 2);
            if (attempts == 1 && rotateProtection)
                response.Headers.Add("x-set-cookie", "datadome=rotated; Path=/; Secure");
            return Task.FromResult(response);
        }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        await native.SetLikedAsync(7, 42, true);
        Assert.Equal(rotateProtection ? 3 : 2, attempts);
        Assert.Equal(1, browser.Preparations); Assert.Equal(1, browser.Writes);
    }

    [Fact]
    public async Task BlockInTheBrowserStopsBeforeAnotherNativeRequestOrMutation()
    {
        var account = Session(); var attempts = 0;
        var rejection = new SoundCloudException("Browser is blocked.", 403, requiresBrowserVerification: true)
            { ChallengeKind = SoundCloudChallengeKind.HardBlock };
        var browser = new Browser(_ => throw rejection);
        using var http = new HttpClient(new Handler((_, _) =>
        {
            attempts++;
            return Task.FromResult(Reply(403, 2));
        }));
        using var native = new SoundCloudHttpTransport(http, () => account, browser);
        var error = await Assert.ThrowsAsync<SoundCloudException>(() => native.SetLikedAsync(7, 42, true));
        Assert.Same(rejection, error);
        Assert.Equal(1, attempts); Assert.Equal(1, browser.Preparations); Assert.Equal(0, browser.Writes);
    }

    private sealed class Browser(Func<CancellationToken, Task> prepare) : ISoundCloudBrowserTransport, ISoundCloudSessionVerifier
    {
        public int Preparations;
        public int Writes;
        public Task PrepareSessionAsync(CancellationToken cancellationToken = default) { Preparations++; return prepare(cancellationToken); }
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected browser read.");
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default)
        { Writes++; return Task.CompletedTask; }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
