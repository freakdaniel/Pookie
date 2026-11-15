using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

try
{
    if (args is ["--fingerprint", var policyDirectory])
    { Console.WriteLine(await PolicyFingerprintAsync(policyDirectory)); return 0; }
    await BuildPackagesAsync();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Could not prepare MewUI packages: " + error.Message);
    return 1;
}

static async Task BuildPackagesAsync()
{
    var directory = SourceDirectory();
    var root = Path.GetFullPath(Path.Combine(directory, "../.."));
    var revisionText = await File.ReadAllTextAsync(Path.Combine(directory, "revision.json"));
    using var revision = JsonDocument.Parse(revisionText);
    var repository = revision.RootElement.GetProperty("repository").GetString()!;
    var commit = revision.RootElement.GetProperty("commit").GetString()!;
    var version = revision.RootElement.GetProperty("packageVersion").GetString()!;
    var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pookie.Build", "MewUI");
    var checkout = Path.Combine(cache, commit);
    var feed = Path.Combine(root, "artifacts", "mewui-source", "packages");
    var stamp = Path.Combine(feed, ".ready");
    var fingerprint = await PolicyFingerprintAsync(directory);
    string[] projects = [
        "MewUI", "MewUI.Platform.Win32", "MewUI.Platform.X11", "MewUI.Platform.MacOS",
        "MewUI.Backend.Direct2D", "MewUI.Backend.Gdi", "MewUI.Backend.MewVG.Win32",
        "MewUI.Backend.MewVG.X11", "MewUI.Backend.MewVG.MacOS"
    ];
    var packages = projects.Skip(1).Prepend("MewUI.Core").Append("MewUI")
        .Select(name => Path.Combine(feed, $"Aprillz.{name}.{version}.nupkg")).ToArray();
    Directory.CreateDirectory(cache);
    using var buildLock = await AcquireBuildLockAsync(Path.Combine(cache, commit + ".lock"));
    if (packages.All(File.Exists) && File.Exists(stamp) && await File.ReadAllTextAsync(stamp) == await CacheStampAsync(fingerprint, packages))
    {
        await InvalidateExtractedPackagesAsync(packages, version);
        File.SetLastWriteTimeUtc(stamp, DateTime.UtcNow);
        return;
    }

    Console.WriteLine($"Preparing MewUI {commit}. The first restore builds its local packages.");
    Directory.CreateDirectory(feed);
    File.Delete(stamp);
    if (!Directory.Exists(Path.Combine(checkout, ".git")))
        await RunAsync("git", ["clone", "--filter=blob:none", repository, checkout]);
    var remote = await ReadAsync("git", ["-C", checkout, "remote", "get-url", "origin"]);
    if (remote.Trim() != repository) throw new InvalidOperationException("Unexpected MewUI checkout remote.");
    var changes = await ReadAsync("git", ["-C", checkout, "status", "--porcelain"]);
    if (!string.IsNullOrWhiteSpace(changes))
        throw new InvalidOperationException($"MewUI source checkout has local changes. Preserve or move them before rebuilding: {checkout}");
    await RunAsync("git", ["-C", checkout, "fetch", "origin", commit]);
    await RunAsync("git", ["-C", checkout, "checkout", "--detach", commit]);

    string[] common = [
        "-c", "Release", "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
        $"-p:PackageVersion={version}", "-p:ContinuousIntegrationBuild=true",
        $"-p:CustomBeforeMicrosoftCommonTargets={Path.Combine(directory, "MewUiPack.targets")}",
        $"-p:CustomBeforeMicrosoftCommonCrossTargetingTargets={Path.Combine(directory, "MewUiPack.targets")}",
        "--disable-build-servers", "-m:1", "--verbosity", "quiet"
    ];
    foreach (var name in new[] { "MewUI.Analyzers", "MewUI.Generators", "MewUI.Preview" })
        await RunAsync("dotnet", ["build", Path.Combine(checkout, "src", name, name + ".csproj"), .. common]);
    foreach (var name in projects)
        await RunAsync("dotnet", ["pack", Path.Combine(checkout, "src", name, name + ".csproj"), "-o", feed, .. common]);
    await RunAsync("dotnet", ["pack", Path.Combine(checkout, "meta", "MewUI.All", "MewUI.All.csproj"), "-o", feed, .. common]);
    if (!packages.All(File.Exists)) throw new InvalidOperationException("The MewUI build did not produce every expected package.");
    await InvalidateExtractedPackagesAsync(packages, version);
    await File.WriteAllTextAsync(stamp, await CacheStampAsync(fingerprint, packages));
    Console.WriteLine($"MewUI packages are ready in {feed}");
}

// Cache the entire packing policy, not just the upstream revision. Keeping the
// stamp separate from revision.json also detects a damaged local package feed.
static async Task<string> PolicyFingerprintAsync(string directory)
{
    var files = Directory.EnumerateFiles(directory, "*.cs")
        .Concat(new[] { "revision.json", "MewUiSource.csproj", "MewUiPack.targets", "MewUiSource.targets", "build.ps1" }
            .Select(name => Path.Combine(directory, name)))
        .OrderBy(Path.GetFileName, StringComparer.Ordinal);
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var file in files)
    {
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(file) + "\0"));
        hash.AppendData(await File.ReadAllBytesAsync(file));
    }
    return Convert.ToHexStringLower(hash.GetHashAndReset());
}

static async Task<string> CacheStampAsync(string fingerprint, string[] packages)
{
    var lines = new List<string> { fingerprint };
    foreach (var package in packages)
    {
        await using var input = File.OpenRead(package);
        lines.Add(Path.GetFileName(package) + " " + Convert.ToBase64String(await SHA512.HashDataAsync(input)));
    }
    return string.Join('\n', lines);
}

static async Task InvalidateExtractedPackagesAsync(string[] packages, string version)
{
    // NuGet otherwise keeps old extracted assemblies when a private package is
    // rebuilt at the same version. Touch only this feed's exact ID/version, and
    // only when its NuGet content hash differs. The shared bootstrap lock is held.
    var listing = await ReadAsync("dotnet", ["nuget", "locals", "global-packages", "--list", "--force-english-output"]);
    var line = listing.Split('\n').Single(value => value.StartsWith("global-packages:", StringComparison.Ordinal));
    var global = line["global-packages:".Length..].Trim();
    foreach (var package in packages)
    {
        var name = Path.GetFileName(package);
        var id = name[..^(version.Length + ".nupkg".Length + 1)].ToLowerInvariant();
        var extracted = Path.Combine(global, id, version.ToLowerInvariant());
        if (!Directory.Exists(extracted)) continue;
        await using var input = File.OpenRead(package);
        var expected = Convert.ToBase64String(await SHA512.HashDataAsync(input));
        var cachedHash = Path.Combine(extracted, id + "." + version.ToLowerInvariant() + ".nupkg.sha512");
        if (File.Exists(cachedHash) && (await File.ReadAllTextAsync(cachedHash)).Trim() == expected) continue;
        Console.WriteLine("Removing stale private package extraction: " + id + " " + version);
        Directory.Delete(extracted, recursive: true);
    }
}

static async Task<FileStream> AcquireBuildLockAsync(string path)
{
    var wait = Stopwatch.StartNew();
    while (true)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) when (wait.Elapsed < TimeSpan.FromMinutes(10))
        {
            await Task.Delay(250);
        }
    }
}

static ProcessStartInfo Command(string program, string[] arguments)
{
    var command = new ProcessStartInfo(program) { UseShellExecute = false };
    foreach (var argument in arguments) command.ArgumentList.Add(argument);
    return command;
}

static async Task RunAsync(string program, string[] arguments)
{
    using var process = Process.Start(Command(program, arguments))!;
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{program} failed with exit code {process.ExitCode}.");
}

static async Task<string> ReadAsync(string program, string[] arguments)
{
    var command = Command(program, arguments);
    command.RedirectStandardOutput = true;
    using var process = Process.Start(command)!;
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{program} failed with exit code {process.ExitCode}.");
    return output;
}

static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
