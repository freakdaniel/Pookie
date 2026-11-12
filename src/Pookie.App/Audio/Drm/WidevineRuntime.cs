using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pookie.App.Storage;
using Pookie.Logging;

namespace Pookie.Audio;

// Pookie owns this optional runtime. No browser installation or browser profile
// is consulted. Downloading a CDM does not grant a Widevine integration license.
internal static class WidevineRuntime
{
    private static readonly SemaphoreSlim installGate = new(1, 1);
    private static readonly HttpClient downloadHttp = new(new HttpClientHandler
        { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(3) };

    internal static string DirectoryPath => Path.Combine(AppDataPaths.DefaultRoot(), "Components", "Widevine");

    internal static Task<WidevineCandidate> ResolveAsync(CancellationToken token) =>
        ResolveAsync(downloadHttp, DirectoryPath, WidevinePackage.CurrentPlatform(), token);

    internal static async Task<WidevineCandidate> ResolveAsync(HttpClient http, string root, WidevinePlatform platform, CancellationToken token)
    {
        var installed = await ReadCurrentAsync(root, platform, token);
        if (installed != null) return installed;
        // A new consent card always uses freshly obtained vendor terms. Do not
        // fall back to a bundled/stale text if Google cannot be reached.
        var package = await WidevinePackage.LatestAsync(http, platform, token);
        var bytes = await DownloadVerifiedAsync(http, package, token);
        using var zip = OpenArchive(bytes);
        ValidateEntries(zip);
        var entry = zip.GetEntry("LICENSE") ?? throw new InvalidDataException("Widevine archive has no license.");
        if (entry.Length is <= 0 or > 64 * 1024) throw new InvalidDataException("Invalid Widevine license size.");
        using var content = new StreamContent(entry.Open());
        var licenseBytes = await HttpRangeStream.ReadBoundedAsync(content, 64 * 1024, token);
        if (licenseBytes.Length != entry.Length) throw new InvalidDataException("Incomplete Widevine license.");
        var start = licenseBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var license = new UTF8Encoding(false, true).GetString(licenseBytes, start, licenseBytes.Length - start);
        if (string.IsNullOrWhiteSpace(license)) throw new InvalidDataException("Widevine license is empty.");
        return new(package, license, bytes);
    }

    internal static Task<string> InstallAsync(WidevineCandidate candidate, CancellationToken token) =>
        InstallCandidateAsync(downloadHttp, DirectoryPath, candidate, token);

    internal static async Task<string> InstallCandidateAsync(HttpClient http, string root, WidevineCandidate candidate, CancellationToken token)
    {
        var library = await InstallAsync(http, root, candidate.Package, candidate.LicenseText, token, candidate.Archive);
        var temporary = Path.Combine(root, ".current-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(candidate.Package, WidevinePackageJson.Default.WidevinePackage), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, Path.Combine(root, "current.json"), overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return library;
    }

    private static async Task<WidevineCandidate?> ReadCurrentAsync(string root, WidevinePlatform platform, CancellationToken token)
    {
        try
        {
            var package = JsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(root, "current.json"), token), WidevinePackageJson.Default.WidevinePackage)
                ?? throw new InvalidDataException("Missing Widevine package descriptor.");
            package.Validate();
            if (package.Rid != platform.Rid || package.Library != platform.Library) return null;
            var directory = Path.Combine(root, package.Rid, package.Version);
            var license = await File.ReadAllTextAsync(Path.Combine(directory, "LICENSE"), token);
            if (!new WidevineConsent(root).IsAccepted(license) || !await IsInstalledAsync(directory, package, license, token)) return null;
            return new(package, license, null);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException) { return null; }
    }

    internal static Task<string> GetAsync(CancellationToken token) => Task.Run(async () =>
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var candidate = await ResolveAsync(timeout.Token);
            return await InstallAsync(candidate, timeout.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (DrmPlaybackException) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or
            UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException)
        {
            AppLog.Failure("Pookie.Audio.Drm", "Не удалось подготовить модуль Widevine", error);
            throw new DrmPlaybackException("Не удалось загрузить или проверить модуль защищённого воспроизведения Widevine. Проверь подключение к интернету и повтори попытку.");
        }
    }, token);

    internal static async Task<string> InstallAsync(HttpClient http, string root, WidevinePackage package, string licenseText, CancellationToken token, byte[]? archive = null)
    {
        new WidevineConsent(root).RequireAccepted(licenseText);
        package.Validate();
        await installGate.WaitAsync(token);
        try
        {
            AppDataPaths.CreatePrivateDirectory(root);
            using var buildLock = await AcquireLockAsync(Path.Combine(root, "install.lock"), token);
            var destination = Path.Combine(root, package.Rid, package.Version);
            var library = Path.Combine(destination, package.Library);
            if (await IsInstalledAsync(destination, package, licenseText, token)) return library;

            AppLog.For("Pookie.Audio.Drm").Information("Устанавливаем модуль защищённого воспроизведения Widevine {Version} ({Runtime})", package.Version, package.Rid);
            var work = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
            AppDataPaths.CreatePrivateDirectory(work);
            try
            {
                var bytes = archive ?? await DownloadVerifiedAsync(http, package, token);
                VerifyArchive(bytes, package);
                var staging = Path.Combine(work, "runtime");
                AppDataPaths.CreatePrivateDirectory(staging);
                await ExtractAsync(bytes, staging, package, token);
                ValidateManifest(staging, package, licenseText);
                var libraryHash = await HashLibraryAsync(Path.Combine(staging, package.Library), token);
                await WriteMarkerAsync(staging, package, licenseText, libraryHash, token);
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var previous = Path.Combine(work, "previous");
                var hadPrevious = Directory.Exists(destination);
                if (hadPrevious) Directory.Move(destination, previous);
                try { Directory.Move(staging, destination); }
                catch
                {
                    if (hadPrevious) Directory.Move(previous, destination);
                    throw;
                }
                AppLog.For("Pookie.Audio.Drm").Information("Модуль Widevine {Version} установлен", package.Version);
                return library;
            }
            finally { Directory.Delete(work, recursive: true); }
        }
        finally { installGate.Release(); }
    }

    private static async Task<byte[]> DownloadVerifiedAsync(HttpClient http, WidevinePackage package, CancellationToken token)
    {
        package.Validate();
        var bytes = await DownloadAsync(http, package, token);
        VerifyArchive(bytes, package);
        return bytes;
    }

    private static void VerifyArchive(byte[] bytes, WidevinePackage package)
    {
        if (bytes.Length != package.Size || Convert.ToHexStringLower(SHA256.HashData(bytes)) != package.Sha256)
            throw new InvalidDataException("Widevine archive SHA-256 mismatch or incorrect size.");
    }

    private static async Task<byte[]> DownloadAsync(HttpClient http, WidevinePackage package, CancellationToken token)
    {
        var uri = package.Url;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!WidevinePackage.AllowedDownload(uri)) throw new InvalidDataException("Unexpected Widevine download host.");
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                uri = response.Headers.Location is { } location ? new Uri(uri, location)
                    : throw new InvalidDataException("Widevine download redirect has no location.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } size && size != package.Size)
                throw new InvalidDataException("Unexpected Widevine archive size.");
            var bytes = await HttpRangeStream.ReadBoundedAsync(response.Content, package.Size, token);
            if (bytes.Length != package.Size) throw new InvalidDataException("Incomplete Widevine archive.");
            return bytes;
        }
        throw new InvalidDataException("Too many Widevine download redirects.");
    }

    private static ZipArchive OpenArchive(byte[] bytes)
    {
        // The hash from Google's HTTPS updater covers both the CRX3 header and ZIP.
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("Cr24"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 3)
            throw new InvalidDataException("Expected a Widevine CRX3 archive.");
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (headerSize > bytes.Length - 12) throw new InvalidDataException("Invalid Widevine CRX3 header.");
        var start = 12 + (int)headerSize;
        return new ZipArchive(new MemoryStream(bytes, start, bytes.Length - start, writable: false), ZipArchiveMode.Read);
    }

    private static void ValidateEntries(ZipArchive zip)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name.Contains('\\') || name.StartsWith('/') || name.Split('/').Any(part => part is "." or "..") ||
                !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Unsafe path or link in Widevine archive.");
        }
    }

    private static async Task ExtractAsync(byte[] bytes, string staging, WidevinePackage package, CancellationToken token)
    {
        using var zip = OpenArchive(bytes);
        ValidateEntries(zip);
        var required = new HashSet<string>([package.Library, "manifest.json", "LICENSE"], StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (!required.Remove(name)) continue;
            var maximum = name == package.Library ? 64 * 1024 * 1024 : 64 * 1024;
            if (entry.Length is <= 0 || entry.Length > maximum) throw new InvalidDataException("Invalid Widevine archive entry size.");
            var target = Path.GetFullPath(Path.Combine(staging, name));
            if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Widevine archive entry escapes staging.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            await using var output = File.Create(target);
            var buffer = new byte[64 * 1024];
            long written = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, token);
                if (count == 0) break;
                written += count;
                if (written > entry.Length) throw new InvalidDataException("Widevine archive entry exceeds its declared size.");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            if (written != entry.Length) throw new InvalidDataException("Incomplete Widevine archive entry.");
        }
        if (required.Count != 0) throw new InvalidDataException("Missing Widevine runtime files.");
    }

    private static void ValidateManifest(string directory, WidevinePackage package, string licenseText)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var manifest = json.RootElement;
        if (manifest.GetProperty("version").GetString() != package.Version ||
            !manifest.GetProperty("x-cdm-interface-versions").GetString()!.Split(',').Contains(CdmAbi.Version.ToString()))
            throw new InvalidDataException("Incompatible Widevine component manifest.");
        if (new FileInfo(Path.Combine(directory, package.Library)).Length == 0 || !File.Exists(Path.Combine(directory, "LICENSE")))
            throw new InvalidDataException("Incomplete Widevine installation.");
        if (WidevineConsent.Fingerprint(File.ReadAllText(Path.Combine(directory, "LICENSE"))) != WidevineConsent.Fingerprint(licenseText))
            throw new InvalidDataException("Widevine license differs from the accepted terms. Review the new terms before installing.");
    }

    private static async Task<bool> IsInstalledAsync(string directory, WidevinePackage package, string licenseText, CancellationToken token)
    {
        try
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, ".pookie-widevine.json"), token));
            var marker = json.RootElement;
            if (marker.GetProperty("version").GetString() != package.Version || marker.GetProperty("rid").GetString() != package.Rid ||
                marker.GetProperty("archiveSha256").GetString() != package.Sha256 ||
                marker.GetProperty("licenseSha256").GetString() != WidevineConsent.Fingerprint(licenseText)) return false;
            ValidateManifest(directory, package, licenseText);
            return marker.GetProperty("librarySha256").GetString() == await HashLibraryAsync(Path.Combine(directory, package.Library), token);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return false; }
    }

    private static async Task<string> HashLibraryAsync(string file, CancellationToken token)
    {
        await using var input = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token));
    }

    private static async Task WriteMarkerAsync(string directory, WidevinePackage package, string licenseText, string libraryHash, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        using (var json = new Utf8JsonWriter(bytes))
        {
            json.WriteStartObject();
            json.WriteString("version", package.Version); json.WriteString("rid", package.Rid);
            json.WriteString("archiveSha256", package.Sha256); json.WriteString("librarySha256", libraryHash);
            json.WriteString("licenseSha256", WidevineConsent.Fingerprint(licenseText));
            json.WriteEndObject();
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, ".pookie-widevine.json"), bytes.ToArray(), token);
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken token)
    {
        var wait = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (wait.Elapsed < TimeSpan.FromMinutes(5)) { await Task.Delay(100, token); }
        }
    }
}
