using Pookie.App.Diagnostics;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Pookie.SoundCloud;
using Pookie.Logging;

namespace Pookie.App.Browser;

internal static class NativeWebLogin
{
    public static async Task<WebSession> ConnectAsync(CancellationToken token, string? fixtureUri = null, string? profilePath = null, bool resetSession = true)
    {
        token.ThrowIfCancellationRequested();
        using var profile = BrowserProfile.Open(fixtureUri, profilePath);
        if (resetSession) await profile.ResetAsync(token);
        return await RunProcessAsync(token, profile.Path, fixtureUri);
    }

    private static async Task<WebSession> RunProcessAsync(CancellationToken token, string profilePath, string? fixtureUri)
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Не найден исполняемый файл Pookie."))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(NativeWebLogin).Assembly.Location);
        start.ArgumentList.Add("--web-login");
        start.ArgumentList.Add(pipe.GetClientHandleAsString());
        start.ArgumentList.Add("--login-profile");
        start.ArgumentList.Add(profilePath);
        if (fixtureUri != null) { start.ArgumentList.Add("--login-fixture"); start.ArgumentList.Add(fixtureUri); }
        StartupLog.EnsureInitialized();
        AppLog.ConfigureChild(start);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить окно входа.");
        pipe.DisposeLocalCopyOfClientHandle();
        // Drain browser-engine diagnostics without exposing login headers/cookies in application logs.
        var stdout = DrainAsync(child.StandardOutput, fixtureUri != null);
        var stderr = DrainAsync(child.StandardError, fixtureUri != null);
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            var buffer = new char[10001];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), token);
                if (read == 0) break;
                count += read;
            }
            token.ThrowIfCancellationRequested();
            if (count == 0)
            {
                await child.WaitForExitAsync(token);
                if (fixtureUri != null) Console.Error.WriteLine($"LOGIN_CHILD_EXIT: {child.ExitCode}");
                throw new InvalidOperationException(child.ExitCode == 2
                    ? "Не удалось открыть окно входа. На Linux нужен libwebkit2gtk-4.1-0, на Windows — WebView2 Runtime."
                    : child.ExitCode == 3 ? "Не удалось передать сессию SoundCloud в Pookie. Перезапустите окно входа."
                    : child.ExitCode == 1 ? "Окно входа закрыто. Подключение к SoundCloud отменено."
                    : "Окно входа аварийно завершилось.");
            }
            if (count == buffer.Length) throw new InvalidOperationException("Некорректный ответ окна входа.");
            var session = JsonSerializer.Deserialize(new string(buffer, 0, count), SoundCloudJson.Default.WebSession);
            if (session == null || !session.IsValid()) throw new InvalidOperationException("Некорректный ответ окна входа.");
            return session;
        }
        finally
        {
            if (!child.HasExited && !token.IsCancellationRequested)
                try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { }
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    public static int RunChild(string handle, string profilePath, string? fixtureUri)
    {
        AppLog.Initialize(Pookie.App.Storage.AppDataPaths.DefaultRoot(), worker: true);
        try
        {
            using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
            void Connected(WebSession session)
            {
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.Write(JsonSerializer.Serialize(session, SoundCloudJson.Default.WebSession));
            }
            var useWebKit = OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") != "infiniframe";
            var connected = useWebKit ? WebKitLoginWindow.Run(Connected, profilePath, fixtureUri) : InfiniFrameLoginWindow.Run(Connected, profilePath, fixtureUri);
            return connected ? 0 : 1;
        }
        catch (InfiniFrameLoginWindow.BridgeException error)
        {
            AppLog.Failure("Pookie.Login", "Ошибка обмена с окном входа", error);
            return 3;
        }
        catch (Exception error)
        {
            AppLog.Failure("Pookie.Login", "Не удалось открыть окно входа", error);
            return 2;
        }
    }

    private static async Task DrainAsync(StreamReader reader, bool fixtureDiagnostics)
    {
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            if (fixtureDiagnostics) Console.Error.Write(new string(buffer, 0, count));
        }
    }
}
