using Pookie.App.Storage;

namespace Pookie.App.Auth;

// Login and the background website reuse one profile; fixtures use isolated temporary profiles.
internal sealed class BrowserProfile : IDisposable
{
    public string Path { get; }
    private readonly bool temporary;
    private readonly FileStream lease;
    private BrowserProfile(string path, bool temporary)
    {
        Path = path; this.temporary = temporary;
        AppDataPaths.CreatePrivateDirectory(path);
        try { lease = new FileStream(System.IO.Path.Combine(path, ".pookie-profile.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new InvalidOperationException("Профиль SoundCloud уже открыт другим окном Pookie. Закрой его и повтори действие.", error); }
    }

    public static BrowserProfile Open(string? fixture = null, string? path = null) => new(
        path ?? (fixture != null ? Directory.CreateTempSubdirectory("pookie-browser-test-").FullName : DefaultPath()),
        fixture != null && path == null);

    public static string DefaultPath() => System.IO.Path.Combine(new AppDataPaths().Browser,
        OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") != "infiniframe" ? "WebKit" : "InfiniFrame");

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
