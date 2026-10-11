using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Pookie.Lyrics;

// Automatic resolution is independent of the audio player and UI dispatcher.
public sealed class LyricsService : IDisposable
{
    private readonly ILyricsProvider provider;
    private readonly string cache;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, (CancellationToken Token, Lazy<Task<LyricsResult>> Request)> pending = new();
    public LyricsService(ILyricsProvider provider, string cacheDirectory, TimeProvider? timeProvider = null)
    {
        this.provider = provider; cache = cacheDirectory; time = timeProvider ?? TimeProvider.System;
        Directory.CreateDirectory(cache);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(cache, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Prune();
    }

    public async Task<LyricsResult> ResolveAsync(LyricsIdentity identity, CancellationToken token, bool force = false)
    {
        var key = identity.TrackId + ":" + identity.Fingerprint + (force ? ":force" : "");
        var entry = pending.AddOrUpdate(key,
            _ => NewRequest(), (_, previous) => previous.Token.IsCancellationRequested ? NewRequest() : previous);
        _ = entry.Request.Value.ContinueWith(_ =>
            ((ICollection<KeyValuePair<string, (CancellationToken, Lazy<Task<LyricsResult>>)>>)pending).Remove(new(key, entry)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { return await entry.Request.Value.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            if (entry.Request.IsValueCreated && entry.Request.Value.IsCompleted)
                ((ICollection<KeyValuePair<string, (CancellationToken, Lazy<Task<LyricsResult>>)>>)pending).Remove(new(key, entry));
        }
        (CancellationToken, Lazy<Task<LyricsResult>>) NewRequest() => (token, new(() => ResolveCoreAsync(identity, token, force)));
    }

    private async Task<LyricsResult> ResolveCoreAsync(LyricsIdentity identity, CancellationToken token, bool force)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token; token.ThrowIfCancellationRequested();
        var path = CachePath(identity);
        var cached = await ReadAsync(path, LyricsJson.Default.LyricsCacheRecord, token).ConfigureAwait(false);
        if (!force && cached is { Complete: true } found && found.Fingerprint == identity.Fingerprint &&
            time.GetUtcNow() - found.At < (found.Result.Status == LyricsStatus.NotFound ? TimeSpan.FromHours(24) : TimeSpan.FromDays(30)))
            return Prepare(found.Result, identity);

        var candidates = new List<LyricsCandidate>();
        var searched = new HashSet<(string Title, string Artist)>();
        var failed = false;
        try
        {
            var queries = identity.Queries();
            try
            {
                var exact = await RetryAsync(() => provider.GetAsync(queries[0], token), token).ConfigureAwait(false);
                if (exact != null) candidates.Add(exact);
            }
            catch (Exception error) when (IsProviderFailure(error, token)) { failed = true; }
            var chosen = LyricsMatcher.Choose(identity, candidates);
            if (chosen == null || chosen.ToDocument().Status == LyricsStatus.Plain || !LyricsMatcher.IsExact(identity, chosen))
            {
                // Try original metadata first. Parenthesis-free titles are a bounded
                // fallback, also allowing a plain match to be upgraded to timed lyrics.
                var fallback = identity.Queries(stripParentheses: true).Except(queries)
                    .Where(query => !string.IsNullOrWhiteSpace(query.Title)).Take(2);
                foreach (var query in queries.Take(2).Concat(fallback))
                {
                    searched.Add((query.Title, query.Artist));
                    try { candidates.AddRange(await RetryAsync(() => provider.SearchAsync(query.Title, query.Artist, token), token).ConfigureAwait(false)); }
                    catch (Exception error) when (IsProviderFailure(error, token)) { failed = true; }
                    chosen = LyricsMatcher.Choose(identity, candidates);
                    if (chosen != null && chosen.ToDocument().Status != LyricsStatus.Plain && LyricsMatcher.IsExact(identity, chosen)) break;
                }
            }
            token.ThrowIfCancellationRequested();
            LyricsResult result;
            if (chosen != null) { var document = chosen.ToDocument(); result = new(document.Status, document); }
            else result = failed ? new(LyricsStatus.Unavailable) : new(LyricsStatus.NotFound);
            if ((chosen == null || result.Status == LyricsStatus.Plain) && LyricsMatcher.SpedUpOriginal(identity) is { } original)
            {
                var originalCandidates = new List<LyricsCandidate>(candidates);
                var originalQueries = original.Queries().Concat(original.Queries(stripParentheses: true)).Distinct()
                    .Where(query => !string.IsNullOrWhiteSpace(query.Title) && !searched.Contains((query.Title, query.Artist))).Take(4);
                var timed = AdaptOriginal();
                foreach (var query in timed ? [] : originalQueries)
                {
                    try { originalCandidates.AddRange(await RetryAsync(() => provider.SearchAsync(query.Title, query.Artist, token), token).ConfigureAwait(false)); }
                    catch (Exception error) when (IsProviderFailure(error, token)) { failed = true; }
                    if (AdaptOriginal()) break;
                }
                bool AdaptOriginal()
                {
                    var adapted = LyricsMatcher.ChooseSpedUpOriginal(identity, originalCandidates);
                    if (adapted == null) return false;
                    var document = LyricsMatcher.SpedUpDocument(identity, adapted);
                    // Keep genuine version-specific text if the original only has
                    // Plain; usable adapted timestamps can upgrade a Plain result.
                    if (chosen == null || document.Status == LyricsStatus.Synced)
                    { chosen = adapted; result = new(document.Status, document); }
                    return document.Status == LyricsStatus.Synced;
                }
            }
            token.ThrowIfCancellationRequested();
            if (chosen == null) result = failed ? new(LyricsStatus.Unavailable) : new(LyricsStatus.NotFound);
            if (failed && cached is { Result.Document: not null } && cached.Fingerprint == identity.Fingerprint &&
                (chosen == null || result.Status == LyricsStatus.Plain && cached.Result.Status == LyricsStatus.Synced))
            { result = cached.Result; chosen = null; }
            // Keep usable plain text offline, but retry an incomplete timed lookup
            // instead of treating a transient failure as a finished selection.
            if (chosen != null || !failed && result.Status == LyricsStatus.NotFound)
            {
                try
                {
                    await WriteAsync(path, new LyricsCacheRecord(identity.Fingerprint, time.GetUtcNow(), result, !failed), LyricsJson.Default.LyricsCacheRecord, token).ConfigureAwait(false);
                    Prune();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            return Prepare(result, identity);
        }
        catch (Exception error) when (IsProviderFailure(error, token))
        {
            return Prepare(cached is { Result.Document: not null } && cached.Fingerprint == identity.Fingerprint ? cached.Result : new(LyricsStatus.Unavailable), identity);
        }
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> request, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return await request().ConfigureAwait(false); }
            catch (Exception error) when (attempt < 3 && IsRetryable(error, token))
            {
                var wait = TimeSpan.FromMilliseconds(attempt switch { 0 => 750, 1 => 2000, _ => 4000 });
                if (error is LyricsRateLimitException rateLimit)
                {
                    // Preserve the server's cooldown, including across other requests.
                    if (rateLimit.RetryAfter > TimeSpan.FromSeconds(30)) throw;
                    if (rateLimit.RetryAfter > wait) wait = rateLimit.RetryAfter;
                }
                await Task.Delay(wait, time, token).ConfigureAwait(false);
            }
        }
    }
    private static bool IsProviderFailure(Exception error, CancellationToken token) => error is HttpRequestException or IOException or JsonException ||
        error is OperationCanceledException && !token.IsCancellationRequested;
    private static bool IsRetryable(Exception error, CancellationToken token) => error switch
    {
        HttpRequestException http => http.StatusCode == null || http.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500,
        OperationCanceledException => !token.IsCancellationRequested,
        IOException => error is not InvalidDataException,
        JsonException => true,
        _ => false
    };
    private static LyricsResult Prepare(LyricsResult result, LyricsIdentity identity) =>
        !identity.TimingAvailable && result.Document is { Lines.Length: > 0 } document
            ? result with { Status = LyricsStatus.Plain, Document = document with { Lines = [] } } : result;
    private string CachePath(LyricsIdentity identity) => Path.Combine(cache, $"{identity.TrackId}-{identity.Fingerprint}.json");

    private static async Task<T?> ReadAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken token)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > LrclibClient.MaxResponseBytes * 2) return default;
            await using var input = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(input, type, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }
    private static async Task WriteAsync<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                await JsonSerializer.SerializeAsync(output, value, type, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    private void Prune()
    {
        try
        {
            var files = new DirectoryInfo(cache).GetFiles("*.json").OrderBy(file => file.LastWriteTimeUtc).ToArray();
            var size = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (size <= 50 * 1024 * 1024) break;
                size -= file.Length; file.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
