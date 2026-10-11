using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElectronNET;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Pookie.SoundCloud;

namespace Pookie.Browser.ElectronHost;

// This process owns Chromium. Only the private inherited pipe carries credentials.
internal static class ApplicationBrowserHost
{
    private sealed record Boot(WebSession Account, string Script);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args)
    {
        var output = Console.Out;
        var started = System.Diagnostics.Stopwatch.StartNew();
        void Milestone(string stage) => output.WriteLine($"POOKIE_STARTUP|{started.ElapsedMilliseconds}|{stage}");
        var login = args.Contains("--app-login");
        string? handle = null;
        var parsed = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--app-login" or "--app-worker") continue;
            if (args[i] == "--pipe" && i + 1 < args.Length) { handle = args[++i]; continue; }
            parsed.Add(args[i]);
        }
        // Electron.NET writes its transport secret to stdout. Never forward that output.
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        var runtime = ElectronNetRuntime.RuntimeController;
        BrowserWindow? window = null;
        try
        {
            if (string.IsNullOrEmpty(handle)) return 2;
            var options = PrototypeOptions.Parse(parsed.ToArray());
            using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            // The parent holds the profile lease through reset, launch and graceful shutdown.
            using var lifetime = new CancellationTokenSource();
            Boot? boot = null;
            if (!login)
            {
                var line = await Console.In.ReadLineAsync();
                if (line is not { Length: > 0 and <= 2000000 }) return 2;
                boot = JsonSerializer.Deserialize<Boot>(line, Json);
                if (boot?.Account.IsValid() != true || string.IsNullOrEmpty(boot.Script)) return 2;
            }
            Environment.SetEnvironmentVariable("POOKIE_ELECTRON_PROFILE", options.Profile);
            Environment.SetEnvironmentVariable("POOKIE_ELECTRON_ORIGIN", options.Origin);
            Environment.SetEnvironmentVariable("POOKIE_ELECTRON_API_ORIGIN", options.ApiOrigin);
            Milestone("browser.child-input-ready");
            await runtime.Start();
            await runtime.WaitReadyTask.WaitAsync(TimeSpan.FromSeconds(30));
            window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions
            {
                Width = 1000, Height = 800, Show = false,
                Title = login ? "Pookie — вход в SoundCloud" : "Pookie — проверка SoundCloud",
                WebPreferences = new WebPreferences
                {
                    NodeIntegration = false, ContextIsolation = true, Sandbox = true, WebSecurity = true,
                    BackgroundThrottling = false, Partition = "persist:soundcloud",
                    Preload = Path.Combine(AppContext.BaseDirectory, ".electron", "preload.js")
                }
            }, "about:blank");
            Milestone("browser.window-created");
            window.OnClosed += lifetime.Cancel;
            if (login && !options.Verify) window.Show();
            // Network failures must not prevent stdin EOF from terminating the helper.
            var commands = new ConcurrentQueue<BrowserRequestCommand>();
            var input = login ? Task.CompletedTask : Task.Run(() => ReadCommandsAsync(commands, lifetime));
            var navigation = window.WebContents.LoadURLAsync(options.StartUrl);
            _ = navigation.ContinueWith(task => { if (task.IsFaulted) { _ = task.Exception; lifetime.Cancel(); } }, TaskScheduler.Default);
            var pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var injected = false;
            var documentBlocked = false;
            string? sessionSignature = null;
            var installedAt = DateTimeOffset.MinValue;
            while (!lifetime.IsCancellationRequested)
            {
                var context = await Program.Evaluate(window, Program.ReadContext(options.Origin), lifetime.Token);
                var live = context.TryGetProperty("session", out var value) ? value.Deserialize<WebSession>(Json) : null;
                if (!login && !documentBlocked && context.TryGetProperty("blocked", out var blocked) && blocked.ValueKind == JsonValueKind.True)
                {
                    documentBlocked = true;
                    window.Show();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new BrowserRequestEvent("blocked", Interactive: true, ChallengeType: "hard_block"), SoundCloudJson.Default.BrowserRequestEvent));
                }
                if (!login && live is { AccessToken.Length: > 0, ClientId.Length: 0 })
                    live = live with { ClientId = boot!.Account.ClientId };
                if (login)
                {
                    if (live?.IsValid() == true)
                    {
                        await Program.Evaluate(window, "window.pookiePrototype.flush()", lifetime.Token);
                        await writer.WriteAsync(JsonSerializer.Serialize(live, SoundCloudJson.Default.WebSession));
                        return 0;
                    }
                }
                else
                {
                    if (DateTimeOffset.UtcNow - installedAt > TimeSpan.FromMilliseconds(300))
                    {
                        var account = live?.IsValid() == true ? live : boot!.Account;
                        var script = boot!.Script.Replace("__POOKIE_ACCOUNT__", JsonSerializer.Serialize(account with { DataDomeClientId = null }, SoundCloudJson.Default.WebSession));
                        var bridge = "(() => {if(location.origin !== " + JsonSerializer.Serialize(options.Origin) +
                            " || window !== window.top || !window.pookiePrototype) return false;" +
                            "window.__pookiePost = message => window.pookiePrototype.post(JSON.stringify({...message,pookie_pairing:" +
                            JsonSerializer.Serialize(pairing) + "}));\n" + script + "\nreturn true;})()";
                        var installed = await Program.Evaluate(window, bridge, lifetime.Token);
                        if (installed.ValueKind == JsonValueKind.True && !injected)
                        {
                            injected = true;
                            Milestone("browser.bridge-installed");
                        }
                        installedAt = DateTimeOffset.UtcNow;
                    }
                    if (injected)
                    {
                        if (live?.IsValid() == true)
                        {
                            var serialized = JsonSerializer.Serialize(live with { DataDomeClientId = null }, SoundCloudJson.Default.WebSession);
                            if (serialized != sessionSignature)
                            {
                                await Program.Evaluate(window, "(() => {if(location.origin === " + JsonSerializer.Serialize(options.Origin) + ") window.__pookieUpdateSession?.(" + serialized + "); return true;})()", lifetime.Token);
                                await writer.WriteLineAsync(JsonSerializer.Serialize(new BrowserRequestEvent("session") { Session = live }, SoundCloudJson.Default.BrowserRequestEvent));
                                sessionSignature = serialized;
                            }
                        }
                        while (commands.TryDequeue(out var command))
                        {
                            await Program.Evaluate(window, "(() => {if(location.origin === " + JsonSerializer.Serialize(options.Origin) +
                                ") window.__pookieRequest?.(" + JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand) + "); return true;})()", lifetime.Token);
                        }
                        var packets = await Program.Evaluate(window, "window.pookiePrototype?.drain() || []", lifetime.Token);
                        if (packets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var packet in packets.EnumerateArray())
                            {
                                var message = BrowserRequestProtocol.Parse(packet.GetString() ?? "", options.Origin, options.Origin, pairing);
                                if (message == null) continue;
                                if (message.Kind is "checking" or "blocked" && message.Interactive) window.Show();
                                if (message.Kind is "ready" or "passed" or "challenge-error") window.Hide();
                                await writer.WriteLineAsync(JsonSerializer.Serialize(message, SoundCloudJson.Default.BrowserRequestEvent));
                            }
                        }
                    }
                }
                await Task.Delay(login ? 250 : 40, lifetime.Token);
            }
            await input;
            return 1;
        }
        catch (OperationCanceledException) { return 1; }
        catch { return 2; }
        finally
        {
            try
            {
                if (window != null)
                {
                    // Flush even when the top document is a challenge or another origin.
                    Electron.App.Quit();
                }
                await runtime.WaitStoppedTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { try { await runtime.Stop(); } catch { } }
        }
    }

    private static async Task ReadCommandsAsync(ConcurrentQueue<BrowserRequestCommand> queue, CancellationTokenSource lifetime)
    {
        try
        {
            while (await Console.In.ReadLineAsync(lifetime.Token) is { } line)
            {
                if (line.Length > BrowserRequestCommand.MaxLength) continue;
                BrowserRequestCommand? command;
                try { command = JsonSerializer.Deserialize(line, SoundCloudJson.Default.BrowserRequestCommand); }
                catch (JsonException) { continue; }
                if (command?.IsValid() != true) continue;
                if (queue.Count >= 256) break;
                queue.Enqueue(command);
            }
        }
        catch (OperationCanceledException) { }
        finally { lifetime.Cancel(); }
    }
}
