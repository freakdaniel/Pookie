using Pookie.App.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Pookie.App.Storage;

namespace Pookie.App.Browser;

// WebKit compiles and caches native request rules before any SoundCloud document loads.
internal sealed class WebKitContentFilter : IDisposable
{
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0";
    private readonly nint filter;

    public WebKitContentFilter(string profilePath)
    {
        var rules = BrowserContentBlocker.CreateWebKitRules();
        var path = Path.Combine(profilePath, "ContentFilters");
        AppDataPaths.CreatePrivateDirectory(path);
        var store = webkit_user_content_filter_store_new(path);
        if (store == 0) throw new InvalidOperationException("Не удалось открыть кеш фильтра браузера.");
        try
        {
            var identifier = "pookie-network-" + Convert.ToHexStringLower(SHA256.HashData(rules));
            filter = Complete(store, identifier, null);
            if (filter == 0) filter = Complete(store, identifier, rules);
            if (filter == 0) throw new InvalidOperationException("Не удалось скомпилировать фильтр браузера.");
            StartupLog.Event("browser.content-filter-ready");
        }
        finally { g_object_unref(store); }
    }

    public void Attach(nint manager) => webkit_user_content_manager_add_filter(manager, filter);
    public void Dispose() => webkit_user_content_filter_unref(filter);

    private static nint Complete(nint store, string identifier, byte[]? rules)
    {
        var completed = false;
        var expired = false;
        nint result = 0;
        var cancellable = g_cancellable_new();
        AsyncReady ready = (source, operation, _) =>
        {
            result = rules == null
                ? webkit_user_content_filter_store_load_finish(source, operation, out var loadError)
                : webkit_user_content_filter_store_save_finish(source, operation, out loadError);
            if (loadError != 0) g_error_free(loadError);
            completed = true;
        };
        TimeoutCallback timeout = _ => { expired = true; g_cancellable_cancel(cancellable); return 0; };
        var timer = g_timeout_add(10000, timeout, 0);
        try
        {
            if (rules == null) webkit_user_content_filter_store_load(store, identifier, cancellable, ready, 0);
            else
            {
                var bytes = g_bytes_new(rules, (nuint)rules.Length);
                try { webkit_user_content_filter_store_save(store, identifier, bytes, cancellable, ready, 0); }
                finally { g_bytes_unref(bytes); }
            }
            // This is the isolated GTK worker, before it owns any windows. Dispatch the
            // asynchronous compiler's completion and cancellation through its native loop.
            while (!completed) g_main_context_iteration(0, 1);
            if (expired)
            {
                if (result != 0) webkit_user_content_filter_unref(result);
                throw new TimeoutException("Фильтр браузера не завершил подготовку вовремя.");
            }
            return result;
        }
        finally
        {
            if (!expired) g_source_remove(timer);
            g_object_unref(cancellable);
            GC.KeepAlive(ready);
            GC.KeepAlive(timeout);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AsyncReady(nint source, nint result, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TimeoutCallback(nint data);
    [DllImport(WebKit)] private static extern nint webkit_user_content_filter_store_new([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(WebKit)] private static extern void webkit_user_content_filter_store_load(nint store, [MarshalAs(UnmanagedType.LPUTF8Str)] string identifier, nint cancellable, AsyncReady callback, nint data);
    [DllImport(WebKit)] private static extern nint webkit_user_content_filter_store_load_finish(nint store, nint result, out nint error);
    [DllImport(WebKit)] private static extern void webkit_user_content_filter_store_save(nint store, [MarshalAs(UnmanagedType.LPUTF8Str)] string identifier, nint source, nint cancellable, AsyncReady callback, nint data);
    [DllImport(WebKit)] private static extern nint webkit_user_content_filter_store_save_finish(nint store, nint result, out nint error);
    [DllImport(WebKit)] private static extern void webkit_user_content_manager_add_filter(nint manager, nint filter);
    [DllImport(WebKit)] private static extern void webkit_user_content_filter_unref(nint filter);
    [DllImport(GLib)] private static extern nint g_bytes_new(byte[] data, nuint size);
    [DllImport(GLib)] private static extern void g_bytes_unref(nint bytes);
    [DllImport(GLib)] private static extern void g_error_free(nint error);
    [DllImport(GLib)] private static extern int g_main_context_iteration(nint context, int mayBlock);
    [DllImport(GLib)] private static extern uint g_timeout_add(uint interval, TimeoutCallback callback, nint data);
    [DllImport(GLib)] private static extern int g_source_remove(uint tag);
    [DllImport(GObject)] private static extern void g_object_unref(nint obj);
    [DllImport(Gio)] private static extern nint g_cancellable_new();
    [DllImport(Gio)] private static extern void g_cancellable_cancel(nint cancellable);
}
