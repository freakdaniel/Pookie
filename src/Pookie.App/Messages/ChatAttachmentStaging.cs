using Pookie.App.Storage;

namespace Pookie.App.Messages;

// Only clipboard images need a temporary path. User-selected originals are never deleted.
internal sealed class ChatAttachmentStaging : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pookie-attachments-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> owned = [];
    private int nextImage;
    internal string AddImage(byte[] bytes, string extension)
    {
        if (bytes.Length > ChatMediaCrypto.MaxFileBytes || !ChatMediaCrypto.IsRasterImage(bytes))
            throw new InvalidOperationException("Не удалось вставить изображение: максимум 16 МБ на файл.");
        if (owned.Count >= 32) throw new InvalidOperationException("Слишком много неотправленных вложений. Отправь или убери предыдущие.");
        AppDataPaths.CreatePrivateDirectory(directory);
        var path = Path.Combine(directory, $"Изображение {++nextImage}" + (extension is ".jpg" or ".gif" or ".webp" ? extension : ".png"));
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var output = new FileStream(path, options)) output.Write(bytes);
        owned.Add(path); return path;
    }
    internal void Keep(IEnumerable<string> paths)
    {
        var keep = paths.ToHashSet();
        foreach (var path in owned.Where(p => !keep.Contains(p)).ToArray())
        {
            try { File.Delete(path); owned.Remove(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public void Dispose()
    {
        Keep([]);
        try { if (Directory.Exists(directory)) Directory.Delete(directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
