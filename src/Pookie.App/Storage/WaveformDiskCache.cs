using System.Security.Cryptography;
using System.Text;

namespace Pookie.App.Storage;

internal sealed class WaveformDiskCache(AppDataPaths paths)
{
    public const int MaxEntrySize = 256 * 1024;
    private string FileFor(string url) => Path.Combine(paths.Waveforms, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".json");
    public async Task<byte[]?> ReadAsync(string url, CancellationToken token)
    {
        try
        {
            var file = new FileInfo(FileFor(url));
            if (!file.Exists || file.Length is <= 0 or > MaxEntrySize || file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) return null;
            return await File.ReadAllBytesAsync(file.FullName, token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
    public async Task WriteAsync(string url, byte[] bytes, CancellationToken token)
    {
        if (bytes.Length is <= 0 or > MaxEntrySize) return;
        var file = FileFor(url);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, file, overwrite: true);
            long size = 0;
            foreach (var entry in new DirectoryInfo(paths.Waveforms).EnumerateFiles("*.json").OrderByDescending(entry => entry.LastWriteTimeUtc))
            {
                size += entry.Length;
                if (size > 32 * 1024 * 1024 || entry.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) entry.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
