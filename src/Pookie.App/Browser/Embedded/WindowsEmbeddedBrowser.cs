#if WINDOWS
using Aprillz.MewUI;
using Microsoft.Web.WebView2.Core;
#if POOKIE_NATIVE_AOT
using BrowserBounds = Windows.Foundation.Rect;
#else
using BrowserBounds = System.Drawing.Rectangle;
#endif

namespace Pookie.App.Browser;

internal sealed class WindowsEmbeddedBrowser(EmbeddedBrowserEvents events) : IEmbeddedBrowser
{
    private readonly List<CoreWebView2Controller> controllers = [];
    private CoreWebView2Environment? environment;
    private CoreWebView2Controller? active;
    private nint parent;
    private BrowserBounds bounds;
    private bool visible;
    private bool disposed;
    private string script = "";
    private System.Diagnostics.Process? browserProcess;
    internal bool HasBeenShown { get; private set; }

    public async Task InitializeAsync(nint parent, string profile, string script, bool isolated)
    {
        this.parent = parent; this.script = script;
#if POOKIE_NATIVE_AOT
        environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile,
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=HardwareMediaKeyHandling,MediaSessionService" });
#else
        environment = await CoreWebView2Environment.CreateAsync(null, profile,
            new CoreWebView2EnvironmentOptions("--disable-features=HardwareMediaKeyHandling,MediaSessionService"));
#endif
        if (!disposed) await CreateControllerAsync();
    }

    private async Task<CoreWebView2Controller?> CreateControllerAsync()
    {
#if POOKIE_NATIVE_AOT
        var controller = await environment!.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)parent));
#else
        var controller = await environment!.CreateCoreWebView2ControllerAsync(parent);
#endif
        if (disposed) { controller.Close(); return null; }
        controller.IsVisible = false;
        var web = controller.CoreWebView2;
        browserProcess ??= System.Diagnostics.Process.GetProcessById((int)web.BrowserProcessId);
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        web.WebResourceRequested += (_, e) =>
        {
            if (!disposed && BrowserContentBlocker.ShouldBlock(e.Request.Uri))
                e.Response = environment!.CreateWebResourceResponse(new MemoryStream("[]"u8.ToArray(), writable: false)
#if POOKIE_NATIVE_AOT
                    .AsRandomAccessStream()
#endif
                    ,
                    200, "OK", "Content-Type: application/json");
        };
        web.NavigationStarting += (_, e) => e.Cancel = !events.Navigate(e.Uri, true);
        web.FrameNavigationStarting += (_, e) => e.Cancel = !events.Navigate(e.Uri, false);
        web.SourceChanged += (_, _) => events.SourceChanged(web.Source);
        web.NavigationCompleted += (_, e) => { if (!disposed && e.IsSuccess && web.Source.StartsWith("http", StringComparison.Ordinal)) events.Ready(); };
        web.WebMessageReceived += (_, e) => ReceiveMessage(e);
        web.FrameCreated += (_, e) => AttachFrame(e.Frame);
        web.NewWindowRequested += async (_, e) =>
        {
            var pending = e.GetDeferral();
            e.Handled = true;
            try
            {
                if (!events.Navigate(e.Uri, true)) return;
                var popup = await CreateControllerAsync();
                if (popup != null) e.NewWindow = popup.CoreWebView2;
            }
            catch (Exception error) { if (!disposed) events.Failed(error); }
            finally { pending.Complete(); }
        };
        web.WindowCloseRequested += (_, _) => ClosePopup(controller);
        web.ProcessFailed += (_, _) => events.Failed(new InvalidOperationException("Браузер входа завершился. Повтори вход."));
        await web.AddScriptToExecuteOnDocumentCreatedAsync(script);
        if (disposed) { controller.Close(); return null; }
        if (active != null) active.IsVisible = false;
        controllers.Add(controller); active = controller;
#if POOKIE_NATIVE_AOT
        controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 17, 17, 19);
#else
        controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(17, 17, 19);
#endif
        controller.Bounds = bounds; controller.IsVisible = visible;
        return controller;
    }

    private void ClosePopup(CoreWebView2Controller controller)
    {
        if (disposed || controllers.Count < 2 || controller != active) return;
        controllers.Remove(controller); controller.Close();
        active = controllers[^1]; active.Bounds = bounds; active.IsVisible = visible;
        events.SourceChanged(active.CoreWebView2.Source);
    }

    private void AttachFrame(CoreWebView2Frame frame)
    {
        frame.WebMessageReceived += (_, e) => ReceiveMessage(e);
        frame.FrameCreated += (_, e) => AttachFrame(e.Frame);
    }

    private void ReceiveMessage(CoreWebView2WebMessageReceivedEventArgs message)
    {
        if (disposed) return;
        try { events.Message(message.TryGetWebMessageAsString(), message.Source); }
        catch (ArgumentException) { /* The paired protocol accepts string messages only. */ }
    }

    public void Navigate(string url) => active?.CoreWebView2.Navigate(url);
    public async Task ExecuteAsync(string script) => await controllers[0].CoreWebView2.ExecuteScriptAsync(script);

    public void SetBounds(Rect area, double scale)
    {
        bounds = new((int)Math.Round(area.X * scale), (int)Math.Round(area.Y * scale),
            Math.Max(1, (int)Math.Round(area.Width * scale)), Math.Max(1, (int)Math.Round(area.Height * scale)));
        if (active != null) { active.Bounds = bounds; active.NotifyParentWindowPositionChanged(); }
    }
    public void SetVisible(bool value)
    {
        visible = value;
        HasBeenShown |= value;
        if (active != null) active.IsVisible = value;
    }
    public async Task<byte[]> CaptureAsync()
    {
        using var buffer = new MemoryStream();
#if POOKIE_NATIVE_AOT
        using var random = buffer.AsRandomAccessStream();
        await active!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, random);
#else
        await active!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, buffer);
#endif
        return buffer.ToArray();
    }
    public void Dispose()
    {
        disposed = true;
        foreach (var controller in controllers) controller.Close();
        controllers.Clear(); active = null;
        environment = null;
    }

    public async Task ShutdownAsync()
    {
        Dispose();
        if (browserProcess == null) return;
        try { await browserProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
        finally { browserProcess.Dispose(); browserProcess = null; }
    }
}
#endif
