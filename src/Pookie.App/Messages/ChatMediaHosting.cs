using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Pookie.Logging;

namespace Pookie.App.Messages;

internal sealed class ChatMediaHosting : IDisposable
{
    private readonly HttpClient http;
    internal ChatMediaDiskCache? DiskCache {get;set;}
    internal ChatMediaHosting(HttpMessageHandler? handler = null)
    {
        if (handler == null)
        {
            var native = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            if (Environment.GetEnvironmentVariable("POOKIE_PROXY") is { Length: > 0 } proxy) native.Proxy = new WebProxy(proxy);
            handler = native;
        }
        http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Pookie/1.0");
    }

    internal async Task<ChatMediaReference> UploadAsync(ChatMediaPackage package, CancellationToken token)
    {
        var key = RandomNumberGenerator.GetBytes(16);
        byte[] encrypted;
        try { encrypted = await Task.Run(() => ChatMediaCrypto.Encrypt(package, key), token); }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
        try
        {
            ChatMediaReference reference;
            try { reference = await UploadToAsync('p', encrypted, key, token); }
            catch (Exception error) when (IsHostFailure(error, token))
            {
                LogUploadFailure('p', error);
                try { reference = await UploadToAsync('q', encrypted, key, token); }
                catch (Exception fallback) when (IsHostFailure(fallback, token))
                {
                    LogUploadFailure('q', fallback);
                    throw new InvalidOperationException($"Не удалось загрузить вложение. {DescribeFailure('p', error)}; {DescribeFailure('q', fallback)}. Файл не отправлен в чат.");
                }
            }
            if (DiskCache is { } cache)
                try { await cache.PutAsync(reference.Code, encrypted, token); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return reference;
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); CryptographicOperations.ZeroMemory(key); }
    }

    private async Task<ChatMediaReference> UploadToAsync(char provider, byte[] encrypted, byte[] key, CancellationToken token)
    {
        // ResponseHeadersRead needs a deadline for the response body as well as its headers.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var requestToken = deadline.Token;
        var archive = provider == 'q' ? await Task.Run(() => ChatMediaArchive.Wrap(encrypted), requestToken) : null;
        try
        {
            using var content = new MultipartFormDataContent();
            using var file = new ByteArrayContent(archive ?? encrypted);
            file.Headers.ContentType = new MediaTypeHeaderValue(provider == 'q' ? "application/zip" : "application/octet-stream");
            content.Add(file, "files[]", provider == 'q' ? "pookie.zip" : "pookie.bin");
            if (provider == 'q') content.Add(new StringContent("-1"), "expiry");
            using var request = new HttpRequestMessage(HttpMethod.Post, provider == 'p' ? "https://pomf.lain.la/upload.php" : "https://qu.ax/upload") { Content = content };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            byte[] body;
            try { body = await ReadBoundedAsync(response.Content, 4096, requestToken); }
            catch (InvalidDataException) { throw new UploadFailure((int)response.StatusCode, "слишком большой ответ сервера"); }
            if (!response.IsSuccessStatusCode) throw new UploadFailure((int)response.StatusCode, "сервер отклонил загрузку");
            return ParseUploadResponse(body, provider, key) ?? throw new UploadFailure((int)response.StatusCode,
                "сервер не вернул поддерживаемый адрес вложения");
        }
        finally { if (archive != null) CryptographicOperations.ZeroMemory(archive); }
    }

    private static ChatMediaReference? ParseUploadResponse(byte[] body, char provider, byte[] key)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() != 1 ||
                files[0].ValueKind != JsonValueKind.Object || !files[0].TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String ||
                url.GetString() is not { Length: <= 512 } value) return null;
            return ParseUploadUrl(value, provider, key);
        }
        catch (JsonException) { return null; }
    }

    private sealed class UploadFailure(int status, string reason) : Exception
    {
        internal int Status { get; } = status;
        internal string Reason { get; } = reason;
    }

    private static bool IsHostFailure(Exception error, CancellationToken token) => !token.IsCancellationRequested &&
        error is UploadFailure or HttpRequestException or OperationCanceledException or IOException;

    // Do not expose arbitrary host responses: they may echo uploaded data or credentials.
    private static string HostName(char provider) => provider == 'p' ? "Pomf" : "qu.ax";
    private static string FailureReason(Exception error) => error is UploadFailure failure ? failure.Reason :
        error is OperationCanceledException ? "тайм-аут загрузки" : "ошибка соединения";
    private static int? FailureStatus(Exception error) => error is UploadFailure failure ? failure.Status :
        error is HttpRequestException { StatusCode: { } status } ? (int)status : null;
    private static string DescribeFailure(char provider, Exception error) =>
        $"{HostName(provider)}: {(FailureStatus(error) is { } status ? $"HTTP {status} — " : "")}{FailureReason(error)}";
    private static void LogUploadFailure(char provider, Exception error) => AppLog.For("Pookie.Chats").Warning(
        "Не удалось загрузить вложение: {Provider}; HTTP {HttpStatus}; {Failure}", HostName(provider), FailureStatus(error), FailureReason(error));

    internal static ChatMediaReference? ParseUploadUrl(string value, char provider, byte[] key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" ||
            provider is not ('p' or 'q') || uri.Host != (provider == 'p' ? "pomf2.lain.la" : "qu.ax")) return null;
        var name = uri.AbsolutePath;
        if (provider == 'p')
        {
            if (!name.StartsWith("/f/", StringComparison.Ordinal) || !name.EndsWith(".bin", StringComparison.Ordinal)) return null;
            name = name[3..^4];
        }
        else
        {
            if (!name.StartsWith('/')) return null;
            name = name[1..];
        }
        var reference = new ChatMediaReference(provider, name, key);
        return ChatMediaReference.Parse(reference.Code);
    }

    internal async Task<ChatMediaPackage> DownloadAsync(ChatMediaReference reference, long sender, long recipient, CancellationToken token, Func<ChatMediaPreview, Task>? previewReady = null, int maximumBytes = int.MaxValue)
    {
        if (ChatMediaReference.Parse(reference.Code) == null) throw new InvalidDataException("Некорректный код вложения.");
        var cache=DiskCache;
        if(cache!=null && await cache.ReadAsync(reference.Code,token,maximumBytes) is {} cached)
        {
            try{return await Task.Run(()=>ChatMediaCrypto.Decrypt(cached,reference.Key,sender,recipient),token);}
            catch(Exception error)when(error is CryptographicException or InvalidDataException){ /* Ignore corrupt cached bytes; authenticate the network copy. */ }
            finally{CryptographicOperations.ZeroMemory(cached);}
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var requestToken = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, reference.Uri);
        // qu.ax's documented download button requires its own page as the referrer. No keys or SC cookies.
        if (reference.Provider == 'q') request.Headers.Referrer = new Uri($"https://qu.ax/{reference.ObjectId}");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
            ? "Вложение больше недоступно на хостинге." : "Не удалось загрузить вложение. Попробуй ещё раз.");
        var previewDelivered = false;
        var bytes = await ReadBoundedAsync(response.Content, Math.Min(maximumBytes, reference.Provider == 'q' ? ChatMediaArchive.MaxArchiveBytes : ChatMediaArchive.MaxEncryptedBytes), requestToken, previewReady == null ? null : async prefix =>
        {
            if (previewDelivered) return;
            var offset = PreviewOffset(prefix.Span, reference.Provider);
            if (offset < 0 || prefix.Length < offset) return;
            var preview = await Task.Run(() => ChatMediaCrypto.ReadPreview(prefix.Span[offset..], reference.Key, sender, recipient), requestToken);
            if (preview == null) return;
            previewDelivered = true;
            try { await previewReady(preview); }
            finally { foreach (var image in preview.Images) if (image != null) CryptographicOperations.ZeroMemory(image.Pixels); }
        });
        byte[]? encrypted = null;
        try
        {
            var package = await Task.Run(() =>
            {
                encrypted = reference.Provider == 'q' ? ChatMediaArchive.Unwrap(bytes) : bytes;
                return ChatMediaCrypto.Decrypt(encrypted, reference.Key, sender, recipient);
            }, token);
            if(cache!=null && encrypted!=null)
                try{await cache.PutAsync(reference.Code,encrypted,token);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
            return package;
        }
        catch (CryptographicException) { throw new InvalidOperationException("Вложение повреждено или ключ не подходит к этой переписке."); }
        finally { CryptographicOperations.ZeroMemory(bytes); if (encrypted != null && encrypted != bytes) CryptographicOperations.ZeroMemory(encrypted); }
    }

    // Stored ZIP entries start with a small local header. Read only the known
    // payload entry; reject compressed/encrypted/descriptor layouts for previews.
    private static int PreviewOffset(ReadOnlySpan<byte> prefix, char provider)
    {
        if (provider != 'q') return 0;
        if (prefix.Length < 30) return -1;
        if (!prefix[..4].SequenceEqual(new byte[] { 80, 75, 3, 4 }) ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(prefix[6..]) != 0 ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(prefix[8..]) != 0) return -1;
        var name = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(prefix[26..]);
        var extra = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(prefix[28..]);
        if (name != 11 || extra > 4096 || prefix.Length < 30 + name + extra || !prefix.Slice(30, name).SequenceEqual("payload.bin"u8)) return -1;
        return 30 + name + extra;
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token, Func<ReadOnlyMemory<byte>, Task>? progress = null)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Превышен допустимый размер данных.");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        { if (output.Length + count > limit) throw new InvalidDataException("Превышен допустимый размер данных."); output.Write(buffer, 0, count);
          if (progress != null) await progress(output.GetBuffer().AsMemory(0, (int)Math.Min(output.Length, ChatMediaCrypto.MaxPreviewBytes + 8192))); }
        return output.ToArray();
    }
    public void Dispose() => http.Dispose();
}
