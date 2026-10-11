using System.Net;
using System.Net.Http.Headers;
using Pookie.Logging;

namespace Pookie.Audio;

// Encoded media only, scoped to one selected track. Seeks reuse these files;
// decoded PCM and DRM keys never enter this cache.
internal sealed class EncodedTrackCache(HttpClient http) : IDisposable
{
    private static readonly string CacheRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Pookie", "AudioCache"));
    internal sealed record Request(Uri Uri, int Maximum, long? Offset = null, long? Length = null, long? Total = null);
    private sealed record Key(string Uri, long? Offset, long? Length);
    private readonly object gate = new();
    private readonly Dictionary<Key, Task<string>> files = [];
    private readonly Dictionary<string, long> progressiveLengths = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly string directory = Path.Combine(CacheRoot, Guid.NewGuid().ToString("N"));
    private Task prefetch = Task.CompletedTask;
    private bool disposed;
    private bool prefetchFailed;
    internal string DirectoryPath => directory;
    public Task Completion { get; private set; } = Task.CompletedTask;

    private static Key CacheKey(Request request) => new(request.Uri.AbsoluteUri, request.Offset, request.Length);

    public bool Contains(Request request)
    {
        lock (gate) return files.TryGetValue(CacheKey(request), out var file) && file.IsCompletedSuccessfully;
    }

    public async Task<byte[]> ReadAsync(Request request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = await GetFile(request).WaitAsync(token);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new MemoryStream();
        await input.CopyToAsync(output, token);
        return output.ToArray();
    }

    private Task<string> GetFile(Request request)
    {
        Task<string> file;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var key = CacheKey(request);
            if (!files.TryGetValue(key, out file!) || file.IsFaulted || file.IsCanceled)
                files[key] = file = DownloadAsync(request);
        }
        return file;
    }

    private async Task<string> DownloadAsync(Request request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
        if (request.Offset is { } start && request.Length is { } count)
            message.Headers.Range = new RangeHeaderValue(start, checked(start + count - 1));
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
        response.EnsureSuccessStatusCode();
        if (request.Offset is { } offset && request.Length is { } length &&
            (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != offset ||
             response.Content.Headers.ContentRange?.To != offset + length - 1 ||
             request.Total is { } total && response.Content.Headers.ContentRange?.Length != total))
            throw new IOException("Server did not honor the media byte range.");
        var bytes = await HttpRangeStream.ReadBoundedAsync(response.Content, request.Maximum, lifetime.Token);
        if (request.Length is { } expected && bytes.Length != expected) throw new IOException("Incomplete media byte range.");
        return await StoreAsync(bytes);
    }

    private async Task<string> StoreAsync(byte[] bytes)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(path, bytes, lifetime.Token);
        return path;
    }

    public async Task StoreFirstAsync(Uri uri, long total, byte[] bytes, CancellationToken token)
    {
        Task<string> file;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var key = new Key(uri.AbsoluteUri, 0, bytes.Length);
            if (!files.TryGetValue(key, out file!)) files[key] = file = StoreAsync(bytes);
            progressiveLengths[uri.AbsoluteUri] = total;
        }
        await file.WaitAsync(token);
    }

    public async Task<(byte[] Data, long Total)?> ReadFirstAsync(Uri uri, int blockSize, CancellationToken token)
    {
        long total;
        lock (gate) if (!progressiveLengths.TryGetValue(uri.AbsoluteUri, out total)) return null;
        return (await ReadAsync(new(uri, blockSize, 0, Math.Min(total, blockSize), total), token), total);
    }

    public long ContiguousEnd(Uri uri, long position, long total, int blockSize)
    {
        var start = Math.Max(0, position - 1) / blockSize * blockSize;
        var end = start;
        while (end < total && Contains(new(uri, blockSize, end, Math.Min(blockSize, total - end), total)))
            end += Math.Min(blockSize, total - end);
        return end == start ? 0 : end;
    }

    public void PrefetchRanges(Uri uri, long position, long total, int blockSize, double duration, CancellationToken token)
    {
        var start = Math.Max(0, position) / blockSize * blockSize;
        // Match the browser's 45-second lookahead while bounding each window to 8 MiB.
        var ahead = double.IsFinite(duration) && duration > 0 ? Math.Clamp(total / duration * 45, blockSize, 8 * 1024 * 1024) : blockSize * 2;
        var end = Math.Min(total, start + (long)ahead);
        var requests = new List<Request>();
        for (var offset = start; offset < end; offset += blockSize)
            requests.Add(new(uri, blockSize, offset, Math.Min(blockSize, total - offset), total));
        Prefetch(requests, token);
    }

    public void Prefetch(IReadOnlyList<Request> requests, CancellationToken token)
    {
        lock (gate)
        {
            if (disposed || prefetchFailed || !prefetch.IsCompleted || requests.All(Contains)) return;
            prefetch = Task.Run(async () =>
            {
                try
                {
                    foreach (var request in requests)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!Contains(request)) await GetFile(request).WaitAsync(token);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || lifetime.IsCancellationRequested)
                { AppLog.For("Pookie.Audio").Debug("Опережающая загрузка отменена"); }
                catch (ObjectDisposedException) when (disposed)
                { AppLog.For("Pookie.Audio").Debug("Кэш трека закрыт"); }
                catch (Exception)
                {
                    lock (gate) prefetchFailed = true;
                    AppLog.For("Pookie.Audio").Debug("Опережающая загрузка недоступна; воспроизведение продолжит обычную загрузку");
                }
            });
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            Completion = CleanupAsync(files.Values.Cast<Task>().Append(prefetch).ToArray());
        }
    }

    private async Task CleanupAsync(Task[] pending)
    {
        try { await Task.WhenAll(pending); }
        catch (Exception) { AppLog.For("Pookie.Audio").Debug("Загрузка кэша завершилась до получения всех данных"); }
        try
        {
            var target = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(target), CacheRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Audio cache cleanup target is outside its root.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { AppLog.For("Pookie.Audio").Warning("Не удалось очистить временный аудиокэш"); }
        lifetime.Dispose();
    }
}
