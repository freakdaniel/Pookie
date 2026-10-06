using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

// One live website per account. Secrets travel through stdin, never through argv or logs.
internal sealed class NativeBrowserSession(WebSession account, string? fixtureUri = null, string? profilePath = null) : IDisposable, IAsyncDisposable, ISoundCloudBrowserTransport, IBrowserAudioSession
{
    private readonly SemaphoreSlim requests = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object sync = new();
    private Worker? worker;
    private readonly List<Worker> workers = [];
    public event Action<BrowserRequestEvent>? Changed;
    public WebSession Account { get { lock (sync) return account; } }

    private void BrowserChanged(BrowserRequestEvent message)
    {
        if (message.Kind == "protection-session" && message.DataDomeClientId is { } value)
            lock (sync) account.UpdateDataDomeClientId(value);
        Changed?.Invoke(message);
    }

    public async Task<SoundCloudUser> GetMeAsync(CancellationToken token = default)
    {
        var result = await RequestAsync(new(Guid.NewGuid().ToString("N"), "me"), token);
        return result.User is { Id: > 0 } user ? user : throw new InvalidOperationException("SoundCloud не вернул профиль.");
    }

    public async Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken token = default) =>
        _ = await RequestAsync(new(Guid.NewGuid().ToString("N"), "like", userId, trackId, liked), token);

    public async Task<HashSet<long>> GetLikedIdsAsync(CancellationToken token = default) =>
        (await RequestAsync(new(Guid.NewGuid().ToString("N"), "liked-ids"), token)).Ids?.ToHashSet() ?? [];

    public Task<BrowserRequestEvent> SendAudioAsync(BrowserAudioCommand command, CancellationToken token = default) =>
        RequestAsync(new(Guid.NewGuid().ToString("N"), "audio", Audio: command), token);

    public async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync(new(Guid.NewGuid().ToString("N"), "api-get", Url: uri.AbsoluteUri), cancellationToken);
        try { return JsonDocument.Parse(result.Json ?? throw new SoundCloudException("WebView не передал ответ SoundCloud.")); }
        catch (JsonException) { throw new SoundCloudException("SoundCloud вернул некорректный JSON."); }
    }

    private async Task<BrowserRequestEvent> RequestAsync(BrowserRequestCommand command, CancellationToken token)
    {
        if (!command.IsValid()) throw new ArgumentException("Некорректная команда браузера.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var ordered = command.Operation != "audio";
        if (ordered) await requests.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            Worker? stoppedWorker;
            lock (sync) stoppedWorker = worker is { Stopped: true } ? worker : null;
            if (stoppedWorker != null) await stoppedWorker.Completion.WaitAsync(timeout.Token).ConfigureAwait(false);
            Worker current;
            lock (sync)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (worker == null || worker.Stopped)
                {
                    worker = new Worker(account, fixtureUri, profilePath, BrowserChanged);
                    workers.Add(worker);
                }
                current = worker;
            }
            // Writes cannot survive cancellation and replay later. Reads cancel only
            // their fetch, preserving the website and its device-check state.
            using var cancellation = command.Operation == "like" ? timeout.Token.Register(current.Abort) : default;
            await current.Ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (current.Blocked && (command.Operation != "audio" || command.Audio?.Action == "start")) throw BrowserBlocked();
            var response = new TaskCompletionSource<BrowserRequestEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            current.Pending[command.Id] = response;
            try
            {
                timeout.Token.ThrowIfCancellationRequested();
                await current.SendAsync(command, timeout.Token).ConfigureAwait(false);
                using var readCancellation = command.Operation != "like"
                    ? timeout.Token.Register(() => _ = current.CancelReadAsync(command.Id)) : default;
                var result = await response.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (result.Status is >= 200 and < 300) return result;
                throw result.Status switch
                {
                    401 => new SoundCloudException("Сессия SoundCloud истекла. Войди в аккаунт ещё раз.", 401),
                    403 when current.Blocked => BrowserBlocked(),
                    403 => new SoundCloudException("SoundCloud отклонил запрос в браузере. Проверка могла не завершиться или ресурс недоступен.", 403),
                    404 => new SoundCloudException("Трек или ресурс SoundCloud не найден.", 404),
                    429 => new SoundCloudException("SoundCloud ограничил частоту запросов. Повтори позже.", 429),
                    0 => new SoundCloudException("WebView не завершил запрос. Проверь соединение и окно проверки SoundCloud."),
                    _ => new SoundCloudException($"SoundCloud не выполнил действие (HTTP {result.Status}).", result.Status)
                };
            }
            finally { current.Pending.TryRemove(command.Id, out _); }
        }
        finally { if (ordered) requests.Release(); }
    }

    private static SoundCloudException BrowserBlocked() => new(
        "SoundCloud временно заблокировал доступ для этого браузера или сети. Капча не предложена. Подробности — в окне сайта; действие не повторяется.", 403);

    public void Dispose()
    {
        lock (sync)
        {
            if (lifetime.IsCancellationRequested) return;
            lifetime.Cancel();
            foreach (var item in workers) item.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        Worker[] all;
        lock (sync) all = workers.ToArray();
        foreach (var item in all) await item.Completion.ConfigureAwait(false);
    }

    public static int RunChild(string handle, string profile, string? fixture)
    {
        try
        {
            ValidateFixture(fixture);
            // Read synchronously: GTK/WebKit must be initialized on the STA entry thread.
            var line = Console.In.ReadLine();
            if (line is not { Length: > 0 and <= 10000 }) return 2;
            var session = JsonSerializer.Deserialize(line, SoundCloudJson.Default.WebSession);
            if (session?.IsValid() != true) return 2;
            using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            void Received(BrowserRequestEvent message) => writer.WriteLine(JsonSerializer.Serialize(message, SoundCloudJson.Default.BrowserRequestEvent));
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("POOKIE_LOGIN_ENGINE") != "infiniframe")
                WebKitLoginWindow.RunRequests(session, Received, profile, fixture);
            else InfiniFrameLoginWindow.RunRequests(session, Received, profile, fixture);
            return 0;
        }
        catch (Exception error)
        {
            if (fixture != null) Console.Error.WriteLine(error.GetType().Name);
            return 2;
        }
    }

    private static void ValidateFixture(string? fixture)
    {
        if (fixture != null && (!Uri.TryCreate(fixture, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http" || uri.UserInfo != ""))
            throw new ArgumentException("Тестовый WebView допускает только локальный HTTP адрес.");
    }

    private sealed class Worker
    {
        public readonly Process Process;
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentDictionary<string, TaskCompletionSource<BrowserRequestEvent>> Pending = new();
        public readonly Task Completion;
        private readonly WebSession account;
        private readonly BrowserProtectionStore protection;
        private readonly SemaphoreSlim input = new(1, 1);
        private int stopped;
        private int blocked;
        public bool Stopped => Volatile.Read(ref stopped) != 0;
        public bool Blocked => Volatile.Read(ref blocked) != 0;

        public Worker(WebSession account, string? fixture, string? profilePath, Action<BrowserRequestEvent> changed)
        {
            ValidateFixture(fixture);
            if (!account.IsValid()) throw new ArgumentException("Некорректная сессия SoundCloud.");
            var profile = BrowserProfile.Open(fixture, profilePath);
            this.account = account;
            protection = new BrowserProtectionStore(profile.Path);
            // The persistent native profile restores website state with its
            // original attributes. A .NET token snapshot must never seed it.
            var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(NativeBrowserSession).Assembly.Location);
                foreach (var argument in new[] { "--web-requests", pipe.GetClientHandleAsString(), "--login-profile", profile.Path }) start.ArgumentList.Add(argument);
                if (fixture != null) { start.ArgumentList.Add("--login-fixture"); start.ArgumentList.Add(fixture); }
                Process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить WebView.");
                pipe.DisposeLocalCopyOfClientHandle();
                Process.StandardInput.WriteLine(JsonSerializer.Serialize(account, SoundCloudJson.Default.WebSession));
                Process.StandardInput.Flush();
                Completion = ReadAsync(pipe, profile, changed);
            }
            catch
            {
                if (Process != null)
                {
                    try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    Process.Dispose();
                }
                pipe.Dispose();
                profile.Dispose();
                throw;
            }
        }

        public async Task SendAsync(BrowserRequestCommand command, CancellationToken token)
        {
            await input.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                // A line must be written atomically; cancel via the next command.
                await Process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand)).ConfigureAwait(false);
                await Process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally { input.Release(); }
        }

        public async Task CancelReadAsync(string id)
        {
            try { await SendAsync(new(id, "cancel"), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
        }

        public void Abort()
        {
            // Cancelled writes must not survive long enough to replay after a challenge.
            Interlocked.Exchange(ref stopped, 1);
            try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            // EOF lets WebKit/WebView2 close normally and persist website storage.
            try { Process.StandardInput.Close(); }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
            _ = KillIfStillRunningAsync();
        }

        private async Task KillIfStillRunningAsync()
        {
            try
            {
                await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
        }

        private async Task ReadAsync(AnonymousPipeServerStream pipe, BrowserProfile profile, Action<BrowserRequestEvent> changed)
        {
            var batches = new Dictionary<string, List<long>>();
            var bodies = new Dictionary<string, (StringBuilder Text, int Next)>();
            var stdout = DrainAsync(Process.StandardOutput);
            var stderr = DrainAsync(Process.StandardError);
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (line.Length > 10000) continue;
                    var message = JsonSerializer.Deserialize(line, SoundCloudJson.Default.BrowserRequestEvent);
                    if (message == null) continue;
                    if (message.Kind == "protection-session" && message.DataDomeClientId is { } value)
                        protection.Save(account, value);
                    if (message.Kind == "ready") Ready.TrySetResult();
                    if (message.Kind == "blocked")
                    {
                        Interlocked.Exchange(ref blocked, 1);
                        Ready.TrySetException(BrowserBlocked());
                    }
                    if (message.Kind == "ids" && Pending.ContainsKey(message.RequestId) && message.Ids is { } ids)
                    {
                        if (!batches.TryGetValue(message.RequestId, out var batch)) batches[message.RequestId] = batch = [];
                        if (batch.Count + ids.Length > 100000) throw new IOException("Too many IDs");
                        batch.AddRange(ids);
                    }
                    // Discard partial responses from cancelled reads before allocating more.
                    foreach (var id in bodies.Keys.Where(id => !Pending.ContainsKey(id)).ToArray()) bodies.Remove(id);
                    foreach (var id in batches.Keys.Where(id => !Pending.ContainsKey(id)).ToArray()) batches.Remove(id);
                    if (message.Kind == "json-chunk" && Pending.ContainsKey(message.RequestId))
                    {
                        if (!bodies.TryGetValue(message.RequestId, out var body)) body = (new StringBuilder(), 0);
                        if (message.ChunkIndex != body.Next || message.Chunk is not { Length: > 0 and <= 1024 } ||
                            body.Text.Length + message.Chunk.Length > 2 * 1024 * 1024 || body.Next >= 4096)
                            throw new IOException("Invalid browser response chunks");
                        body.Text.Append(message.Chunk);
                        bodies[message.RequestId] = (body.Text, body.Next + 1);
                    }
                    if (message.Kind == "complete" && Pending.TryGetValue(message.RequestId, out var pending))
                    {
                        if (batches.Remove(message.RequestId, out var collected)) message = message with { Ids = collected.ToArray() };
                        if (bodies.Remove(message.RequestId, out var body)) message = message with { Json = body.Text.ToString() };
                        pending.TrySetResult(message);
                    }
                    if (message.Kind is "checking" or "blocked" or "passed" or "challenge-error" or "protection-session" or "audio-state")
                        try { changed(message); } catch (InvalidOperationException) { }
                }
            }
            catch (Exception error) when (error is IOException or JsonException or ObjectDisposedException) { }
            finally
            {
                Stop();
                changed(new("audio-closed"));
                var error = new InvalidOperationException("Окно SoundCloud закрыто или WebView завершился. Повтори действие, чтобы открыть его снова.");
                Ready.TrySetException(error);
                foreach (var pending in Pending.Values) pending.TrySetException(error);
                await Process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                Process.Dispose();
                profile.Dispose();
            }
        }

        private static async Task DrainAsync(StreamReader reader)
        {
            var buffer = new char[2048];
            while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
        }
    }
}
