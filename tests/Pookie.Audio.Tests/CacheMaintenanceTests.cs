using Pookie.App.Storage;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class CacheMaintenanceTests
{
    [Fact]
    public async Task ClearRemovesOnlyDerivedCachesAndKeepsDurableAndActiveAudioData()
    {
        var root = Path.Combine(Path.GetTempPath(), "pookie-cache-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(root);
            var derived = new[] { paths.Images, paths.Waveforms, Path.Combine(paths.Cache, "Lyrics") };
            var durable = new[] { paths.Browser, paths.Config, Path.Combine(paths.Root, "Chats"),
                Path.Combine(paths.Cache, "Audio"), Path.Combine(paths.Root, "Recommendations") };
            foreach (var directory in derived.Concat(durable))
            { Directory.CreateDirectory(directory); await File.WriteAllBytesAsync(Path.Combine(directory, "entry.json"), new byte[13]); }
            var service = new CacheMaintenance(paths);
            var size = await service.InspectAsync(); Assert.Equal(39, size.TotalBytes);
            var cleared = await service.ClearAsync();
            Assert.Equal(39, cleared.DeletedBytes); Assert.Equal(0, cleared.RemainingBytes);
            Assert.All(derived, directory => Assert.Empty(Directory.GetFiles(directory)));
            Assert.All(durable, directory => Assert.True(File.Exists(Path.Combine(directory, "entry.json"))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ClearSerializesWithActiveImageAndWaveformWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "pookie-cache-active-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(root);
            var images = new ImageDiskCache(paths); var waves = new WaveformDiskCache(paths);
            var service = new CacheMaintenance(paths);
            var payload = new byte[4096];
            await Task.WhenAll(Enumerable.Range(0, 24).SelectMany(index => new Task[] {
                images.WriteAsync("image:" + index, payload, CancellationToken.None),
                waves.WriteAsync("wave:" + index, payload, CancellationToken.None),
                service.ClearAsync() }));
            await service.ClearAsync();
            Assert.Equal(0, (await service.InspectAsync()).TotalBytes);
            Assert.Null(await images.ReadAsync("image:0", CancellationToken.None));
            Assert.Null(await waves.ReadAsync("wave:0", CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ClearDoesNotFollowALinkToDurableData()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "pookie-cache-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(root);
            var persistent = Path.Combine(paths.Browser, "session.json");
            await File.WriteAllTextAsync(persistent, "account");
            Directory.Delete(paths.Images);
            Directory.CreateSymbolicLink(paths.Images, paths.Browser);
            await new CacheMaintenance(paths).ClearAsync();
            Assert.Equal("account", await File.ReadAllTextAsync(persistent));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
