using System.Security.Cryptography;
using System.Text;

namespace Pookie.App.Storage;

internal sealed class ImageDiskCache(AppDataPaths paths)
{
    private const long MaxSize = 128 * 1024 * 1024;
    private const int MaxImageSize = 4 * 1024 * 1024;
    private string FileFor(string url) => Path.Combine(paths.Images, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".image");

    public async Task<byte[]?> ReadAsync(string url, CancellationToken token)
    {
        try
        {
            var file = new FileInfo(FileFor(url));
            if (!file.Exists || file.Length is <= 0 or > MaxImageSize || file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) return null;
            return await File.ReadAllBytesAsync(file.FullName, token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task WriteAsync(string url, byte[] bytes, CancellationToken token)
    {
        if (bytes.Length is <= 0 or > MaxImageSize) return;
        var file = FileFor(url);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, file, overwrite: true);
            Prune();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public void Prune()
    {
        try
        {
            var files = new DirectoryInfo(paths.Images).EnumerateFiles().OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
            long size = 0;
            foreach (var file in files)
            {
                if (file.Extension == ".tmp")
                {
                    if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)) file.Delete();
                    continue;
                }
                size += file.Length;
                if (size > MaxSize || file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) file.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
