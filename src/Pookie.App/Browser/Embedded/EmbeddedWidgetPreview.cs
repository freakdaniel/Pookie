using System.Text;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;
using Pookie.SoundCloud;
using StbImageSharp;

namespace Pookie.App.Browser;

// Native browser children cannot participate in the card's perspective transform.
// Keep a painted frame underneath, and expose the live player only at rest.
internal sealed class EmbeddedWidgetPreview : ContentControl
{
    private readonly Func<bool> canPresent;
    private readonly TextBlock message = new TextBlock().Text("Загружаем плеер…").FontSize(13).Foreground(TrackButtons.OnSurface).CenterHorizontal();
    private readonly LoadingSpinner spinner = new LoadingSpinner().Width(22).Height(22).Foreground(Color.White).CenterHorizontal();
    // The bundled mark contains large transparent gutters. Crop those rather
    // than centering its PNG canvas and leaving a false gap above the spinner.
    private readonly Image logo = new Image { Source = Icons.SoundCloudMark(76).Source, ViewBox = new Rect(24, 49, 102, 46) }
        .Width(76).Height(36).CenterHorizontal();
    private readonly StackPanel placeholder;
    private readonly DispatcherTimer reload = new(TimeSpan.FromMilliseconds(250));
    private readonly DispatcherTimer deadline = new(TimeSpan.FromSeconds(20));
    private IEmbeddedBrowser? browser;
    private BrowserProfile? profile;
    private Window? window;
    private IImage? snapshot;
    private byte[] snapshotPixels = [];
    private int snapshotWidth, snapshotHeight;
    private string? playerUrl, loadedUrl, documentUrl;
    private string nonce = "";
    private int logicalHeight = 166, revision;
    private bool requested, initializing, ready, shown, staged, failed, disposed;
    private bool popupFrozen;
    private bool tooltipFrozen;
    private bool refreshing;
    private int refreshVersion;
    private Size viewport;
    private Rect nativeBounds;
    private double nativeScale;
    private Task initialization = Task.CompletedTask, shutdown = Task.CompletedTask;
    internal Func<EmbeddedBrowserEvents, IEmbeddedBrowser> BrowserFactory { get; set; } = EmbeddedBrowserFactory.Create;
    internal Func<string, int, string, string> PageBuilder { get; set; } = BuildPage;
    internal Action? ViewportChanged { get; init; }
    internal bool NativeVisible => shown;
    internal bool Ready => ready;
    internal string? PlayerUrl => playerUrl;
    internal int LogicalHeight => logicalHeight;
    internal bool HasSnapshot => snapshot != null;
    internal bool PopupFrozen => popupFrozen;
    internal bool Loading => spinner.IsActive && placeholder.IsVisible;
    internal Rect LoadingLogoBounds => logo.Bounds;
    internal Rect LoadingSpinnerBounds => spinner.Bounds;
    internal bool Refreshing => refreshing;
    internal ReadOnlyMemory<byte> SnapshotPng { get; private set; }
    internal Task Shutdown => shutdown;
    internal const double PreviewRadius = 12;
    internal const string ViewportCss = "html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#18181c}#viewport{position:absolute;inset:0;overflow:hidden;border-radius:12px;background:#1e1e22}iframe{display:block;border:0;transform-origin:0 0}";

    internal EmbeddedWidgetPreview(Func<bool> canPresent)
    {
        this.canPresent = canPresent;
        Height = 166; CornerRadius = PreviewRadius; Background = Color.FromRgb(30, 30, 34); BorderThickness = 0; Padding = new Thickness(0);
        message.IsVisible = false; spinner.Width = spinner.Height = 28;
        Content = placeholder = new StackPanel().Vertical().Spacing(16).Center().Children(logo, spinner, message);
        reload.Tick += () => { reload.Stop(); if (requested && VisibleHere()) Reload(); };
        deadline.Tick += () => Fail(new TimeoutException("Плеер SoundCloud не загрузился вовремя."));
    }

    internal void Configure(string? url, int height)
    {
        if (playerUrl == url && logicalHeight == height) return;
        var colorOnly = ready && logicalHeight == height && SameContent(loadedUrl, url);
        playerUrl = url; logicalHeight = height;
        if (colorOnly)
        {
            // All color inputs use the same in-place widget API path, retaining
            // the displayed frame and avoiding the full-page loading state.
            reload.Stop(); if (requested) reload.Start(); return;
        }
        nonce = "";
        refreshing = false; refreshVersion++;
        failed = false;
        if (!popupFrozen && !tooltipFrozen) { snapshot?.Dispose(); snapshot = null; snapshotPixels = []; SnapshotPng = default; }
        InvalidateVisual();
        if (!requested) return;
        Hide(); ready = false; deadline.Stop();
        placeholder.IsVisible = !popupFrozen && !tooltipFrozen; logo.IsVisible = spinner.IsVisible = url != null; message.IsVisible = url == null;
        spinner.IsActive = url != null; message.Text = url == null ? "Проверь ссылку и цвет виджета." : "Загружаем плеер…";
        reload.Stop(); if (url != null) reload.Start();
    }

    internal void Activate()
    {
        if (!requested && !ready) { loadedUrl = null; failed = false; }
        requested = true;
        if (!ready && !failed && !popupFrozen && !tooltipFrozen)
        {
            placeholder.IsVisible = true;
            spinner.IsActive = spinner.IsVisible = logo.IsVisible = playerUrl != null;
            message.IsVisible = playerUrl == null;
        }
        InvalidateVisual();
    }
    internal void Deactivate() { requested = false; reload.Stop(); Hide(pause: false); if (browser is { } host) _ = PauseAsync(host); }
    internal Task FreezeForPopupAsync() => FreezeFrameAsync(() => popupFrozen = true);
    internal Task FreezeForTooltipAsync() => FreezeFrameAsync(() => tooltipFrozen = true);
    internal void ResumeFromTooltip() { tooltipFrozen = false; InvalidateVisual(); }
    private async Task FreezeFrameAsync(Action hold)
    {
        var host = browser; var document = nonce;
        if (shown && ready && !refreshing && host != null) await CaptureAsync(revision, host, delay: 0);
        await PresentSnapshotAsync();
        if (disposed || !requested || host != browser || document != nonce) return;
        hold(); Hide(pause: false); InvalidateVisual();
    }
    private async Task PresentSnapshotAsync()
    {
        if (snapshot != null && window is { } owner)
        {
            var painted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Painted() => painted.TrySetResult();
            // FrameRendered fires before the outer swap-chain present. Wait
            // for the actual window presentation before moving the native child.
            WindowPresentation.FramePresented(owner) += Painted;
            try { InvalidateVisual(); await painted.Task.WaitAsync(TimeSpan.FromMilliseconds(250)); }
            catch (TimeoutException) { }
            finally { WindowPresentation.FramePresented(owner) -= Painted; }
        }
    }
    internal void ResumeFromPopup() { popupFrozen = false; InvalidateVisual(); }

    private bool VisibleHere()
    {
        if (!requested || disposed || popupFrozen || tooltipFrozen || window == null || !canPresent() || ActualWidth <= 0 || ActualHeight <= 0 || window.WindowState == WindowState.Minimized) return false;
        for (Element? element = this; element != null; element = element.Parent)
            if (element is UIElement { IsVisible: false }) return false;
        return true;
    }

    private void Frame()
    {
        if (requested && window != null && viewport != window.ClientSize)
        { viewport = window.ClientSize; ViewportChanged?.Invoke(); }
        if (!VisibleHere()) { Hide(); return; }
        if (failed) return;
        if (playerUrl == null) { spinner.IsActive = spinner.IsVisible = logo.IsVisible = false; message.IsVisible = true; message.Text = "Проверь ссылку и цвет виджета."; return; }
        if (browser == null && !initializing) { initialization = InitializeAsync(); return; }
        if (initializing || browser == null) return;
        if (loadedUrl != playerUrl && !reload.IsEnabled && !refreshing) Reload();
        if (!ready || refreshing) return;
        var bounds = TranslateRect(new(0, 0, ActualWidth, ActualHeight), window!);
        if (bounds != nativeBounds || nativeScale != window!.DpiScale)
        { nativeBounds = bounds; nativeScale = window!.DpiScale; browser.SetBounds(bounds, nativeScale); }
        if (!shown) { var mapped = staged; shown = true; staged = false; if (!mapped) browser.SetVisible(true); }
    }

    private static bool SameContent(string? previous, string? next)
    {
        if (!Uri.TryCreate(previous, UriKind.Absolute, out var left) || !Uri.TryCreate(next, UriKind.Absolute, out var right)) return false;
        static string Options(Uri uri) => string.Join('&', uri.Query.TrimStart('?').Split('&')
            .Where(part => !part.StartsWith("color=", StringComparison.OrdinalIgnoreCase)));
        return left.GetLeftPart(UriPartial.Path) == right.GetLeftPart(UriPartial.Path) && Options(left) == Options(right);
    }
    private void Reload()
    {
        if (refreshing) return;
        if (ready && SameContent(loadedUrl, playerUrl)) _ = RefreshColorAsync(); else Load();
    }
    private async Task RefreshColorAsync()
    {
        if (browser is not { } host || !VisibleHere()) return;
        var version = ++refreshVersion; var document = nonce; refreshing = true;
        try
        {
            if (shown) await CaptureAsync(revision, host, delay: 0, allowRefresh: true);
            await PresentSnapshotAsync();
            if (disposed || version != refreshVersion || host != browser || document != nonce) return;
            if (!VisibleHere() || !SameContent(loadedUrl, playerUrl)) { refreshing = false; return; }
            Hide(pause: false); loadedUrl = playerUrl;
            deadline.Start();
            await host.ExecuteAsync("window.__pookieReload?.(" + JsonSerializer.Serialize(loadedUrl, SoundCloudJson.Default.String) + ")");
        }
        catch (Exception error) { if (version == refreshVersion && !disposed) { refreshing = false; Fail(error); } }
    }
    private async Task FinishRefreshAsync(string url)
    {
        var version = refreshVersion;
        if (browser is not { } host) return;
        await CaptureAsync(revision, host, allowRefresh: true);
        await PresentSnapshotAsync();
        if (disposed || version != refreshVersion || browser != host || loadedUrl != url) return;
        deadline.Stop(); refreshing = false; Frame(); InvalidateVisual();
    }

    private async Task InitializeAsync()
    {
        initializing = true;
        var version = revision;
        BrowserProfile? lease = null;
        IEmbeddedBrowser? host = null;
        try
        {
            lease = BrowserProfile.Open("widget-preview"); profile = lease;
            host = BrowserFactory(new(Navigate, Message, _ => { }, () => { }, Fail)); browser = host;
            await host.InitializeAsync(window!.Handle, lease.Path, "", isolated: true);
            if (version != revision || disposed) return;
            initializing = false; Load();
        }
        catch (Exception error)
        {
            if (version == revision && !disposed) { initializing = false; Fail(error); browser = null; profile = null; }
            host?.Dispose();
            try { if (host != null) await host.ShutdownAsync(); } finally { lease?.Dispose(); }
        }
    }

    private bool Navigate(string url, bool mainFrame)
    {
        if (disposed) return false;
        if (url == documentUrl || url == "about:blank") return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || target.Scheme != "https") return false;
        // GTK's policy callback can label the first named iframe navigation as
        // a main-frame request. It still belongs inside the preview.
        if (target.Host.Equals("w.soundcloud.com", StringComparison.OrdinalIgnoreCase) && target.AbsolutePath.StartsWith("/player", StringComparison.Ordinal)) return true;
        if (!mainFrame) return true;
        // The engine callback carries no user-gesture proof. Never turn an
        // iframe navigation or redirect into an unsolicited browser tab.
        return false;
    }

    private void Load()
    {
        if (browser == null || initializing || playerUrl == null || !VisibleHere()) return;
        reload.Stop(); deadline.Stop(); Hide(); ready = false;
        loadedUrl = playerUrl; nonce = Guid.NewGuid().ToString("N");
        spinner.IsActive = true; spinner.IsVisible = logo.IsVisible = true; message.IsVisible = false; placeholder.IsVisible = true;
        documentUrl = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(PageBuilder(playerUrl, logicalHeight, nonce)));
        // Stage offscreen until the actual SoundCloud widget reports READY.
        browser.SetBounds(new(-32000, -32000, ActualWidth, ActualHeight), window!.DpiScale);
        staged = true; browser.SetVisible(true); nativeBounds = default;
        deadline.Start(); browser.Navigate(documentUrl);
    }

    internal static string BuildPage(string playerUrl, int height, string nonce)
    {
        var url = JsonSerializer.Serialize(playerUrl, SoundCloudJson.Default.String);
        var id = JsonSerializer.Serialize(nonce, SoundCloudJson.Default.String);
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <style>{{ViewportCss}}</style></head><body>
            <div id="viewport"><iframe id="player" name="pookie-player" title="Предпросмотр SoundCloud" allow="autoplay" scrolling="no"></iframe></div>
            <script>
            (()=>{let frame=document.getElementById('player');
              const post=(kind,url)=>{const text=JSON.stringify({nonce:{{id}},kind,url});
                if(window.chrome?.webview)window.chrome.webview.postMessage(text);else window.webkit?.messageHandlers?.pookie?.postMessage(text);};
              function fit(){const scale=Math.min(1,innerHeight/{{height}});frame.style.width=(innerWidth/scale)+'px';frame.style.height='{{height}}px';frame.style.transform='scale('+scale+')';}
              addEventListener('resize',fit);fit();frame.src={{url}};
              const api=document.createElement('script');api.src='https://w.soundcloud.com/player/api.js';
              api.onload=()=>{
                function connect(url){const own=frame,widget=SC.Widget(own);window.__pookiePause=()=>widget.pause();
                  widget.bind(SC.Widget.Events.READY,()=>{if(own!==frame)return;
                    if(url)requestAnimationFrame(()=>requestAnimationFrame(()=>{if(own===frame)post('updated',url);}));else post('ready');});
                  widget.bind(SC.Widget.Events.ERROR,()=>{if(own===frame)post('error');});
                  widget.bind(SC.Widget.Events.PAUSE,()=>{if(own===frame)post('paused');});}
                connect();
                // api.js load() serializes non-start_track options as booleans,
                // including color. Keep this host/API and refresh only the iframe
                // with the exact URL; a fresh proxy also has fresh READY state.
                window.__pookieReload=url=>{const next=frame.cloneNode(false);next.removeAttribute('src');frame.replaceWith(next);
                  frame=next;fit();frame.src=url;connect(url);};
              };
              api.onerror=()=>post('error');document.head.append(api);
            })();
            </script></body></html>
            """;
    }

    private void Message(string raw, string source)
    {
        if (disposed || browser == null || !source.StartsWith("data:text/html", StringComparison.Ordinal)) return;
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (!json.RootElement.TryGetProperty("nonce", out var value) || value.GetString() != nonce) return;
            var kind = json.RootElement.GetProperty("kind").GetString();
            if (kind == "updated" && refreshing && json.RootElement.TryGetProperty("url", out var url) && url.GetString() == loadedUrl)
            { _ = FinishRefreshAsync(loadedUrl!); return; }
            if (kind == "error") { Fail(new InvalidOperationException("SoundCloud не смог открыть этот виджет.")); return; }
            if (kind == "ready")
            { deadline.Stop(); ready = true; failed = false; spinner.IsActive = false; placeholder.IsVisible = false; Frame(); }
            if (kind is "ready" or "paused") _ = CaptureAsync(revision, browser);
            if (kind == "ready") _ = CaptureSettledAsync(revision, browser);
        }
        catch (JsonException) { }
    }

    private async Task CaptureAsync(int version, IEmbeddedBrowser host, int delay = 100, bool allowRefresh = false)
    {
        var document = nonce;
        try
        {
            if (delay > 0) await Task.Delay(delay);
            if (version != revision || browser != host || disposed || popupFrozen || tooltipFrozen || refreshing && !allowRefresh || !ready || !requested || document != nonce) return;
            var bytes = await host.CaptureAsync().WaitAsync(TimeSpan.FromSeconds(3));
            if (version != revision || browser != host || disposed || popupFrozen || tooltipFrozen || refreshing && !allowRefresh || document != nonce) return;
            SnapshotPng = bytes;
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            for (var index = 0; index < image.Data.Length; index += 4) (image.Data[index], image.Data[index + 2]) = (image.Data[index + 2], image.Data[index]);
            snapshotPixels = image.Data; snapshotWidth = image.Width; snapshotHeight = image.Height;
            var next = ((IImageSource)ImageSource.FromBgraPixels(image.Width, image.Height, image.Data)).CreateImage(window!.GraphicsFactory);
            snapshot?.Dispose(); snapshot = next; InvalidateVisual();
        }
        catch (Exception error) { if (version == revision && !disposed) Pookie.Logging.AppLog.Failure("Pookie.Widget", "Кадр предпросмотра недоступен", error, Serilog.Events.LogEventLevel.Debug); }
    }

    private async Task CaptureSettledAsync(int version, IEmbeddedBrowser host)
    {
        var document = nonce;
        // Artwork/waveforms can arrive after READY. Refresh once after loading,
        // without polling native pixels during playback or scrolling.
        await Task.Delay(1200);
        if (version == revision && browser == host && ready && requested && !disposed && document == nonce) await CaptureAsync(version, host);
    }

    private void Hide(bool pause = true)
    {
        if (!shown || browser == null) return;
        if (window == null)
        { shown = staged = false; browser.SetVisible(false); if (pause) _ = PauseAsync(browser); return; }
        // Keep the native renderer mapped, but outside the window. GTK hide/show
        // discards its drawable and can expose a blank surface on the next map.
        shown = false; staged = true;
        if (snapshotPixels.Length == 0 || !browser.TryParkWithSnapshot(nativeBounds, window.DpiScale, snapshotPixels, snapshotWidth, snapshotHeight))
            browser.SetBounds(new(-32000, -32000, ActualWidth, ActualHeight), window.DpiScale);
        nativeBounds = default;
        if (pause) _ = PauseAsync(browser);
    }
    private static async Task PauseAsync(IEmbeddedBrowser host)
    { try { await host.ExecuteAsync("window.__pookiePause?.()"); } catch (Exception) { /* The page may be navigating or already closed. */ } }

    private void Fail(Exception error)
    {
        if (disposed) return;
        deadline.Stop(); reload.Stop(); Hide(); browser?.SetVisible(false); ready = false; failed = true; loadedUrl = playerUrl;
        spinner.IsActive = false; spinner.IsVisible = logo.IsVisible = false; message.IsVisible = placeholder.IsVisible = true;
        message.Text = "Не удалось загрузить плеер.\nОткрой другую вкладку и попробуй снова.";
        Pookie.Logging.AppLog.Failure("Pookie.Widget", "Предпросмотр SoundCloud недоступен", error);
    }

    internal void Close()
    {
        Deactivate(); deadline.Stop(); revision++; refreshVersion++; popupFrozen = tooltipFrozen = refreshing = false;
        var host = browser; var lease = profile; var starting = initialization;
        browser = null; profile = null; initializing = ready = false; loadedUrl = null;
        spinner.IsActive = false; snapshot?.Dispose(); snapshot = null; snapshotPixels = []; SnapshotPng = default; placeholder.IsVisible = true;
        InvalidateVisual();
        host?.Dispose();
        async Task Finish()
        { try { await starting; if (host != null) await host.ShutdownAsync(); } catch (Exception) { } finally { lease?.Dispose(); } }
        shutdown = Task.WhenAll(shutdown, Finish());
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);
        if (snapshot == null) return;
        context.Save();
        try { context.SetClipRoundedRect(Bounds, PreviewRadius, PreviewRadius); context.DrawImage(snapshot, Bounds); }
        finally { context.Restore(); }
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        if (window != null) window.FrameRendered -= Frame;
        base.OnVisualRootChanged(oldRoot, newRoot); window = newRoot as Window;
        if (window != null) window.FrameRendered += Frame; else Close();
    }
    protected override void OnDispose() { Close(); disposed = true; reload.Dispose(); deadline.Dispose(); base.OnDispose(); }
}
