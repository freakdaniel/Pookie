using System.Runtime.InteropServices;
using Aprillz.MewUI;

namespace Pookie.App.Browser;

internal sealed partial class WebKitLoginWindow
{
    // MewUI's Linux backend is X11 (including XWayland). A GtkPlug embeds a
    // WebKit widget into that window. GLib is pumped by the existing UI dispatcher.
    internal sealed class EmbeddedBrowser : IEmbeddedBrowser
    {
        private readonly EmbeddedBrowserEvents events;
        private readonly DispatcherTimer pump = new(TimeSpan.FromMilliseconds(10));
        private readonly Native.MessageCallback message;
        private readonly Native.NotifyCallback source;
        private readonly Native.PolicyCallback policy;
        private readonly Native.CreateCallback create;
        private readonly Native.CloseCallback close;
        private readonly Native.DeleteCallback focus, permission;
        private readonly Native.LoadCallback loaded;
        private readonly List<nint> pages = [];
        private readonly HashSet<nint> managers = [];
        private nint plug, container, xid, display;
        private string script = "";
        private WebKitContentFilter? filter;
        private int width = 1, height = 1;
        private bool disposed;

        internal EmbeddedBrowser(EmbeddedBrowserEvents events)
        {
            this.events = events;
            message = Message; source = Source; policy = Policy; create = CreatePopup; close = ClosePopup;
            focus = (view, _, _) =>
            {
                if (!disposed && xid != 0) { Native.XSetInputFocus(display, xid, 1, 0); Native.gtk_widget_grab_focus(view); }
                return 0;
            };
            permission = (_, request, _) => { Native.webkit_permission_request_deny(request); return 1; };
            loaded = (_, stage, _) => { if (!disposed && stage == 3) events.Ready(); };
            pump.Tick += Pump;
        }

        public Task InitializeAsync(nint parent, string profile, string script, bool isolated)
        {
            this.script = script;
            Native.gdk_set_allowed_backends("x11");
            if (Native.gtk_init_check(0, 0) == 0) throw new InvalidOperationException("Не удалось запустить встроенный WebKitGTK. Нужен libwebkit2gtk-4.1-0 и X11/XWayland.");
            filter = new WebKitContentFilter(profile);
            var data = Path.Combine(profile, "Data"); var cache = Path.Combine(profile, "Cache");
            Storage.AppDataPaths.CreatePrivateDirectory(data); Storage.AppDataPaths.CreatePrivateDirectory(cache);
            var manager = Native.webkit_website_data_manager_new("base-data-directory", data, "base-cache-directory", cache, 0);
            var cookies = Native.webkit_website_data_manager_get_cookie_manager(manager);
            Native.webkit_cookie_manager_set_persistent_storage(cookies, Path.Combine(data, "cookies.sqlite"), 1);
            var context = Native.webkit_web_context_new_with_website_data_manager(manager);
            Native.g_object_unref(manager);
            var view = Native.webkit_web_view_new_with_context(context); Native.g_object_unref(context);
            plug = Native.gtk_plug_new(parent);
            container = Native.gtk_fixed_new(); Native.gtk_container_add(plug, container);
            AddPage(view);
            Native.gtk_widget_realize(plug);
            var gdk = Native.gtk_widget_get_window(plug);
            xid = Native.gdk_x11_window_get_xid(gdk);
            display = Native.gdk_x11_display_get_xdisplay(Native.gdk_window_get_display(gdk));
            pump.Start();
            return Task.CompletedTask;
        }

        private void AddPage(nint view)
        {
            if (pages.Count > 0) Native.gtk_widget_hide(pages[^1]);
            Native.webkit_settings_set_user_agent(Native.webkit_web_view_get_settings(view), null);
            Native.gtk_fixed_put(container, view, 0, 0);
            Native.gtk_widget_set_size_request(view, width, height);
            pages.Add(view);
            var manager = Native.webkit_web_view_get_user_content_manager(view);
            if (managers.Add(manager))
            {
                filter?.Attach(manager);
                Native.webkit_user_content_manager_register_script_message_handler(manager, "pookie");
                Connect(manager, "script-message-received::pookie", message);
                var userScript = Native.webkit_user_script_new(script, 1, 1, 0, 0);
                Native.webkit_user_content_manager_add_script(manager, userScript); Native.webkit_user_script_unref(userScript);
            }
            Connect(view, "decide-policy", policy); Connect(view, "notify::uri", source);
            Connect(view, "create", create); Connect(view, "close", close);
            Connect(view, "button-press-event", focus); Connect(view, "permission-request", permission);
            Connect(view, "load-changed", loaded);
            Native.gtk_widget_show_all(view);
        }

        private void Message(nint manager, nint result, nint data)
        {
            try
            {
                var value = Native.webkit_javascript_result_get_js_value(result);
                if (!disposed && Native.jsc_value_is_string(value) != 0 && pages.Count > 0)
                    events.Message(TakeString(Native.jsc_value_to_string(value)), Url(pages[^1]));
            }
            catch (Exception error) { events.Failed(error); }
        }

        private static string Url(nint view) => Marshal.PtrToStringUTF8(Native.webkit_web_view_get_uri(view)) ?? "";
        private void Source(nint view, nint spec, nint data) { if (!disposed && pages.Count > 0 && pages[^1] == view) events.SourceChanged(Url(view)); }
        private int Policy(nint view, nint decision, int kind, nint data)
        {
            if (kind is not (0 or 1)) return 0;
            try
            {
                var action = Native.webkit_navigation_policy_decision_get_navigation_action(decision);
                var request = Native.webkit_navigation_action_get_request(action);
                var url = Marshal.PtrToStringUTF8(Native.webkit_uri_request_get_uri(request)) ?? "";
                // Named subframes do not own the login callback. WebKitGTK 4.1's
                // policy API exposes target frame names rather than WKFrameInfo.
                var frame = Native.webkit_navigation_policy_decision_get_frame_name(decision);
                var main = frame == 0 || string.IsNullOrEmpty(Marshal.PtrToStringUTF8(frame));
                if (!disposed && events.Navigate(url, main)) return 0;
            }
            catch (Exception error) { events.Failed(error); }
            Native.webkit_policy_decision_ignore(decision); return 1;
        }

        private nint CreatePopup(nint view, nint action, nint data)
        {
            try { var popup = Native.webkit_web_view_new_with_related_view(view); AddPage(popup); return popup; }
            catch (Exception error) { events.Failed(error); return 0; }
        }
        private void ClosePopup(nint view, nint data)
        {
            if (disposed || pages.Count < 2 || pages[^1] != view) return;
            pages.RemoveAt(pages.Count - 1); Native.gtk_widget_destroy(view);
            Native.gtk_widget_show_all(pages[^1]); events.SourceChanged(Url(pages[^1]));
        }
        private void Pump()
        {
            if (disposed) return;
            for (var count = 0; count < 32 && Native.g_main_context_pending(0) != 0; count++) Native.g_main_context_iteration(0, 0);
        }
        public void Navigate(string url) => Native.webkit_web_view_load_uri(pages[^1], url);
        public Task ExecuteAsync(string script)
        {
            Native.webkit_web_view_run_javascript(pages[0], script, 0, 0, 0);
            return Task.CompletedTask;
        }
        public void SetBounds(Rect bounds, double scale)
        {
            if (plug == 0 || disposed) return;
            var gtkScale = Math.Max(1, Native.gtk_widget_get_scale_factor(plug));
            width = Math.Max(1, (int)Math.Round(bounds.Width * scale / gtkScale));
            height = Math.Max(1, (int)Math.Round(bounds.Height * scale / gtkScale));
            Native.gtk_window_resize(plug, width, height);
            foreach (var page in pages) Native.gtk_widget_set_size_request(page, width, height);
            Native.XMoveResizeWindow(display, xid, (int)Math.Round(bounds.X * scale), (int)Math.Round(bounds.Y * scale),
                (uint)Math.Max(1, Math.Round(bounds.Width * scale)), (uint)Math.Max(1, Math.Round(bounds.Height * scale)));
            Native.XFlush(display);
        }
        public void SetVisible(bool visible)
        {
            if (plug == 0 || disposed) return;
            if (visible) { Native.gtk_widget_show_all(plug); foreach (var page in pages.SkipLast(1)) Native.gtk_widget_hide(page); }
            else Native.gtk_widget_hide(plug);
        }
    



        public Task<byte[]> CaptureAsync()
        {
            var result = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = GCHandle.Alloc(result);
            Native.webkit_web_view_get_snapshot(pages[^1], 0, 0, 0, SnapshotReady, GCHandle.ToIntPtr(handle));
            return result.Task;
        }

        private static readonly Native.SnapshotCallback SnapshotReady = (view, operation, data) =>
        {
            var handle = GCHandle.FromIntPtr(data);
            var result = (TaskCompletionSource<byte[]>)handle.Target!;
            nint surface = 0;
            try
            {
                surface = Native.webkit_web_view_get_snapshot_finish(view, operation, out var error);
                if (error != 0) { Native.g_error_free(error); throw new InvalidOperationException("WebKitGTK не смог подготовить кадр анимации входа."); }
                if (surface == 0) throw new InvalidOperationException("WebKitGTK вернул пустой кадр входа.");
                using var buffer = new MemoryStream();
                Native.PngWriteCallback write = (_, bytes, length) =>
                {
                    try { var chunk = new byte[length]; Marshal.Copy(bytes, chunk, 0, chunk.Length); buffer.Write(chunk); return 0; }
                    catch (Exception failure) { result.TrySetException(failure); return 11; } // CAIRO_STATUS_WRITE_ERROR
                };
                var status = Native.cairo_surface_write_to_png_stream(surface, write, 0);
                GC.KeepAlive(write);
                if (status != 0) throw new InvalidOperationException("Не удалось сохранить кадр WebKitGTK в памяти.");
                result.TrySetResult(buffer.ToArray());
            }
            catch (Exception error) { result.TrySetException(error); }
            finally { if (surface != 0) Native.cairo_surface_destroy(surface); handle.Free(); }
        };
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; pump.Dispose();
            if (plug != 0) Native.gtk_widget_destroy(plug);
            plug = 0; pages.Clear(); managers.Clear(); filter?.Dispose(); filter = null;
        }
    }

    private static partial class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int PolicyCallback(nint view, nint decision, int kind, nint data);
        [DllImport(WebKit)] public static extern nint webkit_navigation_policy_decision_get_navigation_action(nint decision);
        [DllImport(WebKit)] public static extern nint webkit_navigation_action_get_request(nint action);
        [DllImport(WebKit)] public static extern nint webkit_uri_request_get_uri(nint request);
        [DllImport(WebKit)] public static extern void webkit_policy_decision_ignore(nint decision);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void LoadCallback(nint view, int stage, nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SnapshotCallback(nint view, nint operation, nint data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int PngWriteCallback(nint data, nint bytes, uint length);
        [DllImport(WebKit)] public static extern void webkit_web_view_get_snapshot(nint view, int region, int options, nint cancel, SnapshotCallback callback, nint data);
        [DllImport(WebKit)] public static extern nint webkit_web_view_get_snapshot_finish(nint view, nint operation, out nint error);
        [DllImport("libglib-2.0.so.0")] public static extern void g_error_free(nint error);
        [DllImport("libcairo.so.2")] public static extern int cairo_surface_write_to_png_stream(nint surface, PngWriteCallback callback, nint data);
        [DllImport("libcairo.so.2")] public static extern void cairo_surface_destroy(nint surface);
        [DllImport("libgdk-3.so.0")] public static extern void gdk_set_allowed_backends([MarshalAs(UnmanagedType.LPUTF8Str)] string backends);
        [DllImport(Gtk)] public static extern nint gtk_plug_new(nint socket);
        [DllImport(Gtk)] public static extern nint gtk_fixed_new();
        [DllImport(Gtk)] public static extern void gtk_fixed_put(nint container, nint child, int x, int y);
        [DllImport(Gtk)] public static extern void gtk_widget_set_size_request(nint widget, int width, int height);
        [DllImport(Gtk)] public static extern void gtk_widget_grab_focus(nint widget);
        [DllImport(Gtk)] public static extern int gtk_widget_get_scale_factor(nint widget);
        [DllImport(Gtk)] public static extern nint gtk_widget_get_window(nint widget);
        [DllImport(Gtk)] public static extern void gtk_window_resize(nint window, int width, int height);
        [DllImport("libgdk-3.so.0")] public static extern nint gdk_window_get_display(nint window);
        [DllImport("libgdk-3.so.0")] public static extern nint gdk_x11_display_get_xdisplay(nint display);
        [DllImport("libgdk-3.so.0")] public static extern nint gdk_x11_window_get_xid(nint window);
        [DllImport("libglib-2.0.so.0")] public static extern int g_main_context_pending(nint context);
        [DllImport("libglib-2.0.so.0")] public static extern int g_main_context_iteration(nint context, int mayBlock);
        [DllImport("libX11.so.6")] public static extern int XMoveResizeWindow(nint display, nint window, int x, int y, uint width, uint height);
        [DllImport("libX11.so.6")] public static extern int XFlush(nint display);
        [DllImport("libX11.so.6")] public static extern int XSetInputFocus(nint display, nint window, int revert, nint time);
        [DllImport(WebKit)] public static extern void webkit_permission_request_deny(nint request);
        [DllImport(WebKit)] public static extern nint webkit_navigation_policy_decision_get_frame_name(nint decision);
    }
}
