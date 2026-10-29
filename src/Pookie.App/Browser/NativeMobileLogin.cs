using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Pookie.App.Diagnostics;
using Pookie.Logging;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal static class NativeMobileLogin
{
    internal const string CookieScript = """
        (() => {
          if (window.__pookieMobileCookie) return;
          window.__pookieMobileCookie = true;
          let previous = '';
          function publish() {
            try {
              const key = window.dataDomeOptions?.ddCookieSessionName || 'ddSession_datadome';
              const cookie = document.cookie.split(';').map(item => item.trim()).find(item => item.startsWith('datadome='));
              const value = window.localStorage.getItem(key) || (cookie ? decodeURIComponent(cookie.slice(9)) : '');
              if (/^[A-Za-z0-9._~+/%=-]{1,4096}$/.test(value) && value !== previous) {
                previous = value;
                window.__pookiePost({id:'pookie:web-api',version:1,data:{kind:'protection-session',data_dome_client_id:value}});
              }
            } catch { /* Cookie observation must not interrupt authentication. */ }
          }
          publish(); setInterval(publish, 200);
        })();
        """;

    public static async Task<MobileAuthorizationResult> AuthorizeAsync(MobileAuthorizationRequest request, string profilePath,
        CancellationToken token, string? fixtureUri = null)
    {
        token.ThrowIfCancellationRequested();
        if (!request.IsValid()) throw new ArgumentException("Некорректный запрос мобильного входа.");
        ValidateFixture(fixtureUri);
        using var profile = BrowserProfile.Open(fixtureUri, profilePath);
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Не найден исполняемый файл Pookie."))
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet")
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(NativeMobileLogin).Assembly.GetName().Name + ".dll"));
        foreach (var argument in new[] { "--mobile-login", pipe.GetClientHandleAsString(), "--login-profile", profile.Path }) start.ArgumentList.Add(argument);
        if (fixtureUri != null) { start.ArgumentList.Add("--login-fixture"); start.ArgumentList.Add(fixtureUri); }
        StartupLog.EnsureInitialized(); AppLog.ConfigureChild(start);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить мобильный вход.");
        pipe.DisposeLocalCopyOfClientHandle();
        var stdout = DrainAsync(child.StandardOutput); var stderr = DrainAsync(child.StandardError);
        try
        {
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, SoundCloudJson.Default.MobileAuthorizationRequest));
            child.StandardInput.Close();
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var result = await ReadAuthorizationResultAsync(reader, token);
            token.ThrowIfCancellationRequested();
            if (result == null)
            {
                await child.WaitForExitAsync(token);
                if (child.ExitCode == 1) throw new OperationCanceledException("Окно мобильного входа закрыто.", token);
                throw new InvalidOperationException("Не удалось открыть окно мобильного входа.");
            }
            return result;
        }
        finally
        {
            if (!child.HasExited && !token.IsCancellationRequested)
                try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { StartupLog.Event("browser.mobile-login-exit-timeout"); }
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(); await Task.WhenAll(stdout, stderr);
        }
    }

    public static int RunChild(string handle, string profilePath, string? fixtureUri)
    {
        AppLog.Initialize(Storage.AppDataPaths.DefaultRoot(), worker: true);
        try
        {
            ValidateFixture(fixtureUri);
            var line = Console.In.ReadLine();
            if (line is not { Length: > 0 and <= 16384 }) return 2;
            var request = JsonSerializer.Deserialize(line, SoundCloudJson.Default.MobileAuthorizationRequest);
            if (request?.IsValid() != true) return 2;
            using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            var publicationGate = new object(); var published = false;
            void Publish(MobileAuthorizationResult value)
            {
                lock (publicationGate)
                {
                    if (published) return;
                    writer.WriteLine(JsonSerializer.Serialize(value, SoundCloudJson.Default.MobileAuthorizationResult));
                    published = true;
                }
            }
            MobileAuthorizationResult? result = null;
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") != "infiniframe")
                result = WebKitLoginWindow.RunMobile(request, profilePath, fixtureUri, Publish);
            else result = InfiniFrameLoginWindow.RunMobile(request, profilePath, fixtureUri, Publish);
            if (result == null) return 1;
            Publish(result);
            return 0;
        }
        catch (Exception error)
        {
            AppLog.Failure("Pookie.Login", "Не удалось выполнить мобильный вход", error);
            return 2;
        }
    }

    internal static async Task<MobileAuthorizationResult?> ReadAuthorizationResultAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[32769]; var count = 0;
        while (count < buffer.Length)
        {
            var size = await reader.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
            if (size == 0) { if (count == 0) return null; break; }
            var newline = Array.IndexOf(buffer, '\n', count, size);
            if (newline >= 0) { count = newline; break; }
            count += size;
        }
        if (count == buffer.Length) throw new InvalidOperationException("Слишком большой ответ мобильного входа.");
        var result = JsonSerializer.Deserialize(new string(buffer, 0, count), SoundCloudJson.Default.MobileAuthorizationResult);
        if (result?.IsValid() != true) throw new InvalidOperationException("Некорректный ответ мобильного входа.");
        return result;
    }

    internal static string StartUrl(MobileAuthorizationRequest request, string? fixture) => fixture == null ? request.Url :
        new UriBuilder(fixture) { Query = new Uri(request.Url).Query.TrimStart('?') }.Uri.AbsoluteUri;

    private static void ValidateFixture(string? fixture)
    {
        if (fixture != null && (!Uri.TryCreate(fixture, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !uri.IsLoopback || uri.UserInfo != "" || uri.Fragment != ""))
            throw new ArgumentException("Тест входа должен использовать локальный HTTP-адрес.");
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer) != 0) { /* Browser diagnostics can contain callback URLs; never forward them. */ }
    }
}
