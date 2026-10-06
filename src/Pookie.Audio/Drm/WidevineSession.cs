using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Pookie.Audio;

internal sealed class DrmPlaybackException(string message) : InvalidOperationException(message);

// A temporary licensed session. License bytes go straight into the CDM, not a key extractor.
internal sealed class WidevineSession : IDisposable
{
    private readonly NativeWidevine handle;
    private readonly HttpClient http;
    private readonly string authorization;
    private uint promise;
    private bool licensed;

    private WidevineSession(NativeWidevine handle, HttpClient http, string authorization)
    { this.handle = handle; this.http = http; this.authorization = authorization; }

    public static async Task<WidevineSession> OpenAsync(HttpClient http, string authorization, byte[] initData, CancellationToken token, byte[]? serverCertificate = null)
    {
        if (authorization is not { Length: > 0 and <= 16384 } || authorization.Any(c => c < 32 || c == 127))
            throw new DrmPlaybackException("SoundCloud не предоставил авторизацию DRM-лицензии.");
        var library = CdmLocator.Find() ?? throw new DrmPlaybackException("Не найден Widevine CDM. Установи модуль защищённого воспроизведения через Chrome, Brave или Firefox.");
        NativeWidevine handle;
        try { handle = new(library); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { throw new DrmPlaybackException("Установленный модуль Widevine отсутствует или несовместим с этой платформой."); }
        var result = new WidevineSession(handle, http, authorization);
        try
        {
            var initialized = await result.NextAsync(token);
            if (initialized.Kind != 1 || initialized.Code != 0) throw new DrmPlaybackException("Widevine CDM не поддерживает наш нативный интерфейс или отклонил инициализацию.");
            if (serverCertificate != null)
            {
                handle.SetServerCertificate(++result.promise, serverCertificate);
                var installed = await result.NextAsync(token);
                if (installed.Kind != 3 || installed.Promise != result.promise || installed.Code != 0)
                    throw new DrmPlaybackException("Widevine CDM отклонил сертификат сервера.");
                Console.WriteLine("DRM_SERVICE_CERTIFICATE_INSTALLED: native CDM accepted certificate; payload redacted");
            }
            handle.Begin(++result.promise, initData);
            await result.LicenseAsync(token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private async Task LicenseAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var exchanges = 0;
        while (true)
        {
            var message = await NextAsync(timeout.Token);
            if (message.Kind == 4) throw new DrmPlaybackException("Widevine CDM отклонил создание или обновление лицензии.");
            if (message.Kind == 5 && message.Code == 0) { licensed = true; return; }
            if (message.Kind != 2) continue;
            if (++exchanges > 8 || message.Code is not (0 or 1)) throw new DrmPlaybackException("Widevine запросил неподдерживаемый тип обмена лицензией.");
            await SendLicenseAsync(message.Payload, timeout.Token);
        }
    }

    private async Task SendLicenseAsync(byte[] challenge, CancellationToken token)
    {
        try
        {
            // Endpoint and public website application id verified in SoundCloud's current player.
            var uri = new Uri("https://license.media-streaming.soundcloud.cloud/playback/widevine?license_token=" + Uri.EscapeDataString(authorization));
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Referrer = new("https://soundcloud.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://soundcloud.com");
            request.Headers.TryAddWithoutValidation("X-SC-Application-Id", "46941");
            request.Content = new ByteArrayContent(challenge);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw new DrmPlaybackException($"SoundCloud не выдал DRM-лицензию (HTTP {(int)response.StatusCode})." +
                    await DrmLicenseFailure.ReadAsync(response, token));
            var license = await HttpRangeStream.ReadBoundedAsync(response.Content, 2 * 1024 * 1024, token);
            try
            {
                handle.Update(++promise, license);
            }
            finally { CryptographicOperations.ZeroMemory(license); }
        }
        finally { CryptographicOperations.ZeroMemory(challenge); }
    }

    public async Task RenewAsync(CancellationToken token)
    {
        while (TryNext(out var message))
        {
            if (message.Kind == 2)
            {
                if (message.Code is not (0 or 1)) throw new DrmPlaybackException("Неподдерживаемое обновление лицензии Widevine.");
                licensed = false;
                await SendLicenseAsync(message.Payload, token);
                await LicenseAsync(token);
            }
            else if (message.Kind == 4 || message.Kind == 6)
                throw new DrmPlaybackException("Лицензия Widevine истекла или ограничила воспроизведение.");
        }
    }

    public byte[] Decrypt(byte[] data, byte[] kid, byte[] iv, CencSubsample[] subsamples)
    {
        if (!licensed) throw new DrmPlaybackException("Нет действующей лицензии Widevine.");
        return handle.Decrypt(data, kid, iv, subsamples);
    }

    private async Task<CdmEvent> NextAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (TryNext(out var result)) return result;
            await Task.Delay(10, timeout.Token);
        }
    }

    private bool TryNext(out CdmEvent result)
    {
        return handle.TryPoll(out result);
    }

    public void Dispose() => handle.Dispose();
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct CencSubsample(uint ClearBytes, uint CipherBytes);
