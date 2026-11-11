using Pookie.App.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

// Published InfiniFrame 0.62.1 initializes Linux WebKit on a worker thread, which aborts
// in current JavaScriptCore. This fallback keeps GTK/WebKit on the helper's main thread.
internal sealed partial class WebKitLoginWindow
{
    private readonly Action<WebSession> connected;
    private readonly string origin;
    private readonly Native.MessageCallback messageCallback;
    private readonly Native.DeleteCallback deleteCallback;
    private readonly Native.NotifyCallback uriCallback;
    private readonly Native.CreateCallback createCallback;
    private readonly Native.CloseCallback closeCallback;
    private readonly Dictionary<nint, (nint Window, nint Address)> views = [];
    private readonly HashSet<nint> managers = [];
    private readonly string script;
    private readonly bool background;
    private readonly Action<BrowserRequestEvent>? browserEvent;
    private readonly ConcurrentQueue<Action> dispatch = new();
    private readonly Native.IdleCallback idleCallback;
    private readonly string pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private nint rootWindow;
    private nint rootView;
    private WebKitContentFilter? contentFilter;
    private bool done;
    private bool hiddenLogin;

    private WebKitLoginWindow(Action<WebSession> connected, string origin, bool background,
        WebSession? account = null, Action<BrowserRequestEvent>? browserEvent = null)
    {
        this.connected = connected; this.origin = origin; this.background = background;
        this.browserEvent = browserEvent;
        idleCallback = DrainDispatch;
        messageCallback = MessageReceived; deleteCallback = WindowDeleted;
        uriCallback = UriChanged; createCallback = CreatePopup; closeCallback = ClosePopup;
        var scriptBody = account == null ? LoginCaptureScript.Read(origin) : BrowserRequestScript.Read(account, origin);
        CustomizeScript(ref scriptBody, account == null);
        script = "if(location.origin === " + JsonSerializer.Serialize(origin, Pookie.SoundCloud.SoundCloudJson.Default.String) + " && window === window.top) { (()=>{" +
            "const pairing=" + JsonSerializer.Serialize(pairing, Pookie.SoundCloud.SoundCloudJson.Default.String) + ";" +
            "window.__pookiePost=message=>window.webkit.messageHandlers.pookie.postMessage(JSON.stringify({...message,pookie_pairing:pairing}));" +
            scriptBody + "})(); }";
    }

    public static bool Run(Action<WebSession> connected, string profilePath, string? fixtureUri, bool hidden = false)
    {
        var origin = fixtureUri == null ? "https://soundcloud.com" : new Uri(fixtureUri).GetLeftPart(UriPartial.Authority);
        var app = new WebKitLoginWindow(connected, origin, false);
        app.hiddenLogin = hidden;
        return RunCore(app, fixtureUri ?? "https://soundcloud.com/you/likes", profilePath);
    }

    public static void RunRequests(WebSession account, Action<BrowserRequestEvent> received, string profilePath, string? fixtureUri)
    {
        var origin = fixtureUri == null ? "https://soundcloud.com" : new Uri(fixtureUri).GetLeftPart(UriPartial.Authority);
        var app = new WebKitLoginWindow(_ => { }, origin, true, account, received);
        RunCore(app, fixtureUri ?? "https://soundcloud.com/", profilePath);
    }

    private static bool RunCore(WebKitLoginWindow app, string startUri, string profilePath)
    {
        if (Native.gtk_init_check(0, 0) == 0) throw new InvalidOperationException("Не удалось открыть WebView.");
        using var contentFilter = new WebKitContentFilter(profilePath);
        app.contentFilter = contentFilter;
        var dataPath = Path.Combine(profilePath, "Data");
        var cachePath = Path.Combine(profilePath, "Cache");
        Storage.AppDataPaths.CreatePrivateDirectory(dataPath);
        Storage.AppDataPaths.CreatePrivateDirectory(cachePath);
        var manager = Native.webkit_website_data_manager_new("base-data-directory", dataPath, "base-cache-directory", cachePath, 0);
        var context = Native.webkit_web_context_new_with_website_data_manager(manager);
        var cookies = Native.webkit_website_data_manager_get_cookie_manager(manager);
        Native.webkit_cookie_manager_set_persistent_storage(cookies, Path.Combine(dataPath, "cookies.sqlite"), 1); // SQLite
        Native.g_object_unref(manager);
        var view = Native.webkit_web_view_new_with_context(context);
        Native.g_object_unref(context);
        app.rootWindow = app.AddWindow(view);
        app.rootView = view;
        if (app.browserEvent != null)
        {
            StartupLog.Event("browser.window-created");
            StartupLog.Event("browser.engine-ready");
            StartupLog.Event("browser.bridge-installed");
        }
        app.OnBrowserEvent(null);
        Native.webkit_web_view_load_uri(view, startUri);
        if (app.browserEvent != null) _ = Task.Run(app.ReadCommandsAsync);
        Native.gtk_main();
        foreach (var item in app.views.Values.ToArray()) Native.gtk_widget_destroy(item.Window);
        GC.KeepAlive(app);
        return app.done;
    }

    private async Task ReadCommandsAsync()
    {
        try
        {
            while (await Console.In.ReadLineAsync() is { } line)
            {
                if (line.Length > BrowserRequestCommand.MaxLength) continue;
                var command = JsonSerializer.Deserialize(line, SoundCloudJson.Default.BrowserRequestCommand);
                if (command?.IsValid() != true) continue;
                var evaluation = "if(location.origin === " + JsonSerializer.Serialize(origin, Pookie.SoundCloud.SoundCloudJson.Default.String) + ") window.__pookieRequest?.(" +
                    JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand) + ");";
                Dispatch(() => Native.webkit_web_view_run_javascript(rootView, evaluation, 0, 0, 0));
            }
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            Pookie.Logging.AppLog.For("Pookie.Browser").Warning("Команды браузерного процесса завершены: {ErrorType}", error.GetType().Name);
        }
        finally { Dispatch(Native.gtk_main_quit); }
    }

    private void Dispatch(Action action)
    {
        dispatch.Enqueue(action);
        Native.g_idle_add(idleCallback, 0);
    }

    private int DrainDispatch(nint data)
    {
        try { while (dispatch.TryDequeue(out var action)) action(); }
        catch { Native.gtk_main_quit(); }
        return 0;
    }

    private nint AddWindow(nint view)
    {
        // Keep the UA aligned with this WebKitGTK version and platform, including related login popups.
        Native.webkit_settings_set_user_agent(Native.webkit_web_view_get_settings(view), null);
        ConfigureView(view);
        var window = Native.gtk_window_new(0);
        Native.gtk_window_set_title(window, background ? "Pookie — SoundCloud" : "Pookie — вход на сайте SoundCloud");
        Native.gtk_window_set_default_size(window, 900, 760);
        var box = Native.gtk_box_new(1, 8);
        var address = Native.gtk_label_new("Подключение к SoundCloud…");
        Native.gtk_label_set_selectable(address, 1);
        Native.gtk_box_pack_start(box, address, 0, 0, 8);
        Native.gtk_box_pack_start(box, view, 1, 1, 0);
        Native.gtk_container_add(window, box);
        views[view] = (window, address);
        var manager = Native.webkit_web_view_get_user_content_manager(view);
        if (managers.Add(manager))
        {
            contentFilter?.Attach(manager);
            Connect(manager, "script-message-received::pookie", messageCallback);
            Native.webkit_user_content_manager_register_script_message_handler(manager, "pookie");
            var userScript = Native.webkit_user_script_new(script, 1, 0, 0, 0); // Top frame, document start.
            Native.webkit_user_content_manager_add_script(manager, userScript);
            Native.webkit_user_script_unref(userScript);
        }
        Connect(window, "delete-event", deleteCallback);
        Connect(view, "notify::uri", uriCallback);
        Connect(view, "create", createCallback);
        Connect(view, "close", closeCallback);
        // Allocate the browser without ever mapping its top-level window. Relying on
        // iconify lets window managers show it (and may flash it at startup).
        if (background)
        {
            Native.gtk_window_set_skip_taskbar_hint(window, 1);
            Native.gtk_window_set_skip_pager_hint(window, 1);
            Native.gtk_widget_show_all(box);
            Native.gtk_widget_realize(window);
            Native.gtk_widget_realize(view);
            var allocation = new Native.Allocation { Width = 900, Height = 720 };
            Native.gtk_widget_size_allocate(view, ref allocation);
        }
        else if (!hiddenLogin) Native.gtk_widget_show_all(window);
        return window;
    }

    private void MessageReceived(nint manager, nint result, nint data)
    {
        if (done) return;
        try
        {
            var value = Native.webkit_javascript_result_get_js_value(result);
            if (Native.jsc_value_is_string(value) == 0) return;
            var raw = TakeString(Native.jsc_value_to_string(value));
            // WebKitGTK's copied JSC message value has no source realm. Combine a nonce
            // injected only into the permitted top frame with the native navigation URI.
            var source = views.Keys.Select(view => Marshal.PtrToStringUTF8(Native.webkit_web_view_get_uri(view)))
                .FirstOrDefault(uri => Uri.TryCreate(uri, UriKind.Absolute, out var address) && address.GetLeftPart(UriPartial.Authority) == origin);
            if (browserEvent != null)
            {
                var message = BrowserRequestProtocol.Parse(raw, source, origin, pairing);
                if (message == null) return;
                if (message.Kind is "checking" or "blocked" && message.Interactive)
                {
                    Native.gtk_window_set_title(rootWindow, message.Kind == "blocked" ? "Pookie — доступ к SoundCloud ограничен" : "Pookie — проверка SoundCloud");
                    Native.gtk_window_set_skip_taskbar_hint(rootWindow, 0);
                    Native.gtk_window_set_skip_pager_hint(rootWindow, 0);
                    Native.gtk_widget_show_all(rootWindow);
                    Native.gtk_window_deiconify(rootWindow);
                    Native.gtk_window_present(rootWindow);
                }
                if (message.Kind is "passed" or "challenge-error")
                {
                    Native.gtk_widget_hide(rootWindow);
                    Native.gtk_window_set_skip_taskbar_hint(rootWindow, 1);
                    Native.gtk_window_set_skip_pager_hint(rootWindow, 1);
                }
                OnBrowserEvent(message);
                browserEvent(message);
                return;
            }
            var session = LoginSessionMessage.Parse(raw, source, origin, pairing);
            if (session == null) return;
            connected(session);
            done = true;
            Native.gtk_main_quit();
        }
        catch { Native.gtk_main_quit(); }
    }

    private void UriChanged(nint view, nint spec, nint data)
    {
        if (!views.TryGetValue(view, out var item)) return;
        var uri = Marshal.PtrToStringUTF8(Native.webkit_web_view_get_uri(view));
        var text = Uri.TryCreate(uri, UriKind.Absolute, out var address) && address.Scheme == "https"
            ? $"https://{address.Host} — {(background ? "проверка выполняется на этом сайте" : "вход выполняется на этом сайте")}"
            : "Pookie — загрузка страницы входа";
        Native.gtk_label_set_text(item.Address, text);
    }

    partial void OnBrowserEvent(BrowserRequestEvent? message);
    partial void CustomizeScript(ref string scriptBody, bool login);
    partial void ConfigureView(nint view);

    private nint CreatePopup(nint view, nint action, nint data)
    {
        var popup = Native.webkit_web_view_new_with_related_view(view);
        AddWindow(popup);
        return popup;
    }

    private void ClosePopup(nint view, nint data)
    {
        if (!views.Remove(view, out var item)) return;
        Native.gtk_widget_destroy(item.Window);
        if (item.Window == rootWindow) Native.gtk_main_quit();
    }

    private int WindowDeleted(nint window, nint evt, nint data)
    {
        foreach (var view in views.Where(x => x.Value.Window == window).Select(x => x.Key).ToArray()) views.Remove(view);
        if (window == rootWindow) Native.gtk_main_quit();
        return 0;
    }

    private static string TakeString(nint value)
    {
        try { return Marshal.PtrToStringUTF8(value) ?? ""; }
        finally { Native.g_free(value); }
    }
    private static void Connect<T>(nint instance, string signal, T callback) where T : Delegate =>
        Native.g_signal_connect_data(instance, signal, Marshal.GetFunctionPointerForDelegate(callback), 0, 0, 0);

    private static partial class Native
    {
        private const string Gtk = "libgtk-3.so.0", WebKit = "libwebkit2gtk-4.1.so.0", GObject = "libgobject-2.0.so.0", Jsc = "libjavascriptcoregtk-4.1.so.0";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void MessageCallback(nint manager, nint result, nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int DeleteCallback(nint window, nint evt, nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void NotifyCallback(nint view, nint spec, nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate nint CreateCallback(nint view, nint action, nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void CloseCallback(nint view, nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int IdleCallback(nint data);
        [LibraryImport(Gtk)] public static partial int gtk_init_check(nint argc, nint argv);
        [LibraryImport(Gtk)] public static partial nint gtk_window_new(int type);
        [LibraryImport(Gtk)] public static partial void gtk_window_set_default_size(nint window, int width, int height);
        [LibraryImport(Gtk)] public static partial void gtk_window_set_title(nint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
        [LibraryImport(Gtk)] public static partial void gtk_window_deiconify(nint window);
        [LibraryImport(Gtk)] public static partial void gtk_window_present(nint window);
        [LibraryImport(Gtk)] public static partial void gtk_window_set_skip_taskbar_hint(nint window, int skip);
        [LibraryImport(Gtk)] public static partial void gtk_window_set_skip_pager_hint(nint window, int skip);
        [LibraryImport(Gtk)] public static partial nint gtk_box_new(int orientation, int spacing);
        [LibraryImport(Gtk)] public static partial nint gtk_label_new([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
        [LibraryImport(Gtk)] public static partial void gtk_label_set_text(nint label, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
        [LibraryImport(Gtk)] public static partial void gtk_label_set_selectable(nint label, int selectable);
        [LibraryImport(Gtk)] public static partial void gtk_box_pack_start(nint box, nint child, int expand, int fill, uint padding);
        [LibraryImport(Gtk)] public static partial void gtk_container_add(nint container, nint child);
        [LibraryImport(Gtk)] public static partial void gtk_widget_show_all(nint widget);
        [LibraryImport(Gtk)] public static partial void gtk_widget_hide(nint widget);
        [LibraryImport(Gtk)] public static partial void gtk_widget_realize(nint widget);
        [StructLayout(LayoutKind.Sequential)] public struct Allocation { public int X, Y, Width, Height; }
        [LibraryImport(Gtk)] public static partial void gtk_widget_size_allocate(nint widget, ref Allocation allocation);
        [LibraryImport(Gtk)] public static partial void gtk_widget_destroy(nint widget);
        [LibraryImport(Gtk)] public static partial void gtk_main();
        [LibraryImport(Gtk)] public static partial void gtk_main_quit();
        [LibraryImport(GObject)] public static partial void g_object_unref(nint obj);
        [LibraryImport(GObject)] public static partial ulong g_signal_connect_data(nint instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, nint callback, nint data, nint destroy, int flags);
        [LibraryImport("libglib-2.0.so.0")] public static partial void g_free(nint value);
        [LibraryImport("libglib-2.0.so.0")] public static partial uint g_idle_add(IdleCallback callback, nint data);
        [LibraryImport(WebKit)] public static partial nint webkit_website_data_manager_new(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string firstProperty, [MarshalAs(UnmanagedType.LPUTF8Str)] string dataPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string secondProperty, [MarshalAs(UnmanagedType.LPUTF8Str)] string cachePath, nint end);
        [LibraryImport(WebKit)] public static partial nint webkit_web_context_new_with_website_data_manager(nint manager);
        [LibraryImport(WebKit)] public static partial nint webkit_website_data_manager_get_cookie_manager(nint manager);
        [LibraryImport(WebKit)] public static partial void webkit_cookie_manager_set_persistent_storage(nint manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int storage);
        [LibraryImport(WebKit)] public static partial nint webkit_web_view_new_with_context(nint context);
        [LibraryImport(WebKit)] public static partial nint webkit_web_view_new_with_related_view(nint view);
        [LibraryImport(WebKit)] public static partial nint webkit_web_view_get_settings(nint view);
        [LibraryImport(WebKit)] public static partial void webkit_settings_set_user_agent(nint settings, [MarshalAs(UnmanagedType.LPUTF8Str)] string? userAgent);
        [LibraryImport(WebKit)] public static partial nint webkit_web_view_get_user_content_manager(nint view);
        [LibraryImport(WebKit)] public static partial nint webkit_web_view_get_uri(nint view);
        [LibraryImport(WebKit)] public static partial void webkit_web_view_load_uri(nint view, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri);
        [LibraryImport(WebKit)] public static partial void webkit_web_view_run_javascript(nint view, [MarshalAs(UnmanagedType.LPUTF8Str)] string script, nint cancellable, nint callback, nint data);
        [LibraryImport(WebKit)] public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [LibraryImport(WebKit)] public static partial nint webkit_user_script_new([MarshalAs(UnmanagedType.LPUTF8Str)] string source, int frames, int injectionTime, nint allowList, nint denyList);
        [LibraryImport(WebKit)] public static partial void webkit_user_content_manager_add_script(nint manager, nint script);
        [LibraryImport(WebKit)] public static partial void webkit_user_script_unref(nint script);
        [LibraryImport(WebKit)] public static partial nint webkit_javascript_result_get_js_value(nint result);
        [LibraryImport(Jsc)] public static partial int jsc_value_is_string(nint value);
        [LibraryImport(Jsc)] public static partial nint jsc_value_to_string(nint value);
    }
}
