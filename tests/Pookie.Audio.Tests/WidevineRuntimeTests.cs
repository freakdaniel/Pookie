using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pookie.Audio;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class WidevineRuntimeTests : IDisposable
{
    private const string Library = "_platform_specific/linux_x64/libwidevinecdm.so";
    private const string LicenseText = "Fixture vendor terms v1. See www.widevine.com";
    private const string Version = "4.10.3112.0";
    private readonly string root = Path.Combine(Path.GetTempPath(), "pookie-owned-widevine-" + Guid.NewGuid());

    [Fact]
    public async Task InstallsItsOwnCheckedModuleAndReusesItWithoutNetwork()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        var package = Package(bytes);
        var library = await Install(http, package);
        Assert.Equal(Path.Combine(root, "linux-x64", Version, Library), library);
        Assert.Equal("fixture module", await File.ReadAllTextAsync(library));
        Assert.True(File.Exists(Path.Combine(root, package.Rid, package.Version, "LICENSE")));
        handler.Offline = true;
        Assert.Equal(library, await Install(http, package));
        Assert.Equal(1, handler.Requests);
        NoTemporaryFiles();
    }

    [Fact]
    public async Task AlteredArchiveIsRejectedBeforeExtraction()
    {
        var bytes = Archive();
        var package = Package(bytes);
        bytes[^1] ^= 1;
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => Install(http, package));
        Assert.Contains("SHA-256 mismatch", failure.Message);
        Assert.False(Directory.Exists(Path.Combine(root, package.Rid, package.Version)));
        NoTemporaryFiles();
    }

    [Fact]
    public async Task InterruptedDownloadCanBeRetriedWithoutLeavingAnInstallation()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        handler.Block = true;
        using var cancellation = new CancellationTokenSource();
        var pending = Install(http, Package(bytes), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        NoTemporaryFiles();
        handler.Block = false;
        Assert.True(File.Exists(await Install(http, Package(bytes))));
    }

    [Fact]
    public async Task ShortResponseIsRejected()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes[..^1]); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Install(http, Package(bytes)));
        NoTemporaryFiles();
    }

    [Fact]
    public async Task UntrustedRedirectIsNotFollowed()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes) { Redirect = new("https://untrusted.invalid/module") };
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => Install(http, Package(bytes)));
        Assert.Contains("download host", failure.Message);
        Assert.Equal(1, handler.Requests);
        NoTemporaryFiles();
    }

    [Fact]
    public async Task DoesNotForwardPlaybackHeadersOrCredentialsToTheComponentServer()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        await Install(http, Package(bytes));
        Assert.Empty(handler.HeaderNames);
        Assert.DoesNotContain("license_token", handler.RequestUri!.Query);
    }

    [Theory]
    [InlineData("../escaped.so")]
    [InlineData("/tmp/escaped.so")]
    [InlineData("..\\escaped.so")]
    public async Task ArchiveTraversalIsRejected(string path)
    {
        var bytes = Archive(extra: path);
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Install(http, Package(bytes)));
        Assert.False(File.Exists(Path.Combine(root, "escaped.so")));
        NoTemporaryFiles();
    }

    [Theory]
    [InlineData("different-version", "10", true)]
    [InlineData(Version, "9", true)]
    [InlineData(Version, "10", false)]
    public async Task MissingLicenseAndIncompatibleManifestsAreRejected(string version, string interfaces, bool license)
    {
        var bytes = Archive(version: version, interfaces: interfaces, license: license);
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Install(http, Package(bytes)));
        Assert.False(Directory.Exists(Path.Combine(root, "linux-x64", Version)));
        NoTemporaryFiles();
    }

    [Fact]
    public async Task InvalidCrxHeaderIsRejected()
    {
        var bytes = Archive();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), uint.MaxValue);
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Install(http, Package(bytes)));
        NoTemporaryFiles();
    }

    [Fact]
    public async Task CorruptedInstalledModuleIsRepairedAndFailedRepairPreservesIt()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        var package = Package(bytes);
        var library = await Install(http, package);
        await File.WriteAllTextAsync(library, "corrupted");
        handler.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Install(http, package));
        Assert.Equal("corrupted", await File.ReadAllTextAsync(library));
        handler.Offline = false;
        Assert.Equal(library, await Install(http, package));
        Assert.Equal("fixture module", await File.ReadAllTextAsync(library));
        NoTemporaryFiles();
    }

    [Fact]
    public async Task ConcurrentPlaybackRequestsInstallTheModuleOnlyOnce()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        var paths = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Install(http, Package(bytes))));
        Assert.Single(paths.Distinct());
        Assert.Equal(1, handler.Requests);
        NoTemporaryFiles();
    }

    [Theory]
    [InlineData("linux", Architecture.X64, "linux-x64")]
    [InlineData("windows", Architecture.X64, "win-x64")]
    [InlineData("windows", Architecture.Arm64, "win-arm64")]
    [InlineData("macos", Architecture.X64, "osx-x64")]
    [InlineData("macos", Architecture.Arm64, "osx-arm64")]
    public void PlatformSelectionUsesTheProcessArchitecture(string os, Architecture architecture, string rid)
    {
        var package = WidevinePackage.ForPlatform(os, architecture);
        Assert.Equal(rid, package.Rid);
        Assert.StartsWith("_platform_specific/", package.Library);
    }

    [Fact]
    public void UnsupportedPlatformIsReportedWithoutTryingToInstallABrowser()
    {
        Assert.Throws<DrmPlaybackException>(() => WidevinePackage.ForPlatform("linux", Architecture.Arm64));
    }

    [Fact]
    public async Task ConsentIsRequiredBeforeNetworkAndEvenForAnInstalledModule()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        var package = Package(bytes);
        await Assert.ThrowsAsync<DrmPlaybackException>(() => WidevineRuntime.InstallAsync(http, root, package, LicenseText, default));
        Assert.Equal(0, handler.Requests);
        Assert.False(Directory.Exists(root));
        var library = await Install(http, package);
        File.Delete(new WidevineConsent(root).FilePath);
        await Assert.ThrowsAsync<DrmPlaybackException>(() => WidevineRuntime.InstallAsync(http, root, package, LicenseText, default));
        Assert.Equal(1, handler.Requests);
        Assert.True(File.Exists(library));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"schema\":1,\"licenseSha256\":\"old-terms\",\"acceptedAtUtc\":\"2026-11-12T00:00:00Z\"}")]
    public async Task MissingCorruptOrOutdatedConsentRequiresAcceptance(string content)
    {
        var consent = new WidevineConsent(root);
        Assert.False(consent.IsAccepted(LicenseText));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(consent.FilePath, content);
        Assert.False(consent.IsAccepted(LicenseText));
        await consent.AcceptAsync(LicenseText, default);
        Assert.True(new WidevineConsent(root).IsAccepted(LicenseText));
        Assert.DoesNotContain(Directory.EnumerateFiles(root), p => Path.GetFileName(p).StartsWith(".consent-"));
    }

    [Fact]
    public async Task CancelledAcceptanceDoesNotUnlockTheInstaller()
    {
        var consent = new WidevineConsent(root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consent.AcceptAsync(LicenseText, cancellation.Token));
        Assert.False(consent.IsAccepted(LicenseText));
        Assert.Empty(Directory.EnumerateFiles(root));
    }

    [Fact]
    public async Task SetupNeverCallsPreparationBeforeAcceptance()
    {
        var consent = new WidevineConsent(root);
        var calls = 0;
        var setup = new WidevineSetup(consent, _ => Task.FromResult(new WidevineCandidate(Package(Archive()), LicenseText, null)), (_, _) => { calls++; return Task.FromResult("fixture"); });
        await Assert.ThrowsAsync<DrmPlaybackException>(() => setup.PrepareAsync(default));
        Assert.Equal(0, calls);
        await setup.ResolveAsync(default);
        await Assert.ThrowsAsync<DrmPlaybackException>(() => setup.PrepareAsync(default));
        Assert.Equal(0, calls);
        await consent.AcceptAsync(LicenseText, default);
        Assert.Equal("fixture", await setup.PrepareAsync(default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ComponentPreparationDoesNotInheritTheUiSynchronizationContext()
    {
        var consent = new WidevineConsent(root);
        await consent.AcceptAsync(LicenseText, default);
        var setup = new WidevineSetup(consent, _ => Task.FromResult(new WidevineCandidate(Package(Archive()), LicenseText, null)), (_, _) =>
        {
            Assert.Null(SynchronizationContext.Current);
            return Task.FromResult("fixture");
        });
        await setup.ResolveAsync(default);
        var previous = SynchronizationContext.Current;
        Task<string> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            pending = setup.PrepareAsync(default);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.Equal("fixture", await pending);
    }

    [Fact]
    public async Task DifferentArchiveLicenseCannotBeInstalledUnderExistingConsent()
    {
        var bytes = Archive(licenseText: "New terms that have not been accepted");
        using var handler = new Handler(bytes); using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => Install(http, Package(bytes)));
        Assert.Contains("license differs", failure.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "linux-x64", Version)));
        NoTemporaryFiles();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObtainsTermsBeforeConsentAndInstallsTheSameArchiveWithoutDownloadingTwice(bool utf8Bom)
    {
        const string freshTerms = "Fresh terms from the component, not a bundled text";
        var bytes = Archive(licenseText: (utf8Bom ? "\uFEFF" : "") + freshTerms);
        var package = Package(bytes);
        using var handler = new Handler(bytes) { Metadata = UpdateResponse(package) };
        using var http = new HttpClient(handler);
        var candidate = await WidevineRuntime.ResolveAsync(http, root, WidevinePackage.ForPlatform("linux", Architecture.X64), default);
        Assert.Equal(freshTerms, candidate.LicenseText);
        Assert.Equal(package, candidate.Package);
        Assert.Equal(bytes, candidate.Archive);
        Assert.Equal(2, handler.Requests);
        Assert.False(Directory.Exists(root));
        await Assert.ThrowsAsync<DrmPlaybackException>(() => WidevineRuntime.InstallCandidateAsync(http, root, candidate, default));
        Assert.False(Directory.Exists(root));
        await new WidevineConsent(root).AcceptAsync(freshTerms, default);
        handler.Offline = true;
        var library = await WidevineRuntime.InstallCandidateAsync(http, root, candidate, default);
        Assert.True(File.Exists(library));
        Assert.Equal(freshTerms, await File.ReadAllTextAsync(Path.Combine(root, package.Rid, package.Version, "LICENSE")));
        Assert.Equal(2, handler.Requests);
        var cached = await WidevineRuntime.ResolveAsync(http, root, WidevinePackage.ForPlatform("linux", Architecture.X64), default);
        Assert.Null(cached.Archive);
        Assert.Equal(freshTerms, cached.LicenseText);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task ExistingComponentWithoutAcceptanceCannotSupplyAStaleConsentCardOffline()
    {
        var bytes = Archive();
        using var handler = new Handler(bytes) { Metadata = UpdateResponse(Package(bytes)) };
        using var http = new HttpClient(handler);
        var candidate = await WidevineRuntime.ResolveAsync(http, root, WidevinePackage.ForPlatform("linux", Architecture.X64), default);
        var consent = new WidevineConsent(root);
        await consent.AcceptAsync(LicenseText, default);
        await WidevineRuntime.InstallCandidateAsync(http, root, candidate, default);
        File.Delete(consent.FilePath);
        handler.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => WidevineRuntime.ResolveAsync(http, root,
            WidevinePackage.ForPlatform("linux", Architecture.X64), default));
        Assert.False(consent.IsAccepted(LicenseText));
        handler.Offline = false;
        var freshBytes = Archive(licenseText: "Changed vendor terms");
        handler.Bytes = freshBytes;
        handler.Metadata = UpdateResponse(Package(freshBytes));
        var fresh = await WidevineRuntime.ResolveAsync(http, root, WidevinePackage.ForPlatform("linux", Architecture.X64), default);
        Assert.Equal("Changed vendor terms", fresh.LicenseText);
        Assert.False(consent.IsAccepted(fresh.LicenseText));
        await consent.AcceptAsync(fresh.LicenseText, default);
        await WidevineRuntime.InstallCandidateAsync(http, root, fresh, default);
        Assert.False(consent.IsAccepted(LicenseText));
    }

    [Fact]
    public async Task ChangedTermsRequireANewExplicitAcceptance()
    {
        var consent = new WidevineConsent(root);
        await consent.AcceptAsync(LicenseText, default);
        var calls = 0;
        var setup = new WidevineSetup(consent, _ => Task.FromResult(new WidevineCandidate(Package(Archive()), "Changed terms", null)),
            (_, _) => { calls++; return Task.FromResult("fixture"); });
        await setup.ResolveAsync(default);
        Assert.False(setup.IsAccepted);
        await Assert.ThrowsAsync<DrmPlaybackException>(() => setup.PrepareAsync(default));
        Assert.Equal(0, calls);
        await setup.AcceptAsync(default);
        Assert.True(setup.IsAccepted);
        Assert.Equal("fixture", await setup.PrepareAsync(default));
        Assert.Equal(1, calls);
        Assert.False(consent.IsAccepted(LicenseText));
    }

    [Fact]
    public async Task GoogleUpdaterRequestUsesThePlatformAndNoUserCredentials()
    {
        var package = Package(Archive());
        using var handler = new Handler([]) { Metadata = UpdateResponse(package) };
        using var http = new HttpClient(handler);
        Assert.Equal(package, await WidevinePackage.LatestAsync(http, WidevinePackage.ForPlatform("linux", Architecture.X64), default));
        Assert.Equal("https://update.googleapis.com/service/update2/json", handler.RequestUri!.AbsoluteUri);
        Assert.Empty(handler.HeaderNames);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        var request = json.RootElement.GetProperty("request");
        Assert.Equal("linux", request.GetProperty("os").GetProperty("platform").GetString());
        Assert.Equal("x64", request.GetProperty("os").GetProperty("arch").GetString());
        Assert.Equal("oimompecagnajdejgnnjijobebaeigek", request.GetProperty("apps")[0].GetProperty("appid").GetString());
        Assert.Equal("0.0.0.0", request.GetProperty("apps")[0].GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("http://dl.google.com/fixture.crx3")]
    [InlineData("https://untrusted.invalid/fixture.crx3")]
    [InlineData("https://dl.google.com.untrusted.invalid/fixture.crx3")]
    [InlineData("https://user:password@dl.google.com/fixture.crx3")]
    [InlineData("https://dl.google.com:444/fixture.crx3")]
    public void UpdaterCannotSelectAnInsecureOrUntrustedArchive(string url)
    {
        var response = UpdateResponse(Package(Archive()), url);
        Assert.Throws<InvalidDataException>(() => WidevinePackage.ParseResponse(response, WidevinePackage.ForPlatform("linux", Architecture.X64)));
    }

    [Fact]
    public async Task CorruptArchiveCannotSupplyTermsForTheCard()
    {
        var bytes = Archive();
        var package = Package(bytes);
        bytes[^1] ^= 1;
        using var handler = new Handler(bytes) { Metadata = UpdateResponse(package) };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => WidevineRuntime.ResolveAsync(http, root,
            WidevinePackage.ForPlatform("linux", Architecture.X64), default));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "  ")]
    [InlineData(true, "oversized")]
    public async Task MissingEmptyOrOversizedTermsCannotBePresented(bool license, string text)
    {
        var bytes = Archive(license: license, licenseText: text == "oversized" ? new string('x', 65537) : text);
        using var handler = new Handler(bytes) { Metadata = UpdateResponse(Package(bytes)) };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => WidevineRuntime.ResolveAsync(http, root,
            WidevinePackage.ForPlatform("linux", Architecture.X64), default));
        Assert.False(Directory.Exists(root));
    }

    private static string UpdateResponse(WidevinePackage package, string? url = null) => ")]}'\n" + $$$"""
        {"response":{"apps":[{"appid":"oimompecagnajdejgnnjijobebaeigek","status":"ok",
        "updatecheck":{"status":"ok","nextversion":"{{{package.Version}}}","pipelines":[{"operations":[
        {"type":"download","size":{{{package.Size}}},"out":{"sha256":"{{{package.Sha256}}}"},
        "urls":[{"url":"http://dl.google.com/insecure.crx3"},{"url":"{{{url ?? package.Url.AbsoluteUri}}}"}]},
        {"type":"crx3","in":{"sha256":"{{{package.Sha256}}}"}}]}]}}]}}
        """;

    private async Task<string> Install(HttpClient http, WidevinePackage package, CancellationToken token = default)
    {
        await new WidevineConsent(root).AcceptAsync(LicenseText, CancellationToken.None);
        return await WidevineRuntime.InstallAsync(http, root, package, LicenseText, token);
    }
    private static WidevinePackage Package(byte[] bytes) => new(Version, "linux-x64", new("https://www.google.com/fixture.crx3"),
        bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), Library);

    private static byte[] Archive(string version = Version, string interfaces = "10", bool license = true, string? extra = null, string? licenseText = null)
    {
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(Library, "fixture module");
            Add("manifest.json", $"{{\"version\":\"{version}\",\"x-cdm-interface-versions\":\"{interfaces}\"}}");
            if (license) Add("LICENSE", licenseText ?? LicenseText);
            if (extra != null) Add(extra, "escape");
            void Add(string name, string text)
            {
                using var output = zip.CreateEntry(name).Open();
                output.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        var bytes = new byte[16 + zipBytes.Length];
        "Cr24"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 4);
        zipBytes.ToArray().CopyTo(bytes, 16);
        return bytes;
    }

    private void NoTemporaryFiles() => Assert.Empty(Directory.GetDirectories(root, ".install-*"));

    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        public int Requests;
        public bool Offline, Block;
        public byte[] Bytes = bytes;
        public string? Metadata, RequestBody;
        public Uri? Redirect, RequestUri;
        public string[] HeaderNames = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            RequestUri = request.RequestUri; HeaderNames = request.Headers.Select(header => header.Key).ToArray();
            Started.TrySetResult();
            if (Block) await Task.Delay(Timeout.Infinite, token);
            if (Offline) throw new HttpRequestException("fixture offline");
            if (request.RequestUri!.Host == "update.googleapis.com")
            {
                RequestBody = await request.Content!.ReadAsStringAsync(token);
                return new(HttpStatusCode.OK) { Content = new StringContent(Metadata ?? "{}") };
            }
            if (Redirect != null) return new(HttpStatusCode.Redirect) { Headers = { Location = Redirect } };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) };
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
