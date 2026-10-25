using System.Security.Cryptography;
using System.Text;
using Pookie.App.Storage;

namespace Pookie.App.Messages;

/// <summary>Account-scoped ciphertext only. Filenames never contain URLs, keys, names or message text.</summary>
internal sealed class ChatMediaDiskCache
{
    internal const long DefaultBudget = 512L * 1024 * 1024;
    private readonly string directory, favorites;
    private HashSet<string> pinned = [];
    private long pinRevision;
    private readonly long budget;
    private readonly SemaphoreSlim io = new(1, 1);
    internal ChatMediaDiskCache(AppDataPaths paths, long account, long budget = DefaultBudget)
    { if (account <= 0) throw new ArgumentOutOfRangeException(nameof(account)); directory = Path.Combine(paths.Cache, "Chats", account.ToString(System.Globalization.CultureInfo.InvariantCulture), "Media"); favorites = Path.Combine(paths.Root, "Chats", account.ToString(System.Globalization.CultureInfo.InvariantCulture), "FavoriteMedia"); this.budget = Math.Max(0, budget); }
    private static string NameFor(string code)
    {
        var gif = code.EndsWith(":gif", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gif ? code[..^4] : code))) + (gif ? ".gif.bin" : ".bin");
    }
    private static bool IsPinned(string name, HashSet<string> names) => names.Contains(name) || name.EndsWith(".gif.bin", StringComparison.Ordinal) && names.Contains(name[..^8] + ".bin");
    private string PathFor(string code) => Path.Combine(IsPinned(NameFor(code), pinned) ? favorites : directory, NameFor(code));
    internal async Task<byte[]?> ReadAsync(string code, CancellationToken token, int maximumBytes = int.MaxValue)
    {
        await io.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var path = PathFor(code); if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > ChatMediaArchive.MaxEncryptedBytes) { File.Delete(path); return null; }
            if (new FileInfo(path).Length > maximumBytes) return null;
            var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false); File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return bytes;
        }
        catch (IOException) { return null; }
        finally { io.Release(); }
    }
    internal async Task PutAsync(string code, byte[] encrypted, CancellationToken token)
    {
        if (encrypted.Length > budget && !IsPinned(NameFor(code), pinned)) return;
        await io.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var path = PathFor(code); AppDataPaths.CreatePrivateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            try
            {
                await using (var stream = new FileStream(temporary, options)) { await stream.WriteAsync(encrypted, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Trim();
        }
        finally { io.Release(); }
    }
    internal Task SetPinnedAsync(IEnumerable<string> codes)
    {
        var names = codes.Select(NameFor).ToHashSet(StringComparer.Ordinal); var revision = Interlocked.Increment(ref pinRevision);
        return Task.Run(async () =>
        {
            await io.WaitAsync().ConfigureAwait(false);
            try
            {
                if (revision != Volatile.Read(ref pinRevision)) return;
                AppDataPaths.CreatePrivateDirectory(favorites); AppDataPaths.CreatePrivateDirectory(directory);
                foreach (var temporary in Directory.EnumerateFiles(directory, "*.tmp").Concat(Directory.EnumerateFiles(favorites, "*.tmp"))) File.Delete(temporary);
                foreach (var source in Directory.EnumerateFiles(directory, "*.bin"))
                    if (IsPinned(Path.GetFileName(source), names)) File.Move(source, Path.Combine(favorites, Path.GetFileName(source)), true);
                foreach (var file in Directory.EnumerateFiles(favorites, "*.bin"))
                    if (!IsPinned(Path.GetFileName(file), names)) File.Move(file, Path.Combine(directory, Path.GetFileName(file)), true);
                pinned = names; Trim();
            }
            finally { io.Release(); }
        });
    }
    private void Trim()
    {
        var files = Directory.Exists(directory) ? new DirectoryInfo(directory).GetFiles("*.bin").OrderBy(f => f.LastWriteTimeUtc).ToArray() : [];
        var total = files.Sum(f => f.Length); foreach (var file in files) { if (total <= budget) break; total -= file.Length; file.Delete(); }
    }
    internal async Task<long> SizeAsync()
    { await io.WaitAsync().ConfigureAwait(false); try { return Directory.Exists(directory) ? new DirectoryInfo(directory).GetFiles("*.bin").Sum(f => f.Length) : 0; } finally { io.Release(); } }
    internal async Task ClearAsync()
    { await io.WaitAsync().ConfigureAwait(false); try { if (Directory.Exists(directory)) foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); } finally { io.Release(); } }
}
