using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Linux libVLC installation requires Linux.");
    if (args.Length != 4 || args[0] != "--output" || args[2] != "--root")
        throw new ArgumentException("Usage: dotnet run --project tools/libvlc-linux -- --output <directory> --root <repository>");
    await PrepareAsync(Path.GetFullPath(args[1]), Path.GetFullPath(args[3]));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Linux libVLC: {error.Message}");
    return 1;
}

static RuntimeConfig ReadConfig()
{
    using var stream = typeof(RuntimeConfig).Assembly.GetManifestResourceStream("runtime.json")!;
    var config = JsonSerializer.Deserialize<RuntimeConfig>(stream)!;
    if (!Regex.IsMatch(config.version, "^[1-9][0-9]*$") || config.rid != "linux-x64" ||
        config.asset != "libvlc-linux-x64.tar.gz")
        throw new InvalidDataException("Invalid Linux libVLC runtime configuration.");
    return config;
}

static bool IsHash(string? value) => value is not null && Regex.IsMatch(value, "\\A[a-f0-9]{64}\\z");

static string ReadChecksum(string file, RuntimeConfig config)
{
    var match = Regex.Match(File.ReadAllText(file).Trim(), "\\A([a-f0-9]{64})  (\\S+)\\z");
    if (!match.Success || match.Groups[2].Value != config.asset)
        throw new InvalidDataException("Invalid Linux libVLC checksum file.");
    return match.Groups[1].Value;
}

static void ValidateBundle(string directory, RuntimeConfig config)
{
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
    if (manifest.RootElement.GetProperty("version").GetString() != config.version ||
        manifest.RootElement.GetProperty("rid").GetString() != config.rid)
        throw new InvalidDataException("Linux libVLC archive version/architecture does not match runtime.json.");
    foreach (var file in new[] { "libvlc.so", "libvlc.so.5", "libvlccore.so", "libvlccore.so.9",
        "vlc/plugins/codec/libavcodec_plugin.so", "vlc/plugins/demux/libavformat_plugin.so", "licenses/README.txt" })
        if (!File.Exists(Path.Combine(directory, file)))
            throw new InvalidDataException($"Missing Linux libVLC runtime file: {file}");
}

static bool Cached(string destination, RuntimeConfig config, string? expected)
{
    try
    {
        using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, ".pookie-runtime.json")));
        var value = marker.RootElement;
        var hash = value.GetProperty("sha256").GetString();
        if (value.GetProperty("version").GetString() != config.version ||
            value.GetProperty("rid").GetString() != config.rid || !IsHash(hash) ||
            (expected is not null && hash != expected)) return false;
        ValidateBundle(destination, config);
        return true;
    }
    catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
    {
        return false;
    }
}

static async Task<FileStream> AcquireLockAsync(string path)
{
    var wait = Stopwatch.StartNew();
    while (true)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) when (wait.Elapsed < TimeSpan.FromMinutes(10)) { await Task.Delay(100); }
    }
}

static async Task PrepareAsync(string destination, string root)
{
    var config = ReadConfig();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    using var buildLock = await AcquireLockAsync(destination + ".lock");
    var archive = Environment.GetEnvironmentVariable("POOKIE_LIBVLC_ARCHIVE");
    if (string.IsNullOrEmpty(archive))
        archive = new[] { "artifacts/libvlc-runtime", "artifacts/libvlc-linux" }
            .Select(directory => Path.Combine(root, directory, config.asset)).FirstOrDefault(File.Exists);
    var expected = Environment.GetEnvironmentVariable("POOKIE_LIBVLC_SHA256");
    if (string.IsNullOrEmpty(expected))
        expected = archive is not null && File.Exists(archive + ".sha256") ? ReadChecksum(archive + ".sha256", config) : null;
    if ((archive is not null && expected is null) || (expected is not null && !IsHash(expected)))
        throw new InvalidDataException("POOKIE_LIBVLC_ARCHIVE requires POOKIE_LIBVLC_SHA256 (64 lowercase hex characters) or an adjacent .sha256 file.");
    if (Cached(destination, config, expected))
    {
        Console.WriteLine($"Using cached Linux libVLC v{config.version}: {destination}");
        return;
    }
    if (archive is null)
        throw new FileNotFoundException("No packaged Linux libVLC runtime is available. Download the libvlc-linux-x64 Actions artifact " +
            "into artifacts/libvlc-runtime, or set POOKIE_LIBVLC_ARCHIVE and its SHA-256. CI prepares the archive during the application build.");

    var work = Path.Combine(Path.GetDirectoryName(destination)!, "libvlc-extract-" + Guid.NewGuid());
    Directory.CreateDirectory(work);
    try
    {
        // Hash the staged copy so extraction always consumes the verified bytes.
        var checkedArchive = Path.Combine(work, config.asset);
        File.Copy(archive, checkedArchive);
        await using var input = File.OpenRead(checkedArchive);
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(input));
        if (actual != expected)
            throw new InvalidDataException($"Linux libVLC archive SHA-256 mismatch: expected {expected}, got {actual}.");
        Console.WriteLine($"Preparing Linux libVLC v{config.version} ({config.baseline})...");
        input.Position = 0;
        var staging = Path.Combine(work, "runtime");
        Directory.CreateDirectory(staging);
        try
        {
            using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
            Extract(gzip, staging);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException($"Could not extract Linux libVLC archive: {error.Message}", error);
        }
        ValidateBundle(staging, config);
        // Deterministic archives have mtime=0; refresh regular files so
        // MSBuild's PreserveNewest updates previously copied runtime files.
        var installedAt = DateTime.UtcNow;
        foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            if (new FileInfo(file).LinkTarget is null) File.SetLastWriteTimeUtc(file, installedAt);
        await File.WriteAllTextAsync(Path.Combine(staging, ".pookie-runtime.json"),
            JsonSerializer.Serialize(new { config.version, config.rid, sha256 = actual }) + "\n");
        var previous = destination + ".previous-" + Guid.NewGuid();
        var hadPrevious = Directory.Exists(destination);
        if (hadPrevious) Directory.Move(destination, previous);
        try { Directory.Move(staging, destination); }
        catch
        {
            if (hadPrevious) Directory.Move(previous, destination);
            throw;
        }
        if (hadPrevious) Directory.Delete(previous, recursive: true);
        Console.WriteLine($"Verified Linux libVLC v{config.version}: {destination}");
    }
    finally { Directory.Delete(work, recursive: true); }
}

static string ContainedPath(string root, string name)
{
    if (Path.IsPathRooted(name) || name.Split('/').Contains(".."))
        throw new InvalidDataException($"Unsafe paths in Linux libVLC archive: {name}");
    var target = Path.GetFullPath(Path.Combine(root, name));
    if (target != root && !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        throw new InvalidDataException($"Archive link escapes the Linux libVLC directory: {name}");
    return target;
}

static void Extract(Stream stream, string staging)
{
    // Defer links until all regular files are written. No archive entry can
    // redirect a later write through a link outside the staging directory.
    var links = new List<(string Path, string Target)>();
    var paths = new HashSet<string>(StringComparer.Ordinal);
    using var reader = new TarReader(stream);
    var count = 0;
    while (reader.GetNextEntry() is { } entry)
    {
        count++;
        var target = ContainedPath(staging, entry.Name);
        if (!paths.Add(target)) throw new InvalidDataException($"Duplicate archive entry: {entry.Name}");
        switch (entry.EntryType)
        {
            case TarEntryType.Directory:
                Directory.CreateDirectory(target);
                break;
            case TarEntryType.RegularFile:
            case TarEntryType.V7RegularFile:
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: false);
                break;
            case TarEntryType.SymbolicLink:
                if (Path.IsPathRooted(entry.LinkName))
                    throw new InvalidDataException($"Archive link escapes the Linux libVLC directory: {entry.Name}");
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, entry.LinkName));
                if (!resolved.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException($"Archive link escapes the Linux libVLC directory: {entry.Name}");
                links.Add((target, entry.LinkName));
                break;
            default:
                throw new InvalidDataException($"Unsupported archive entry: {entry.Name} ({entry.EntryType})");
        }
    }
    if (count == 0) throw new InvalidDataException("Empty Linux libVLC archive.");
    // The runtime only needs links to regular files, never directory links or
    // chains. Rejecting those also rules out cycles and dangling links.
    foreach (var (path, target) in links)
    {
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
        if (!File.Exists(resolved) || new FileInfo(resolved).LinkTarget is not null || links.Any(link => link.Path == resolved))
            throw new InvalidDataException($"Archive link must point to a regular runtime file: {path}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.CreateSymbolicLink(path, target);
    }
}

internal sealed record RuntimeConfig(string version, string rid, string asset, string baseline);
