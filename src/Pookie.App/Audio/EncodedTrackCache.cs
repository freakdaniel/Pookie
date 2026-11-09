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
    private readonly Dictionary<string, ProgressiveTrackDownload> sequential = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly string directory = Path.Combine(CacheRoot, Guid.NewGuid().ToString("N"));
    private readonly object directorySync = new();
    private FileStream? directoryLease;
    private static readonly Lazy<Task> orphanCleanup = new(() => Task.Run(() => PruneOrphans(CacheRoot,DateTime.UtcNow.AddDays(-7))));
    private Task prefetch = Task.CompletedTask;
    private Task seekPrefetch = Task.CompletedTask;
    private bool disposed;
    private readonly HashSet<string> progressivePrefetch = [];
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

    internal async Task ReadRangeAsync(Request request, long offset, Memory<byte> destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = await GetFile(request).WaitAsync(token).ConfigureAwait(false);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.RandomAccess);
        input.Position = offset;
        await input.ReadExactlyAsync(destination, token).ConfigureAwait(false);
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
        var bytes = await AudioHttp.ReadAsync(http, request.Uri, request.Maximum, lifetime.Token, request.Offset, request.Length, request.Total);
        return await StoreAsync(bytes);
    }

    private string CreatePath()
    {
        lock (directorySync)
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            directoryLease ??= new FileStream(Path.Combine(directory,".active"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        _ = orphanCleanup.Value;
        return Path.Combine(directory, Guid.NewGuid().ToString("N"));
    }

    private async Task<string> StoreAsync(byte[] bytes)
    {
        var path = CreatePath();
        await File.WriteAllBytesAsync(path, bytes, lifetime.Token);
        return path;
    }

    internal Stream? OpenSequential(Uri uri, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return sequential.TryGetValue(uri.AbsoluteUri, out var download) ? download.Open(token) : null;
        }
    }

    internal Stream StartSequential(Uri uri, HttpResponseMessage response, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!sequential.TryGetValue(uri.AbsoluteUri, out var download))
                sequential[uri.AbsoluteUri] = download = new(http, uri, CreatePath(), response, lifetime.Token);
            else response.Dispose();
            return download.Open(token);
        }
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
        lock (gate) if (disposed || token.IsCancellationRequested || !progressivePrefetch.Add(uri.AbsoluteUri)) return;
        var start = Math.Min(total, Math.Max(0, position)) / blockSize * blockSize;
        var requests = new List<Request>();
        for (var offset = start; offset < total; offset += blockSize)
            requests.Add(new(uri, blockSize, offset, Math.Min(blockSize, total - offset), total));
        for (var offset = 0L; offset < start; offset += blockSize)
            requests.Add(new(uri, blockSize, offset, Math.Min(blockSize, total - offset), total));
        Prefetch(requests, token);
    }

    public void Prefetch(IReadOnlyList<Request> requests, CancellationToken token, bool seeking = false, int concurrency = 1)
    {
        lock (gate)
        {
            ref var pending = ref (seeking ? ref seekPrefetch : ref prefetch);
            if (disposed || !pending.IsCompleted || requests.All(Contains)) return;
            pending = Task.Run(async () =>
            {
                try
                {
                    if (concurrency > 1)
                        await Parallel.ForEachAsync(requests, new ParallelOptions
                        {
                            MaxDegreeOfParallelism = concurrency, CancellationToken = lifetime.Token
                        }, async (request, stop) => await WarmAsync(request, stop));
                    else
                        foreach (var request in requests) await WarmAsync(request, lifetime.Token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || lifetime.IsCancellationRequested)
                { AppLog.For("Pookie.Audio").Debug("Опережающая загрузка отменена"); }
                catch (ObjectDisposedException) when (disposed)
                { AppLog.For("Pookie.Audio").Debug("Кэш трека закрыт"); }
                catch (Exception)
                {
                    AppLog.For("Pookie.Audio").Debug("Опережающая загрузка недоступна; воспроизведение продолжит обычную загрузку");
                }
            });
        }
    }

    private async ValueTask WarmAsync(Request request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Contains(request)) await GetFile(request);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            var pendingCleanup = files.Values.Cast<Task>().Concat(sequential.Values.Select(download => download.Completion))
                .Append(prefetch).Append(seekPrefetch).ToArray();
            Completion = Task.Run(() => CleanupAsync(pendingCleanup));
        }
    }

    private async Task CleanupAsync(Task[] pending)
    {
        try { await Task.WhenAll(pending); }
        catch (Exception) { AppLog.For("Pookie.Audio").Debug("Загрузка кэша завершилась до получения всех данных"); }
        try
        {
            lock(directorySync) { directoryLease?.Dispose(); directoryLease = null; }
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
    internal static void PruneOrphans(string root,DateTime cutoff)
    {
        try
        {
            if(!Directory.Exists(root))return;
            foreach(var path in Directory.EnumerateDirectories(root))
            {
                var info=new DirectoryInfo(path);
                if(!Guid.TryParseExact(info.Name,"N",out _) || info.LinkTarget!=null || info.LastWriteTimeUtc>=cutoff)continue;
                var marker=Path.Combine(path,".active");
                // Legacy directories have no ownership proof. Never guess that
                // another running version has stopped using them.
                if(!File.Exists(marker))continue;
                try
                {
                    using(var lease=new FileStream(marker,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
                    Directory.Delete(path,recursive:true);
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
            }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
    }
}
