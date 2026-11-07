using System.Net;
using System.Net.Http.Headers;

namespace Pookie.Audio;

// A dropped connection suspends the current download; cancellation still owns
// track changes, seeks and shutdown. Protocol/authorization failures stay terminal.
internal static class AudioHttp
{
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(15);

    internal static async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token,
        bool attemptTimeout = true)
    {
        var delay = 250;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (attemptTimeout) attempt.CancelAfter(StallTimeout);
            try { return await action(attempt.Token).ConfigureAwait(false); }
            catch (Exception error) when (!token.IsCancellationRequested && IsTransient(error))
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                delay = Math.Min(4000, delay * 2);
            }
        }
    }

    private static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException request => request.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)request.StatusCode >= 500,
        OperationCanceledException => true,
        IOException => true,
        _ => false
    };

    internal static Task<byte[]> ReadAsync(HttpClient http, Uri uri, int maximum, CancellationToken token,
        long? offset = null, long? length = null, long? total = null) => RetryAsync(async stop =>
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset is { } start && length is { } count)
            message.Headers.Range = new RangeHeaderValue(start, checked(start + count - 1));
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, stop).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (offset is { } from && length is { } size &&
            (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != from ||
             response.Content.Headers.ContentRange?.To != from + size - 1 ||
             total is { } expected && response.Content.Headers.ContentRange?.Length != expected))
            throw new InvalidDataException("Server did not honor the media byte range.");
        var bytes = await HttpRangeStream.ReadBoundedAsync(response.Content, maximum, stop).ConfigureAwait(false);
        if (length is { } expectedLength && bytes.Length != expectedLength) throw new IOException("Incomplete media byte range.");
        return bytes;
    }, token);
}
