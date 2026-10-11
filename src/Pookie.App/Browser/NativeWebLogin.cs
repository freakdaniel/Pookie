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
    public static async Task<WebSession> ConnectAsync(CancellationToken token, string? fixtureUri = null, string? profilePath = null, bool resetSession = false)
    {
        token.ThrowIfCancellationRequested();
        using var profile = BrowserProfile.Open(fixtureUri, profilePath);
        if (resetSession) await profile.ResetAsync(token);
        return await RunProcessAsync(token, profile.Path, fixtureUri);
    }

    public static async Task<WebSession?> TryRestoreAsync(CancellationToken token, string? fixtureUri = null, string? profilePath = null)
    {
        var path = profilePath ?? BrowserProfile.DefaultPath();
        if (!Directory.Exists(Path.Combine(path, "Partitions", "soundcloud"))) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var profile = BrowserProfile.Open(fixtureUri, path);
            return await RunProcessAsync(timeout.Token, profile.Path, fixtureUri, hidden: true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static async Task<WebSession> RunProcessAsync(CancellationToken token, string profilePath, string? fixtureUri, bool hidden = false)
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var start = ElectronProcess.Create("--app-login", pipe.GetClientHandleAsString(), profilePath, fixtureUri);
        if (hidden) start.ArgumentList.Add("--verify-session");
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
                    ? "Не удалось открыть окно входа. Проверь наличие браузерного процесса Electron в установке Pookie."
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
