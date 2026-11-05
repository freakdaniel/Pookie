using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

try
{
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
    string[] projects = [
        "MewUI", "MewUI.Platform.Win32", "MewUI.Platform.X11", "MewUI.Platform.MacOS",
        "MewUI.Backend.Direct2D", "MewUI.Backend.Gdi", "MewUI.Backend.MewVG.Win32",
        "MewUI.Backend.MewVG.X11", "MewUI.Backend.MewVG.MacOS"
    ];
    var packages = projects.Skip(1).Prepend("MewUI.Core").Append("MewUI")
        .Select(name => Path.Combine(feed, $"Aprillz.{name}.{version}.nupkg")).ToArray();
    Directory.CreateDirectory(cache);
    using var buildLock = await AcquireBuildLockAsync(Path.Combine(cache, commit + ".lock"));
    if (packages.All(File.Exists) && File.Exists(stamp) && await File.ReadAllTextAsync(stamp) == revisionText)
    {
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
        "--disable-build-servers", "-m:1", "--verbosity", "quiet"
    ];
    foreach (var name in new[] { "MewUI.Analyzers", "MewUI.Generators", "MewUI.Preview" })
        await RunAsync("dotnet", ["build", Path.Combine(checkout, "src", name, name + ".csproj"), .. common]);
    foreach (var name in projects)
        await RunAsync("dotnet", ["pack", Path.Combine(checkout, "src", name, name + ".csproj"), "-o", feed, .. common]);
    await RunAsync("dotnet", ["pack", Path.Combine(checkout, "meta", "MewUI.All", "MewUI.All.csproj"), "-o", feed, .. common]);
    if (!packages.All(File.Exists)) throw new InvalidOperationException("The MewUI build did not produce every expected package.");
    await File.WriteAllTextAsync(stamp, revisionText);
    Console.WriteLine($"MewUI packages are ready in {feed}");
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
