using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pookie.App.Diagnostics;
using Pookie.Logging;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

// A fresh challenge-only profile. Never opens the SoundCloud site or imports a
// desktop login; the only exported state is an explicit SDK success callback.
internal sealed class NativeMobileChallenge(string? fixtureUri = null) : ISoundCloudMobileChallengeHandler
{
    private readonly SemaphoreSlim windowGate = new(1, 1);
    public async Task<string?> SolveAsync(SoundCloudMobileChallenge challenge, CancellationToken cancellationToken)
    {
        if (!challenge.IsValid()) throw new ArgumentException("Некорректная мобильная проверка SoundCloud.");
        ValidateFixture(fixtureUri);
        await windowGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        DirectoryInfo? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("pookie-mobile-challenge-");
            using var profile = BrowserProfile.Open(path: directory.FullName);
            using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Не найден исполняемый файл Pookie."))
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet")
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(NativeMobileChallenge).Assembly.GetName().Name + ".dll"));
            foreach (var argument in new[] { "--mobile-challenge", pipe.GetClientHandleAsString(), "--login-profile", profile.Path }) start.ArgumentList.Add(argument);
            if (fixtureUri != null) { start.ArgumentList.Add("--login-fixture"); start.ArgumentList.Add(fixtureUri); }
            StartupLog.EnsureInitialized(); AppLog.ConfigureChild(start);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Не удалось открыть мобильную проверку.");
            pipe.DisposeLocalCopyOfClientHandle();
            var stdout = DrainAsync(child.StandardOutput); var stderr = DrainAsync(child.StandardError);
            try
            {
                await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(challenge, SoundCloudJson.Default.SoundCloudMobileChallenge).AsMemory(), cancellationToken).ConfigureAwait(false);
                child.StandardInput.Close();
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var buffer = new char[16384]; var count = 0;
                while (count < buffer.Length)
                {
                    var size = await reader.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
                    if (size == 0) break;
                    var newline = Array.IndexOf(buffer, '\n', count, size);
                    if (newline >= 0) { count = newline; break; }
                    count += size;
                }
                if (count == 0)
                {
                    await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    if (child.ExitCode == 1) throw new OperationCanceledException("Мобильная проверка закрыта.", cancellationToken);
                    throw new SoundCloudException("Не удалось открыть мобильную проверку SoundCloud.");
                }
                if (count == buffer.Length) throw new SoundCloudException("Слишком большой ответ мобильной проверки.");
                var result = JsonSerializer.Deserialize(new string(buffer, 0, count), SoundCloudJson.Default.SoundCloudMobileChallengeResult);
                if (result?.IsValid() != true) throw new SoundCloudException("Некорректный ответ мобильной проверки.");
                if (result.Blocked) throw new SoundCloudException("SoundCloud заблокировал доступ. Капча не предложена.", 403, requiresBrowserVerification: true)
                    { ChallengeKind = SoundCloudChallengeKind.HardBlock };
                return result.Cookie;
            }
            finally
            {
                if (!child.HasExited)
                    try { child.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (child.HasExited) { }
                await child.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                for (var attempt = 0; directory != null; attempt++)
                {
                    try { directory.Delete(recursive: true); break; }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        if (attempt == 9) break;
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
            }
            finally { windowGate.Release(); }
        }
    }

    public static int RunChild(string handle, string profilePath, string? fixture)
    {
        try
        {
            ValidateFixture(fixture);
            var line = Console.In.ReadLine();
            if (line is not { Length: > 0 and <= 4 * 1024 * 1024 }) return 2;
            var challenge = JsonSerializer.Deserialize(line, SoundCloudJson.Default.SoundCloudMobileChallenge);
            if (challenge?.IsValid() != true) return 2;
            using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            var published = false;
            void Publish(SoundCloudMobileChallengeResult result)
            {
                if (published) return;
                writer.WriteLine(JsonSerializer.Serialize(result, SoundCloudJson.Default.SoundCloudMobileChallengeResult));
                published = true;
            }
            var result = OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") != "infiniframe"
                ? WebKitLoginWindow.RunMobileChallenge(challenge, profilePath, fixture, Publish)
                : InfiniFrameLoginWindow.RunMobileChallenge(challenge, profilePath, fixture, Publish);
            if (result == null) return 1;
            Publish(result); return 0;
        }
        catch { return 2; } // Challenge URLs, HTML and cookies must not reach logs.
    }

    internal static void ValidateFixture(string? fixture)
    {
        if (fixture != null && (!Uri.TryCreate(fixture, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !uri.IsLoopback || uri.UserInfo != "" || uri.Fragment != ""))
            throw new ArgumentException("Тест мобильной проверки должен использовать локальный HTTP-адрес.");
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
    }
}
