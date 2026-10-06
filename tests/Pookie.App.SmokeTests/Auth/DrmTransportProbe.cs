using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfiniFrame;
using InfiniFrame.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pookie.Audio;

namespace Pookie.App.Auth;

// Diagnostic only: compare native CDM transport with Pookie's own browser CDM.
// One fixed license endpoint, no arbitrary browser requests, credentials only over anonymous IPC.
internal sealed class DrmTransportProbe(string profilePath) : HttpMessageHandler
{
    private const string Endpoint = "https://license.media-streaming.soundcloud.cloud/playback/widevine";
    private const string BridgeId = "pookie:drm-transport-probe";
    private static readonly HttpRequestOptionsKey<bool> CapabilitiesOnly = new("pookie:capabilities-only");
    private static readonly HttpRequestOptionsKey<bool> BrowserCdm = new("pookie:browser-cdm");
    private static readonly HttpRequestOptionsKey<bool> MediaOnly = new("pookie:media-only");
    private sealed record Command(string Pairing, string Token, byte[] Body, bool CapabilitiesOnly, bool BrowserCdm, bool MediaOnly);
    private sealed record Result(string Pairing, int Status, byte[] Body, string Failure, string Widevine, string Keys = "unknown", int Exchanges = 0);

    public static async Task CheckBrowserCdmAsync(string profilePath, string authorization, byte[] initData, CancellationToken token)
    {
        using var http = new HttpClient(new DrmTransportProbe(profilePath));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + "?license_token=" + Uri.EscapeDataString(authorization));
        request.Options.Set(BrowserCdm, true);
        request.Content = new ByteArrayContent(initData);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Browser CDM license rejected (HTTP {(int)response.StatusCode}).");
    }

    public static async Task CheckCapabilitiesAsync(CancellationToken token, bool mediaOnly = false)
    {
        using var profile = BrowserProfile.Open(fixture: "capabilities");
        using var http = new HttpClient(new DrmTransportProbe(profile.Path));
        // This mode never sends a license HTTP request; the child only queries browser EME.
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + "?license_token=unused");
        request.Options.Set(CapabilitiesOnly, true);
        request.Options.Set(MediaOnly, mediaOnly);
        request.Content = new ByteArrayContent([]);
        using var response = await http.SendAsync(request, token);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri is not { } uri ||
            uri.GetLeftPart(UriPartial.Path) != Endpoint || uri.UserInfo != "" || uri.Fragment != "")
            throw new InvalidOperationException("DRM probe only permits the SoundCloud Widevine license endpoint.");
        var query = uri.Query;
        if (!query.StartsWith("?license_token=", StringComparison.Ordinal) || query.Contains('&'))
            throw new InvalidOperationException("Invalid DRM probe authorization.");
        var authorization = Uri.UnescapeDataString(query[15..]);
        var body = await request.Content!.ReadAsByteArrayAsync(token);
        var capabilitiesOnly = request.Options.TryGetValue(CapabilitiesOnly, out var mode) && mode;
        var browserCdm = request.Options.TryGetValue(BrowserCdm, out var browserMode) && browserMode;
        var mediaOnly = request.Options.TryGetValue(MediaOnly, out var mediaMode) && mediaMode;
        if (authorization.Length is < 1 or > 16384 || body.Length > 65536 || !capabilitiesOnly && body.Length == 0)
            throw new InvalidOperationException("Invalid DRM probe payload size.");
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(DrmTransportProbe).Assembly.Location);
        start.ArgumentList.Add("--drm-transport-child");
        start.ArgumentList.Add(pipe.GetClientHandleAsString());
        start.ArgumentList.Add("--login-profile");
        start.ArgumentList.Add(profilePath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start DRM transport probe.");
        pipe.DisposeLocalCopyOfClientHandle();
        var stdout = DrainAsync(process.StandardOutput);
        var stderr = DrainAsync(process.StandardError);
        var chars = new char[100000];
        try
        {
            var pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new Command(pairing, authorization, body, capabilitiesOnly, browserCdm, mediaOnly)).AsMemory(), token);
            process.StandardInput.Close();
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var count = 0;
            while (count < chars.Length)
            {
                var read = await reader.ReadAsync(chars.AsMemory(count, 1), token);
                if (read == 0 || chars[count] == '\n') break;
                count += read;
            }
            if (count == chars.Length) throw new InvalidOperationException("DRM transport probe response too large.");
            var result = count > 0 ? JsonSerializer.Deserialize<Result>(new string(chars, 0, count)) : null;
            if (result == null || result.Pairing != pairing || result.Status is < 0 or > 599 || result.Body.Length > 65536 ||
                result.Failure is not ("none" or "network" or "oversized" or "evaluation" or "eme" or "mse-unsupported" or "mse-timeout" or "mse-media-1" or "mse-media-2" or "mse-media-3" or "mse-media-4") ||
                result.Keys is not ("unknown" or "usable" or "unusable" or "update-error") ||
                result.Exchanges is < 0 or > 8 ||
                result.Widevine is not ("supported" or "unsupported" or "unknown"))
                throw new InvalidOperationException("DRM transport probe returned no valid response.");
            if (capabilitiesOnly)
            {
                Console.WriteLine($"DRM_WEBVIEW_CAPABILITIES: mseProbe={mediaOnly}; widevine={result.Widevine}; evaluation={result.Failure}; no license request");
                if (result.Failure != "none") throw new InvalidOperationException(mediaOnly ? "Browser MSE initialization failed." : "Cannot query browser EME capabilities.");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            Console.WriteLine($"DRM_WEBVIEW_TRANSPORT: cdm={(browserCdm ? "browser" : "native")}; http={result.Status}; failure={result.Failure}; emeWidevine={result.Widevine}; keys={result.Keys}; exchanges={result.Exchanges}; bytes={result.Body.Length}; credentials redacted");
            if (result.Status == 0) throw new InvalidOperationException($"WebView license request failed ({result.Failure}).");
            if (browserCdm && result.Status is >= 200 and < 300 && result.Keys != "usable")
                throw new InvalidOperationException("Browser CDM did not report usable keys.");
            return new HttpResponseMessage((HttpStatusCode)result.Status) { Content = new ByteArrayContent(result.Body) };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
            Array.Clear(chars);
            if (!process.HasExited)
            {
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { process.Kill(entireProcessTree: true); }
            }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    public static int RunChild(string handle, string profilePath)
    {
        var line = Console.In.ReadLine();
        if (line is not { Length: > 0 and < 120000 }) return 2;
        var command = JsonSerializer.Deserialize<Command>(line);
        if (command is not { Pairing.Length: 64, Token.Length: > 0 and <= 16384, Body.Length: <= 65536 } ||
            !command.CapabilitiesOnly && command.Body.Length == 0) return 2;
        using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        using var lifetime = new CancellationTokenSource();
        var completed = false;
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        var builder = InfiniFrameWindowBuilder.Create(services)
            .SetUserAgent(null).SetTitle("Pookie — проверка DRM-транспорта").SetSize(900, 760)
            // A static secure document keeps SoundCloud's genuine origin without starting its player or ads.
            .SetStartPageUrl("https://soundcloud.com/robots.txt").SetTemporaryFilesPath(profilePath)
            .EnableIgnoreCertificateErrors(false).EnableWebSecurity(true).EnableDevTools(false)
            .EnableFileSystemAccess(false).EnableBrowserPermissions(false).EnableMediaAutoplay(command.MediaOnly)
            .AddTrustedOrigin("https://soundcloud.com");
        builder.RegisterWebMessagePostHandler(BridgeId, (window, raw) =>
        {
            if (completed || raw is not { Length: < 100000 } ||
                !Uri.TryCreate(window.GetCurrentUrl(), UriKind.Absolute, out var source) ||
                source.GetLeftPart(UriPartial.Authority) != "https://soundcloud.com") return;
            // Named Post handlers receive the data object, not the outer v2 envelope.
            using var json = JsonDocument.Parse(raw);
            var payload = json.RootElement.TryGetProperty("Pairing", out _) ? json.RootElement : json.RootElement.GetProperty("data");
            var result = payload.Deserialize<Result>();
            if (result?.Pairing != command.Pairing) return;
            completed = true;
            writer.WriteLine(JsonSerializer.Serialize(result)); writer.Flush();
            window.Close();
        });
        builder.RegisterWindowCreatedHandler(window =>
        {
            if (command.MediaOnly) BrowserWindowVisibility.UseForPlayback(window);
            else BrowserWindowVisibility.Set(window, false);
            _ = ExecuteAsync(window);
        });
        try { builder.Build().WaitForClose(); }
        finally { lifetime.Cancel(); CryptographicOperations.ZeroMemory(command.Body); }
        return completed ? 0 : 1;

        async Task ExecuteAsync(IInfiniFrameWindow window)
        {
            try
            {
                await window.WaitForReadyAsync(lifetime.Token);
                var script = "(() => { const q=" + JsonSerializer.Serialize(command) + ";\n" + Script + "\n})()";
                await window.Features.JavaScript.ExecuteJavaScriptAsync(script, lifetime.Token);
            }
            catch (Exception) when (lifetime.IsCancellationRequested || window.IsClosedOrClosing()) { return; }
            catch (Exception)
            {
                await window.DispatchAsync(() =>
                {
                    if (completed) return;
                    completed = true;
                    writer.WriteLine(JsonSerializer.Serialize(new Result(command.Pairing, 0, [], "evaluation", "unknown"))); writer.Flush();
                    window.Close();
                });
            }
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer) != 0) { /* Discard engine output; it can contain credentials. */ }
    }

    private const string Script = """
        if (location.origin !== 'https://soundcloud.com' || window !== window.top) return;
        (async () => {
          let widevine = 'unknown', access = null;
          try {
            access = await navigator.requestMediaKeySystemAccess('com.widevine.alpha', [{
              initDataTypes: ['cenc'], audioCapabilities: [{contentType:'audio/mp4; codecs="mp4a.40.2"'}],
              distinctiveIdentifier:'not-allowed', persistentState:'not-allowed'
            }]);
            widevine = 'supported';
          } catch { widevine = 'unsupported'; }
          let completed = false, exchanges = 0, lastStatus = 0, session = null;
          const send = (status, body, failure, keys = 'unknown') => {
            if (completed) return; completed = true;
            window.infiniframe.host.postData({
            id:'pookie:drm-transport-probe', command:'Post', version:2,
            data:{Pairing:q.Pairing, Status:status, Body:body, Failure:failure, Widevine:widevine, Keys:keys, Exchanges:exchanges}
            });
          };
          if (q.MediaOnly) {
            if (!window.MediaSource || !MediaSource.isTypeSupported('audio/mp4; codecs="mp4a.40.2"')) { send(0,'','mse-unsupported'); return; }
            const audio = document.createElement('audio'), source = new MediaSource();
            audio.style.display = 'none'; audio.preload = 'auto'; document.body.append(audio);
            source.addEventListener('sourceopen', () => send(0,'','none'), {once:true});
            audio.addEventListener('error', () => send(0,'','mse-media-' + audio.error.code), {once:true});
            audio.src = URL.createObjectURL(source); audio.load(); audio.play().catch(() => {});
            setTimeout(() => send(0,'','mse-timeout'), 10000); return;
          }
          if (q.CapabilitiesOnly) { send(0, '', 'none'); return; }
          // EME key status can arrive after the update promise resolves.
          const reportKeys = () => {
            if (!session || !session.keyStatuses.size || lastStatus < 200 || lastStatus >= 300) return;
            send(lastStatus, '', 'none', Array.from(session.keyStatuses.values()).includes('usable') ? 'usable' : 'unusable');
          };
          setTimeout(() => send(lastStatus, '', 'eme', 'unusable'), 45000);
          // Match SoundCloud's HLS player: a binary POST using XMLHttpRequest.
          const requestLicense = (challenge, session = null) => new Promise(resolve => {
            const xhr = new XMLHttpRequest(); let finished = false;
            const finish = (status, body, failure, keys = 'unknown') => {
              if (finished) return; finished = true; send(status, body, failure, keys); resolve();
            };
            xhr.open('POST', 'https://license.media-streaming.soundcloud.cloud/playback/widevine?license_token=' + encodeURIComponent(q.Token), true);
            xhr.responseType = 'arraybuffer'; xhr.timeout = 20000;
            xhr.setRequestHeader('Content-Type','application/octet-stream');
            xhr.setRequestHeader('X-SC-Application-Id','46941');
            xhr.onerror = xhr.ontimeout = () => finish(0,'','network');
            xhr.onprogress = event => { if (event.loaded > 65536) { finish(0,'','oversized'); xhr.abort(); } };
            xhr.onload = async () => { try {
            lastStatus = xhr.status;
            const bytes = new Uint8Array(xhr.response || new ArrayBuffer(0));
            if (bytes.length > 65536) { finish(0,'','oversized'); return; }
            let keys = 'unknown';
            if (session && xhr.status >= 200 && xhr.status < 300) {
              try {
                await session.update(bytes);
                // The first response can install a service certificate and trigger another message.
                // It is not a usable license yet; wait for the following request instead of reporting success.
                if (!session.keyStatuses.size) { finished = true; resolve(); return; }
                keys = Array.from(session.keyStatuses.values()).includes('usable') ? 'usable' : 'unusable';
              } catch { keys = 'update-error'; }
            }
            let text = ''; for (const byte of bytes) text += String.fromCharCode(byte);
            // A browser-generated license belongs to its browser session and is never exported.
            finish(xhr.status, session ? '' : btoa(text), 'none', keys);
            } catch { finish(0,'','network'); } };
            try { xhr.send(challenge); } catch { finish(0,'','network'); }
          });
          const input = Uint8Array.from(atob(q.Body), c => c.charCodeAt(0));
          if (q.BrowserCdm) {
            try {
              if (!access) throw new Error('unsupported');
              const mediaKeys = await access.createMediaKeys();
              session = mediaKeys.createSession('temporary');
              session.addEventListener('keystatuseschange', reportKeys);
              const audio = document.createElement('audio'); document.body.append(audio);
              await audio.setMediaKeys(mediaKeys);
              let queue = Promise.resolve();
              session.addEventListener('message', event => {
                if (exchanges == 8) { send(0,'','eme'); return; }
                exchanges++;
                const challenge = new Uint8Array(event.message);
                queue = queue.then(() => requestLicense(challenge, session));
              });
              await session.generateRequest('cenc', input);
            } catch { send(0,'','eme'); }
          } else await requestLicense(input);
        })();
        """;
}
