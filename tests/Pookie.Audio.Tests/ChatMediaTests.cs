using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Pookie.App.Messages;
using Pookie.App.Storage;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class ChatMediaTests
{
    private static ChatMediaPackage Package() => new(42, 90, "Private caption", [new("photo.png", "image/png", "Private bytes"u8.ToArray())]);
    [Theory]
    [InlineData('c', "Ab3xYz")]
    [InlineData('n', "12345678")]
    [InlineData('z', "a-b_C")]
    [InlineData('p', "Ab3xYz09")]
    [InlineData('q', "Ab3xY")]
    public void ReferencesStayWithin36AndNeverSendKeysToHost(char provider, string id)
    {
        var reference = new ChatMediaReference(provider, id, RandomNumberGenerator.GetBytes(16));
        Assert.InRange(reference.Code.Length, 1, 36);
        var parsed = ChatMediaReference.Parse(reference.Code)!;
        Assert.Equal(reference.Key, parsed.Key); Assert.Equal(reference.Uri, parsed.Uri);
        Assert.Equal("", parsed.Uri.Query); Assert.Equal("", parsed.Uri.Fragment);
        Assert.DoesNotContain(reference.Code.Split(':')[3], parsed.Uri.AbsoluteUri);
        Assert.DoesNotContain(reference.Code, parsed.ToString());
    }
    [Theory]
    [InlineData("p1:c:../evil:AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("p1:c:123456789:AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("p1:e:abcdef:AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("p1:c:abcdef:AAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("p1:p:123456789:AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("p1:q:../evil:AAAAAAAAAAAAAAAAAAAAAA")]
    public void InvalidAndNonCanonicalReferencesAreRejected(string text) => Assert.Null(ChatMediaReference.Parse(text));
    [Fact]
    public void CiphertextHidesContentAndAuthenticatesParticipantsKeyAndEveryByte()
    {
        var package = Package(); var key = RandomNumberGenerator.GetBytes(16);
        var encrypted = ChatMediaCrypto.Encrypt(package, key);
        Assert.DoesNotContain("Private", Encoding.UTF8.GetString(encrypted));
        var restored = ChatMediaCrypto.Decrypt(encrypted, key, 42, 90);
        Assert.Equal(package.Caption, restored.Caption); Assert.Equal(package.Attachments[0].Data, restored.Attachments[0].Data);
        Assert.ThrowsAny<CryptographicException>(() => ChatMediaCrypto.Decrypt(encrypted, key, 90, 42));
        Assert.ThrowsAny<CryptographicException>(() => ChatMediaCrypto.Decrypt(encrypted, key, 42, 91));
        Assert.ThrowsAny<CryptographicException>(() => ChatMediaCrypto.Decrypt(encrypted, RandomNumberGenerator.GetBytes(16), 42, 90));
        foreach (var index in new[] { 4, 16, 32, encrypted.Length - 1 })
        { var tampered = encrypted.ToArray(); tampered[index] ^= 1; Assert.ThrowsAny<CryptographicException>(() => ChatMediaCrypto.Decrypt(tampered, key, 42, 90)); }
        Assert.NotEqual(encrypted, ChatMediaCrypto.Encrypt(package, key));
    }
    [Fact]
    public void UntrustedFilenamesAndOversizedFilesAreRejected()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        foreach (var name in new[] { "../photo.png", "..\\photo.png", "photo\n.png", "" })
            Assert.Throws<InvalidDataException>(() => ChatMediaCrypto.Encrypt(Package() with { Attachments = [new(name, "image/png", [1])] }, key));
        Assert.Throws<InvalidDataException>(() => ChatMediaCrypto.Encrypt(Package() with { Attachments = [new("large", "x", new byte[ChatMediaCrypto.MaxFileBytes + 1])] }, key));
    }
    [Fact]
    public void PreviewRejectsActiveContentRegardlessOfClaimedMimeType()
    {
        Assert.False(ChatMediaCrypto.IsRasterImage("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray()));
        Assert.False(ChatMediaCrypto.IsRasterImage("<html><script>code()</script>"u8.ToArray()));
        Assert.False(ChatMediaCrypto.IsRasterImage("MZ executable"u8.ToArray()));
        Assert.True(ChatMediaCrypto.IsRasterImage("GIF89a"u8.ToArray()));
    }
    [Fact]
    public async Task UploadFallbackReusesCiphertextAndDownloadCannotRedirectOutsideHost()
    {
        var handler = new HostingHandler(); using var hosting = new ChatMediaHosting(handler);
        var reference = await hosting.UploadAsync(Package(), CancellationToken.None);
        Assert.Equal('q', reference.Provider); Assert.Equal(2, handler.Uploads.Count);
        Assert.Equal(handler.Uploads[0], ChatMediaArchive.Unwrap(handler.Uploads[1]));
        var restored = await hosting.DownloadAsync(reference, 42, 90, CancellationToken.None);
        Assert.Equal("Private caption", restored.Caption);
        Assert.Null(ChatMediaHosting.ParseUploadUrl("https://evil.test/f/abc.bin", 'p', reference.Key));
        Assert.Null(ChatMediaHosting.ParseUploadUrl("https://pomf2.lain.la/f/abcdef.bin?key=secret", 'p', reference.Key));
        Assert.Null(ChatMediaHosting.ParseUploadUrl("https://pomf2.lain.la/f/123456789.bin", 'p', reference.Key));
    }
    [Fact]
    public async Task PrimaryHostingRoundTripDoesNotUploadToFallback()
    {
        var handler = new HostingHandler { PrimaryStatus = HttpStatusCode.OK, PrimaryResponse = """{"success":true,"files":[{"url":"https://pomf2.lain.la/f/Ab3xYz09.bin"}]}""" };
        using var hosting = new ChatMediaHosting(handler);
        var reference = await hosting.UploadAsync(Package(), CancellationToken.None);
        Assert.Equal('p', reference.Provider); Assert.Equal(36, reference.Code.Length); Assert.Single(handler.Uploads);
        var restored = await hosting.DownloadAsync(reference, 42, 90, CancellationToken.None);
        Assert.Equal(Package().Attachments[0].Data, restored.Attachments[0].Data);
        Assert.Equal(Package().Caption, restored.Caption);
    }
    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(412)]
    [InlineData(415)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(302)]
    [InlineData(200)]
    public async Task RejectedOrInvalidPomfResponseAlwaysTriesFallback(int status)
    {
        var handler = new HostingHandler { PrimaryStatus = (HttpStatusCode)status };
        using var hosting = new ChatMediaHosting(handler);
        var reference = await hosting.UploadAsync(Package(), CancellationToken.None);
        Assert.Equal('q', reference.Provider);
        Assert.Equal(2, handler.Uploads.Count);
        Assert.Equal(handler.Uploads[0], ChatMediaArchive.Unwrap(handler.Uploads[1]));
    }
    [Fact]
    public async Task BothHostFailuresExplainStatusesWithoutExposingResponseContent()
    {
        var handler = new HostingHandler { PrimaryStatus = HttpStatusCode.PreconditionFailed, FallbackStatus = HttpStatusCode.ServiceUnavailable };
        using var hosting = new ChatMediaHosting(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => hosting.UploadAsync(Package(), CancellationToken.None));
        Assert.Equal(2, handler.Uploads.Count);
        Assert.Contains("Pomf: HTTP 412", error.Message);
        Assert.Contains("qu.ax: HTTP 503", error.Message);
        Assert.DoesNotContain("sensitive-host-response", error.Message);
    }
    [Fact]
    public async Task CancellationDoesNotStartFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new HostingHandler { Cancel = cancellation };
        using var hosting = new ChatMediaHosting(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hosting.UploadAsync(Package(), cancellation.Token));
        Assert.Single(handler.Uploads);
    }
    [Theory]
    [InlineData("https://qu.ax/abcdef?key=secret", 'q')]
    [InlineData("https://qu.ax/abcdef#secret", 'q')]
    [InlineData("https://user@qu.ax/abcdef", 'q')]
    [InlineData("http://qu.ax/abcdef", 'q')]
    [InlineData("https://qu.ax:444/abcdef", 'q')]
    [InlineData("https://qu.ax/x/abcdef.zip", 'q')]
    [InlineData("https://qu.ax/abcdef.zip", 'q')]
    [InlineData("https://qu.ax/abcdef/other", 'q')]
    [InlineData("https://pomf2.lain.la/f/abcdef.zip", 'p')]
    [InlineData("https://pomf2.lain.la/abcdef.bin", 'p')]
    [InlineData("https://pomf2.lain.la/f/abc%2fdef.bin", 'p')]
    public void ProviderResponsesCannotInjectPathsHostsOrKeys(string url, char provider) =>
        Assert.Null(ChatMediaHosting.ParseUploadUrl(url, provider, new byte[16]));
    [Theory]
    [InlineData("<html>upload successful</html>")]
    [InlineData("{\"success\":false,\"files\":[{\"url\":\"https://pomf2.lain.la/f/abcdef.bin\"}]}")]
    [InlineData("{\"success\":true,\"files\":[{\"url\":\"https://evil.test/abcdef.bin\"}]}")]
    [InlineData("{\"success\":true,\"files\":[]}")]
    [InlineData("{\"success\":true,\"files\":[null]}")]
    public async Task InvalidSuccessfulResponsesCannotBecomeMediaCodes(string response)
    {
        var handler = new HostingHandler { PrimaryStatus = HttpStatusCode.OK, PrimaryResponse = response };
        using var hosting = new ChatMediaHosting(handler);
        Assert.Equal('q', (await hosting.UploadAsync(Package(), CancellationToken.None)).Provider);
        Assert.Equal(2, handler.Uploads.Count);
    }
    [Fact]
    public async Task RedirectResponseIsNeverFollowedAndLegacyCodesStillDownload()
    {
        foreach (var provider in new[] { 'p', 'q', 'c', 'n', 'z' })
        {
            var handler = new HostingHandler { DownloadRedirect = true };
            using var hosting = new ChatMediaHosting(handler);
            await Assert.ThrowsAsync<InvalidOperationException>(() => hosting.DownloadAsync(new(provider, "abcdef", new byte[16]), 42, 90, CancellationToken.None));
            Assert.Equal(1, handler.Downloads);
        }
        var key = RandomNumberGenerator.GetBytes(16);
        foreach (var provider in new[] { 'c', 'n', 'z' })
        {
            var handler = new HostingHandler { Ciphertext = ChatMediaCrypto.Encrypt(Package(), key) };
            using var hosting = new ChatMediaHosting(handler);
            var package = await hosting.DownloadAsync(new(provider, "abcdef", key), 42, 90, CancellationToken.None);
            Assert.Equal(Package().Caption, package.Caption);
        }
    }
    [Fact]
    public void ZipOnlyContainsCiphertextAndRejectsExtraEntriesPathsAndInflation()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var encrypted = ChatMediaCrypto.Encrypt(Package(), key);
        var wrapped = ChatMediaArchive.Wrap(encrypted);
        Assert.Equal(encrypted, ChatMediaArchive.Unwrap(wrapped));
        Assert.DoesNotContain("Private", Encoding.UTF8.GetString(wrapped));
        foreach (var name in new[] { "../payload.bin", "other.bin" })
            Assert.Throws<InvalidDataException>(() => ChatMediaArchive.Unwrap(Zip(name, encrypted)));
        Assert.Throws<InvalidDataException>(() => ChatMediaArchive.Unwrap(Zip("payload.bin", encrypted, extra: true)));
        Assert.Throws<InvalidDataException>(() => ChatMediaArchive.Unwrap("<html>not a zip</html>"u8.ToArray()));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var entry = zip.CreateEntry("payload.bin", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[8192];
            for (var i = 0; i <= ChatMediaArchive.MaxEncryptedBytes / zeros.Length; i++) entry.Write(zeros);
        }
        Assert.Throws<InvalidDataException>(() => ChatMediaArchive.Unwrap(output.ToArray()));
    }
    private static byte[] Zip(string name, byte[] bytes, bool extra = false)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry(name).Open()) entry.Write(bytes);
            if (extra) zip.CreateEntry("extra");
        }
        return output.ToArray();
    }
    [Fact]
    public void OutboxAndDraftsSurviveRestartAndAreIsolatedByAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), "pookie-chat-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(path); var store = new ChatLocalStore(paths, 42);
            store.Save(new(new() { [90] = "Draft" }, [new(90, "Message", "Message", DateTimeOffset.UtcNow, true, ["prior-message"])]));
            var loaded = new ChatLocalStore(paths, 42).Load();
            Assert.Equal("Draft", loaded.Drafts[90]); Assert.Single(loaded.Pending);
            Assert.Equal(["prior-message"], Assert.IsType<string[]>(loaded.Pending[0].BeforeIds));
            Assert.Empty(new ChatLocalStore(paths, 43).Load().Pending);
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(path, "Chats", "42", "drafts.json")));
        }
        finally { Directory.Delete(path, true); }
    }
    private sealed class HostingHandler : HttpMessageHandler
    {
        internal List<byte[]> Uploads = []; internal byte[] Ciphertext = [];
        internal HttpStatusCode PrimaryStatus = HttpStatusCode.ServiceUnavailable, FallbackStatus = HttpStatusCode.OK;
        internal string PrimaryResponse = "sensitive-host-response";
        internal int Downloads;
        internal bool DownloadRedirect;
        internal CancellationTokenSource? Cancel;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            if (request.Method == HttpMethod.Get)
            {
                Downloads++;
                Assert.Equal(request.RequestUri!.Host == "qu.ax" ? "https://qu.ax/" + request.RequestUri.AbsolutePath[3..^4] : null, request.Headers.Referrer?.AbsoluteUri);
                if (DownloadRedirect) return new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.test/file") } };
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Ciphertext.Length > 0 ? Ciphertext : Uploads.Last()) };
            }
            Assert.Null(request.Headers.Referrer);
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var file = multipart.Single(p => p.Headers.ContentDisposition?.FileName != null);
            Assert.Equal(Uploads.Count == 0 ? "pomf.lain.la" : "qu.ax", request.RequestUri!.Host);
            Assert.Equal("files[]", file.Headers.ContentDisposition!.Name!.Trim('"'));
            if (Uploads.Count == 1)
            {
                Assert.Equal("application/zip", file.Headers.ContentType!.MediaType);
                Assert.Equal("-1", await multipart.Single(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "expiry").ReadAsStringAsync(cancellationToken));
            }
            else Assert.Equal("application/octet-stream", file.Headers.ContentType!.MediaType);
            Uploads.Add(await file.ReadAsByteArrayAsync(cancellationToken));
            if (Cancel != null) { Cancel.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            return Uploads.Count == 1 ? new(PrimaryStatus) { Content = new StringContent(PrimaryResponse) } :
                new(FallbackStatus) { Content = new StringContent(FallbackStatus == HttpStatusCode.OK
                    ? """{"success":true,"files":[{"url":"https://qu.ax/abcDEF"}]}""" : "sensitive-host-response") };
        }
    }
}
