using System.Text.Json;
using ElectronNET;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Pookie.SoundCloud;
using Pookie.Browser.ElectronHost;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--app-login") || args.Contains("--app-worker"))
            return await ApplicationBrowserHost.RunAsync(args);
        // Never print framework transport tokens, request headers, page URLs or cookies.
        var output = Console.Out;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        void Report(object result) => output.WriteLine("POOKIE_ELECTRON_RESULT " + JsonSerializer.Serialize(result, Json));
        PrototypeOptions options;
        try { options = PrototypeOptions.Parse(args); }
        catch (ArgumentException error) { Report(new { kind = "invalid-options", message = error.Message }); return 2; }
        Directory.CreateDirectory(options.Profile);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(options.Profile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        FileStream lease;
        try { lease = new(Path.Combine(options.Profile, ".pookie-profile.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { Report(new { kind = "profile-in-use" }); return 3; }
        using (lease)
        {
            // custom_main.js reads these before Chromium creates any sessions.
            Environment.SetEnvironmentVariable("POOKIE_ELECTRON_PROFILE", options.Profile);
            Environment.SetEnvironmentVariable("POOKIE_ELECTRON_ORIGIN", options.Origin);
            Environment.SetEnvironmentVariable("POOKIE_ELECTRON_API_ORIGIN", options.ApiOrigin);
            using var stop = new CancellationTokenSource(options.Timeout);
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
            var runtime = ElectronNetRuntime.RuntimeController;
            BrowserWindow? window = null;
            var windowClosed = false;
            try
            {
                var binary = Path.Combine(AppContext.BaseDirectory, ".electron", "node_modules", "electron", "dist",
                    OperatingSystem.IsWindows() ? "electron.exe" : OperatingSystem.IsMacOS() ? "Electron.app/Contents/MacOS/Electron" : "electron");
                if (!File.Exists(binary)) { Report(new { kind = "runtime-missing", message = "Собери проект с -p:PookieInstallElectron=true (нужны Node.js и npm)." }); return 2; }
                await runtime.Start();
                await runtime.WaitReadyTask.WaitAsync(TimeSpan.FromSeconds(30), stop.Token);
                window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions
                {
                    Width = 1000, Height = 800, Show = false, Title = "Pookie — вход в SoundCloud",
                    WebPreferences = new WebPreferences
                    {
                        NodeIntegration = false, ContextIsolation = true, Sandbox = true, WebSecurity = true,
                        Partition = "persist:soundcloud",
                        Preload = Path.Combine(AppContext.BaseDirectory, ".electron", "preload.js")
                    }
                }, "about:blank").WaitAsync(TimeSpan.FromSeconds(10), stop.Token);
                window.OnClosed += () => { windowClosed = true; stop.Cancel(); };
                Report(new { kind = "started", mode = options.Verify ? "verify" : "login" });
                if (!options.Verify) window.Show();
                await window.WebContents.LoadURLAsync(options.StartUrl).WaitAsync(TimeSpan.FromSeconds(45), stop.Token);

                WebSession? account = null;
                JsonElement context = default;
                while (account == null)
                {
                    context = await Evaluate(window, ReadContext(options.Origin), stop.Token);
                    if (context.TryGetProperty("blocked", out var blocked) && blocked.GetBoolean())
                    {
                        Report(new { kind = "site-blocked" });
                        return 4;
                    }
                    if (context.TryGetProperty("session", out var session))
                    {
                        account = session.Deserialize<WebSession>(Json);
                        if (account?.IsValid() != true) account = null;
                    }
                    if (account == null) await Task.Delay(500, stop.Token);
                }
                // The authenticated request runs through the real website fetch/DataDome tag.
                var result = await Evaluate(window, ReadMe(options, account), stop.Token);
                var status = result.TryGetProperty("status", out var statusValue) ? statusValue.GetInt32() : 0;
                if (status != 200 || !result.TryGetProperty("user", out var userValue) ||
                    userValue.Deserialize<SoundCloudUser>(Json) is not { Id: > 0, Username.Length: > 0 } user)
                {
                    Report(new { kind = "profile-failed", status });
                    return 4;
                }
                await Evaluate(window, "window.pookiePrototype.flush()", stop.Token);
                Report(new { kind = "profile", status, user = new { user.Id, user.Username },
                    userAgent = account.UserAgent, runtime = context.GetProperty("runtime") });
                return 0;
            }
            catch (OperationCanceledException) { Report(new { kind = windowClosed ? "closed" : "timeout-or-cancelled" }); return 1; }
            catch (TimeoutException) { Report(new { kind = "browser-timeout" }); return 2; }
            catch (Exception) { Report(new { kind = "browser-failed" }); return 2; }
            finally
            {
                try
                {
                    if (window != null) Electron.App.Quit();
                    await runtime.WaitStoppedTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch { try { await runtime.Stop(); } catch { } }
            }
        }
    }

    internal static async Task<JsonElement> Evaluate(BrowserWindow window, string script, CancellationToken token)
    {
        // ExecuteJavaScript has a shared reply channel in Core 0.6; keep calls sequential.
        // Always resolve with JSON so renderer failures cannot leave its callback hanging.
        var wrapped = "(async()=>{try{return JSON.stringify(await (" + script + "));}catch{return '{}';}})()";
        var raw = await window.WebContents.ExecuteJavaScriptAsync<string>(wrapped).WaitAsync(TimeSpan.FromSeconds(20), token);
        return JsonSerializer.Deserialize<JsonElement>(raw, Json);
    }

    internal static string ReadContext(string origin) => $$"""
        (async () => {
          if (location.origin !== {{JsonSerializer.Serialize(origin)}} || !window.pookiePrototype) return {};
          const state = await window.pookiePrototype.readSession();
          const hydration = window.__sc_hydration;
          const id = Array.isArray(hydration) ? hydration.find(x => x.hydratable === 'apiClient')?.data?.id : '';
          const protectionName = window.dataDomeOptions?.ddCookieSessionName || 'ddSession_datadome';
          let protection = '';
          try { protection = localStorage.getItem(protectionName) || ''; } catch {}
          return {
            blocked: /Доступ (временно )?ограничен|Access (temporarily )?restricted/i.test(document.title + ' ' + document.body?.innerText),
            runtime: state.runtime,
            session: { clientId: state.clientId || id || '', accessToken: state.accessToken || '',
              userAgent: navigator.userAgent, dataDomeClientId: protection || state.dataDomeClientId || null,
              appVersion: state.appVersion || window.__sc_version || null,
              appLocale: state.appLocale || document.documentElement.lang || null }
          };
        })()
        """;

    private static string ReadMe(PrototypeOptions options, WebSession account)
    {
        var url = options.ApiOrigin + (options.Origin == "https://soundcloud.com" ? "/me" : "/api/me") +
            "?client_id=" + Uri.EscapeDataString(account.ClientId);
        if (account.AppVersion != null) url += "&app_version=" + Uri.EscapeDataString(account.AppVersion);
        if (account.AppLocale != null) url += "&app_locale=" + Uri.EscapeDataString(account.AppLocale);
        return $$"""
            (async () => {
              if (location.origin !== {{JsonSerializer.Serialize(options.Origin)}}) return {};
              const headers = { Authorization: {{JsonSerializer.Serialize("OAuth " + account.AccessToken)}}, Accept: 'application/json' };
              // Same credentials mode as SoundCloud's API client. The live website tag
              // supplies protection headers; do not overwrite them with a snapshot.
              const response = await fetch({{JsonSerializer.Serialize(url)}}, { headers, credentials: 'same-origin', redirect: 'error', signal: AbortSignal.timeout(15000) });
              if (!response.ok) return { status: response.status };
              const user = await response.json();
              return { status: response.status, user: { id: user.id, username: user.username } };
            })()
            """;
    }
}
