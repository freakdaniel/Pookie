using Pookie.App.Storage;

namespace Pookie.App.Auth;

// Login and the background website reuse one profile; fixtures use isolated temporary profiles.
internal sealed class BrowserProfile : IDisposable
{
    public string Path { get; }
    private readonly bool temporary;
    private readonly FileStream lease;
    private const string LeaseFile = ".pookie-profile.lock";
    private BrowserProfile(string path, bool temporary)
    {
        Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)); this.temporary = temporary;
        AppDataPaths.CreatePrivateDirectory(Path);
        try { lease = new FileStream(System.IO.Path.Combine(Path, LeaseFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new InvalidOperationException("Профиль SoundCloud уже открыт другим окном Pookie. Закрой его и повтори действие.", error); }
    }

    public static BrowserProfile Open(string? fixture = null, string? path = null) => new(
        path ?? (fixture != null ? Directory.CreateTempSubdirectory("pookie-browser-test-").FullName : DefaultPath()),
        fixture != null && path == null);

    public static string DefaultPath() => System.IO.Path.Combine(new AppDataPaths().Browser,
        OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") != "infiniframe" ? "WebKit" : "InfiniFrame");

    public async Task ResetAsync(CancellationToken token)
    {
        // Hold the same lease through cleanup and login so another Pookie process cannot reuse this profile.
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(Path))
                    {
                        token.ThrowIfCancellationRequested();
                        if (System.IO.Path.GetFileName(entry) == LeaseFile) continue;
                        var target = System.IO.Path.GetFullPath(entry);
                        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                        if (!string.Equals(System.IO.Path.GetDirectoryName(target), Path, comparison))
                            throw new InvalidOperationException("Очистка вышла за пределы профиля Pookie.");
                        if (Directory.Exists(target)) Directory.Delete(target, recursive: true); else File.Delete(target);
                    }
                }, token).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (OperatingSystem.IsWindows() && attempt < 29 && error is IOException or UnauthorizedAccessException)
            {
                // WebView2 can retain profile files briefly after its awaited host exits.
                // Retry only file access failures, bounded to three seconds, before opening a fresh login.
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }

    public static void Clear() => Clear(new AppDataPaths());

    internal static void Clear(AppDataPaths paths)
    {
        foreach (var engine in new[] { "WebKit", "InfiniFrame" })
        {
            var path = System.IO.Path.Combine(paths.Browser, engine);
            if (!Directory.Exists(path)) continue;
            using var profile = Open(path: path);
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (System.IO.Path.GetFileName(entry) == ".pookie-profile.lock") continue;
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true); else File.Delete(entry);
            }
        }
    }

    public void Dispose()
    {
        lease.Dispose();
        if (temporary)
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
