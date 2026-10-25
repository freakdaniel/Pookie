using System.Security.Cryptography;
using Pookie.App.Messages;
using Pookie.App.Storage;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatGifCacheTests
{
    private static readonly byte[] gif = Convert.FromBase64String("R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBCAABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7");
    private const string url = "https://static.klipy.com/fixture.gif";
    private static ChatMediaReference Reference() => new('p', "12345678", RandomNumberGenerator.GetBytes(16));

    [Fact]
    public async Task ReadersShareOneDownloadAndRestartUsesEncryptedAccountCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "pookie-gif-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(root); var disk = new ChatMediaDiskCache(paths, 42); var reference = Reference();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            var loader = new ChatGifMediaCache(disk, async (_, token) =>
            { Interlocked.Increment(ref calls); started.TrySetResult(); await ready.Task.WaitAsync(token); return gif; }, CancellationToken.None);
            using var firstCancellation = new CancellationTokenSource();
            var first = loader.GetAsync(reference, url, 42, 90, CancellationToken.None, firstCancellation.Token);
            var second = loader.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstCancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            ready.SetResult(); Assert.Equal(gif, await second); Assert.Equal(1, calls);
            var encrypted = await disk.ReadAsync(reference.Code + ":gif", CancellationToken.None);
            Assert.NotNull(encrypted); Assert.False(encrypted.AsSpan(0, 6).SequenceEqual(gif.AsSpan(0, 6)));
            var restarted = new ChatGifMediaCache(new(paths, 42), (_, _) => throw new IOException("Offline fixture"), CancellationToken.None);
            Assert.Equal(gif, await restarted.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None));
            var other = new ChatGifMediaCache(new(paths, 7), (_, _) => throw new IOException("Offline fixture"), CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => other.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FavoriteGifSurvivesMediaClearAndCorruptCacheIsRefetched()
    {
        var root = Path.Combine(Path.GetTempPath(), "pookie-gif-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(root); var disk = new ChatMediaDiskCache(paths, 42); var reference = Reference();
            var calls = 0;
            var loader = new ChatGifMediaCache(disk, (_, _) => { calls++; return Task.FromResult(gif); }, CancellationToken.None);
            await loader.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None);
            await disk.PutAsync(reference.Code + ":gif", [1, 2, 3], CancellationToken.None);
            Assert.Equal(gif, await loader.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None));
            Assert.Equal(2, calls);
            await disk.SetPinnedAsync([reference.Code]); await disk.ClearAsync();
            var restoredDisk = new ChatMediaDiskCache(paths, 42);
            await restoredDisk.SetPinnedAsync([reference.Code]);
            var restarted = new ChatGifMediaCache(restoredDisk, (_, _) => throw new IOException("Offline fixture"), CancellationToken.None);
            Assert.Equal(gif, await restarted.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None));
            await restoredDisk.SetPinnedAsync([]); await disk.ClearAsync();
            await Assert.ThrowsAsync<IOException>(() => restarted.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LastCancelledReaderCancelsWorkAndAllowsFreshRetry()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var loader = new ChatGifMediaCache(null, async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) > 1) return gif;
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.SetResult(); }
            return gif;
        }, CancellationToken.None);
        var reference = Reference(); using var stop = new CancellationTokenSource();
        var first = loader.GetAsync(reference, url, 42, 90, CancellationToken.None, stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(gif, await loader.GetAsync(reference, url, 42, 90, CancellationToken.None, CancellationToken.None));
        Assert.Equal(2, calls);
    }
}
