namespace Pookie.App.Storage;

internal sealed class AppDataPaths
{
    public string Root { get; }
    public string Config => Path.Combine(Root, "Config");
    public string Cache => Path.Combine(Root, "Cache");
    public string Downloads => Path.Combine(Root, "Downloads");
    // Website state belongs to durable data, not the disposable application cache.
    public string Browser => Path.Combine(Root, "Browser");
    public string Waveforms => Path.Combine(Cache, "Waveforms");
    public string Images => Path.Combine(Cache, "Images");

    public AppDataPaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? DefaultRoot());
        foreach (var directory in new[] { Root, Config, Cache, Downloads, Browser, Images, Waveforms }) CreatePrivateDirectory(directory);
    }

    internal static string DefaultRoot() => ResolveRoot(
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"));

    internal static string ResolveRoot(string platform, string home, string localData, string? xdgData) => platform switch
    {
        "windows" => Path.Combine(localData, "Pookie"),
        "macos" => Path.Combine(home, "Library", "Application Support", "Pookie"),
        _ => Path.Combine(!string.IsNullOrWhiteSpace(xdgData) && xdgData.StartsWith('/')
            ? xdgData : Path.Combine(home, ".local", "share"), "Pookie")
    };

    internal static void CreatePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
