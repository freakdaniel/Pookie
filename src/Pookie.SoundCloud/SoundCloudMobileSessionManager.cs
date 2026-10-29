namespace Pookie.SoundCloud;

public interface ISoundCloudSessionRefresher
{
    Task<WebSession> RefreshAsync(WebSession expected, bool force, CancellationToken cancellationToken = default);
}

public sealed class SoundCloudMobileSessionManager(SoundCloudMobileOAuth oauth, Func<WebSession> current,
    Action<WebSession> publish, Func<WebSession, CancellationToken, Task>? persist = null, TimeProvider? timeProvider = null)
    : ISoundCloudSessionRefresher, IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private Task<WebSession>? pending;
    private bool revoked;
    private DateTimeOffset retryAfter;
    public event Action? PersistenceFailed;

    public Task<WebSession> RefreshAsync(WebSession expected, bool force, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var account = current();
            if (!expected.Equals(account) || revoked || account.RefreshToken == null)
                return Task.FromException<WebSession>(new SoundCloudException("Мобильная сессия SoundCloud завершилась. Войди снова.", 401));
            if (account.AccessToken != expected.AccessToken || !force && account.ExpiresAtUtc > clock.GetUtcNow().AddMinutes(1)) return Task.FromResult(account);
            if (pending is { IsCompleted: false }) return pending.WaitAsync(cancellationToken);
            if (retryAfter > clock.GetUtcNow()) return Task.FromException<WebSession>(new SoundCloudOAuthException(SoundCloudOAuthFailure.RateLimited) { RetryAfterUtc = retryAfter });
            pending = RefreshCoreAsync(account, lifetime.Token);
            return pending.WaitAsync(cancellationToken);
        }
    }

    private async Task<WebSession> RefreshCoreAsync(WebSession previous, CancellationToken token)
    {
        try
        {
            var updated = await oauth.RefreshAsync(previous, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!current().Equals(previous)) throw new SoundCloudException("Сессия SoundCloud изменилась.", 401);
            // Publish first: a storage failure must never cause reuse of the
            // single-use refresh token which the server has already consumed.
            publish(updated);
            if (persist != null)
                try { await persist(updated, token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                { PersistenceFailed?.Invoke(); }
            return updated;
        }
        catch (SoundCloudOAuthException error) when (error.Failure is SoundCloudOAuthFailure.InvalidGrant or SoundCloudOAuthFailure.InvalidResponse)
        {
            lock (gate) revoked = true;
            throw new SoundCloudException("Мобильная сессия SoundCloud завершилась. Войди снова.", 401);
        }
        catch (SoundCloudOAuthException error) when (error.Failure == SoundCloudOAuthFailure.RateLimited)
        {
            lock (gate) retryAfter = error.RetryAfterUtc > clock.GetUtcNow() ? error.RetryAfterUtc.Value : clock.GetUtcNow().AddSeconds(30);
            throw;
        }
        catch (SoundCloudOAuthException error) when (error.Failure == SoundCloudOAuthFailure.ServerUnavailable)
        {
            lock (gate) retryAfter = clock.GetUtcNow().AddSeconds(30);
            throw;
        }
        catch (HttpRequestException)
        {
            lock (gate) retryAfter = clock.GetUtcNow().AddSeconds(30);
            throw;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            lock (gate) retryAfter = clock.GetUtcNow().AddSeconds(30);
            throw new SoundCloudOAuthException(SoundCloudOAuthFailure.ServerUnavailable);
        }
    }

    public void Dispose() => lifetime.Cancel();
}
