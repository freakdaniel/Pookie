using System.Net;
using System.Security.Cryptography;
using Pookie.App.Messages;

namespace Pookie.App.Diagnostics;

// Explicit opt-in only: uploads ciphertext to a public host, never sends a SoundCloud DM.
internal static class ChatMediaHostingLiveProbe
{
    internal static async Task RunAsync(string provider, string? file)
    {
        if (provider is not ("pomf" or "quax")) throw new ArgumentException("Укажи pomf или quax.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var data = file == null ? RandomNumberGenerator.GetBytes(4096) :
            new FileInfo(file).Length <= ChatMediaCrypto.MaxFileBytes ? await File.ReadAllBytesAsync(file, timeout.Token) :
            throw new InvalidOperationException("Файл слишком большой для проверки.");
        ChatMediaReference? reference = null;
        ChatMediaPackage? restored = null;
        try
        {
            HttpMessageHandler? handler = null;
            if (provider == "quax")
            {
                var native = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
                if (Environment.GetEnvironmentVariable("POOKIE_PROXY") is { Length: > 0 } proxy) native.Proxy = new WebProxy(proxy);
                handler = new FailPrimary { InnerHandler = native };
            }
            using var hosting = new ChatMediaHosting(handler);
            var package = new ChatMediaPackage(42, 90, "Hosting round-trip probe",
                [new(file == null ? "probe.bin" : Path.GetFileName(file), "application/octet-stream", data)]);
            reference = await hosting.UploadAsync(package, timeout.Token);
            if (reference.Provider != (provider == "pomf" ? 'p' : 'q')) throw new InvalidOperationException("Unexpected hosting provider.");
            restored = await hosting.DownloadAsync(reference, 42, 90, timeout.Token);
            if (restored.Caption != package.Caption || restored.Attachments.Length != 1 ||
                !restored.Attachments[0].Data.AsSpan().SequenceEqual(data)) throw new InvalidOperationException("Hosting changed the encrypted attachment.");
            Console.WriteLine($"CHAT_MEDIA_HOSTING_LIVE_OK: provider={provider}; code_length={reference.Code.Length}; bytes={data.Length}; authenticated decryption and exact file match; credentials and object IDs omitted");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            if (reference != null) CryptographicOperations.ZeroMemory(reference.Key);
            if (restored != null) foreach (var attachment in restored.Attachments) CryptographicOperations.ZeroMemory(attachment.Data);
        }
    }

    private sealed class FailPrimary : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri?.Host == "pomf.lain.la" ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) : base.SendAsync(request, cancellationToken);
    }
}
