using System.Collections.Concurrent;

namespace Pookie.App.Storage;

internal sealed record CacheUsage(long ImagesBytes, long WaveformsBytes, long LyricsBytes)
{
    internal long TotalBytes => ImagesBytes + WaveformsBytes + LyricsBytes;
}

internal sealed record CacheClearResult(long DeletedBytes, long RemainingBytes);

// Every cache reader/writer uses the same directory lease. Clearing keeps the
// directory in place, and never races a temporary-file commit or cache pruning.
internal static class CacheDirectoryGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static async ValueTask<IDisposable> EnterAsync(string directory, CancellationToken token)
    {
        var gate = gates.GetOrAdd(Path.GetFullPath(directory), _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        return new Lease(gate);
    }

    internal static IDisposable Enter(string directory)
    {
        var gate = gates.GetOrAdd(Path.GetFullPath(directory), _ => new(1, 1));
        gate.Wait();
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? owner = gate;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
}

internal sealed class CacheMaintenance(AppDataPaths paths)
{
    private string[] Directories => [paths.Images, paths.Waveforms, Path.Combine(paths.Cache, "Lyrics")];

    internal Task<CacheUsage> InspectAsync(CancellationToken token = default) => Task.Run(async () =>
    {
        var sizes = new List<long>();
        foreach (var directory in Directories)
        {
            using var lease = await CacheDirectoryGate.EnterAsync(directory, token).ConfigureAwait(false);
            sizes.Add(Size(directory));
        }
        return new CacheUsage(sizes[0], sizes[1], sizes[2]);
    }, token);

    internal Task<CacheClearResult> ClearAsync(CancellationToken token = default) => Task.Run(async () =>
    {
        long deleted = 0, remaining = 0;
        foreach (var directory in Directories)
        {
            using var lease = await CacheDirectoryGate.EnterAsync(directory, token).ConfigureAwait(false);
            foreach (var file in Files(directory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var size = file.Length;
                    file.Delete(); deleted += size;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            remaining += Size(directory);
        }
        return new CacheClearResult(deleted, remaining);
    }, token);

    private static long Size(string directory)
    {
        long size = 0;
        foreach (var file in Files(directory))
            try { size += file.Length; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return size;
    }

    private static FileInfo[] Files(string directory)
    {
        try
        {
            var folder = new DirectoryInfo(directory);
            // Cache cleanup never follows links, descends into unknown folders,
            // or reaches durable browser, chat, account and recommendation data.
            return !folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0
                ? [] : folder.GetFiles().Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
}
