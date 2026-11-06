using System.Security.Cryptography;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;
using StbImageSharp;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal sealed partial class EmbeddedLoginView : FrameworkElement
{
    private readonly TaskCompletionSource<object> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string pairing = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private readonly WebSession? workerAccount;
    private Window? ownerWindow;
    private readonly string origin;
    private readonly string start;
    private readonly BrowserProfile profile;
    private readonly bool isolated;
    private IEmbeddedBrowser? browser;
    private bool initializing;
    private bool stopped;
    private Task initialization = Task.CompletedTask;
    private Task? stopping;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource controlsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? currentOrigin;
    private bool preparing, active;
    private Rect preparationBounds;
    private IImage? snapshot;
    private bool frozen;
    private readonly CancellationTokenSource presentationLifetime = new();

    internal event Action<string>? OriginChanged;
    internal Task<object> Completion => completed.Task;
    internal bool HasSnapshot => snapshot != null;
    internal ReadOnlyMemory<byte> SnapshotPng { get; private set; }
    internal double SnapshotOpacity { get; set; } = 1;
    internal bool NativePresented => active && !preparing && !stopped;
    internal bool Stopped => stopped;

    internal EmbeddedLoginView(string profilePath, string? fixture = null, WebSession? account = null)
    {
        if (account != null && !account.IsValid()) throw new ArgumentException("Некорректная сессия.", nameof(account));
        if (fixture != null && (!Uri.TryCreate(fixture, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !uri.IsLoopback || uri.UserInfo != "" || uri.Fragment != ""))
            throw new ArgumentException("Тест входа должен использовать локальный HTTP-адрес.", nameof(fixture));
        workerAccount = account;
        isolated = fixture != null;
        start = fixture ?? (account == null ? "https://soundcloud.com/you/likes" : "https://soundcloud.com/");
        origin = new Uri(start).GetLeftPart(UriPartial.Authority);
        profile = BrowserProfile.Open(fixture, profilePath);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        if (stopped || FindVisualRoot() is not Window window || window.Handle == 0) return;
        if (browser == null && !initializing) initialization = InitializeAsync(window);
        UpdateBounds();
    }

    private async Task InitializeAsync(Window window)
    {
        initializing = true; ownerWindow = window;
        try
        {
            var host = EmbeddedBrowserFactory.Create(new(Navigate, Message, SourceChanged, DocumentReady, Fail));
            browser = host;
            var body = workerAccount == null ? EmbeddedLoginPage.ReadyScript + "\n" + LoginCaptureScript.Read(origin) : BrowserRequestScript.Read(workerAccount, origin);
            var script = "if(window===window.top && location.origin===" + JsonSerializer.Serialize(origin) + ") { (()=>{\n" +
                "const pairing=" + JsonSerializer.Serialize(pairing) + ";\n" +
                "window.__pookiePost=message=>{const raw=JSON.stringify({...message,pookie_pairing:pairing});" +
                "if(window.chrome?.webview) window.chrome.webview.postMessage(raw);" +
                "else window.webkit.messageHandlers.pookie.postMessage(raw);};\n" + body + "\n})(); }";
            await host.InitializeAsync(window.Handle, profile.Path, script, isolated);
            if (stopped) return;
            UpdateBounds();
            host.Navigate(start);
        }
        catch (Exception error) { if (!stopped) Fail(error); }
    }

    private bool Navigate(string url, bool mainFrame)
    {
        if (stopped) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var target) && target.Scheme is "https" or "http" or "about";
    }

    private void Message(string raw, string source)
    {
        if (stopped) return;
        if (workerAccount != null) { WorkerMessage(raw, source); return; }
        if (completed.Task.IsCompleted) return;
        var message = BrowserRequestProtocol.Parse(raw, source, origin, pairing);
        if (message?.Kind == "ready") { controlsReady.TrySetResult(); return; }
        if (LoginSessionMessage.Parse(raw, source, origin, pairing) is { } session) completed.TrySetResult(session);
    }

    private void SourceChanged(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            currentOrigin = uri.GetLeftPart(UriPartial.Authority);
            OriginChanged?.Invoke(currentOrigin);
        }
    }

    private void DocumentReady()
    {
        ready.TrySetResult();
        // Provider pages receive no Pookie script. Their native load event and
        // painted screenshot are the readiness boundary instead.
        if (currentOrigin != null && currentOrigin != origin) controlsReady.TrySetResult();
    }

    private void Fail(Exception error) { completed.TrySetException(error); workerReady.TrySetException(error); }

    private void UpdateBounds()
    {
        var window = FindVisualRoot() as Window ?? ownerWindow;
        if (window == null) { browser?.SetVisible(false); return; }
        if (preparing)
        {
            browser?.SetBounds(new(-32000, -32000, preparationBounds.Width, preparationBounds.Height), window.DpiScale);
            browser?.SetVisible(!stopped);
            return;
        }
        browser?.SetBounds(TranslateRect(new(0, 0, ActualWidth, ActualHeight), window), window.DpiScale);
        var visible = !stopped && active;
        for (Element? element = this; element != null; element = element.Parent)
            if (element is UIElement ui && !ui.IsVisible) { visible = false; break; }
        browser?.SetVisible(visible && ActualWidth > 0 && ActualHeight > 0);
    }

    internal async Task PrepareAsync(Window window, Rect bounds, CancellationToken token)
    {
        preparing = true; preparationBounds = bounds;
        initialization = InitializeAsync(window);
        await initialization.WaitAsync(token);
        var first = await Task.WhenAny(ready.Task, completed.Task).WaitAsync(token);
        if (first == completed.Task) return;
        first = await Task.WhenAny(controlsReady.Task, completed.Task).WaitAsync(token);
        if (first == completed.Task) return;
        // Navigation completion may precede the first painted page. Keep the
        // loader until a nonempty frame exists instead of crossfading a blank image.
        while (!completed.Task.IsCompleted)
        {
            await PresentFrameAsync(window, token);
            if (await CaptureAsync(window, token)) break;
        }
        preparing = false; browser?.SetVisible(false);
    }

    internal async Task ActivateAsync()
    {
        if (stopped || frozen || active || FindVisualRoot() is not Window window) return;
        await PresentFrameAsync(window, presentationLifetime.Token);
        if (stopped || frozen || active) return;
        active = true; UpdateBounds();
    }

    internal async Task FreezeAsync()
    {
        frozen = true;
        if (active && !stopped && FindVisualRoot() is Window window)
        {
            try
            {
                using var capture = CancellationTokenSource.CreateLinkedTokenSource(presentationLifetime.Token);
                capture.CancelAfter(TimeSpan.FromSeconds(2));
                await CaptureAsync(window, capture.Token);
                // Present the replacement underneath the live native view first.
                // Hiding it before this frame reveals stale pixels for one refresh.
                await PresentFrameAsync(window, presentationLifetime.Token);
            }
            catch (Exception error) { Pookie.Logging.AppLog.Failure("Pookie.Login", "Не удалось обновить кадр закрытия входа", error, Serilog.Events.LogEventLevel.Debug); }
        }
        active = preparing = false; browser?.SetVisible(false);
    }

    private async Task<bool> CaptureAsync(Window window, CancellationToken token)
    {
        var bytes = await browser!.CaptureAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
        if (stopped) return false;
        var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        if (!HasPaintedContent(image)) return false;
        SnapshotPng = bytes;
        for (var i = 0; i < image.Data.Length; i += 4) (image.Data[i], image.Data[i + 2]) = (image.Data[i + 2], image.Data[i]);
        var source = ImageSource.FromBgraPixels(image.Width, image.Height, image.Data);
        var next = ((IImageSource)source).CreateImage(window.GraphicsFactory);
        snapshot?.Dispose(); snapshot = next; InvalidateVisual();
        return true;
    }

    private static bool HasPaintedContent(ImageResult image)
    {
        var first = image.Data.AsSpan(0, 4);
        for (var y = 0; y < image.Height; y += Math.Max(1, image.Height / 64))
        for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 64))
        {
            var offset = (y * image.Width + x) * 4;
            for (var channel = 0; channel < 3; channel++)
                if (image.Data[offset + 3] != 0 && Math.Abs(image.Data[offset + channel] - first[channel]) > 4) return true;
        }
        return false;
    }

    private async Task PresentFrameAsync(Window window, CancellationToken token)
    {
        if (stopped || window.WindowState == WindowState.Minimized) return;
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Rendered() => presented.TrySetResult();
        void Closed() => presented.TrySetCanceled();
        window.FrameRendered += Rendered; window.Closed += Closed;
        try { InvalidateVisual(); window.InvalidateVisual(); await presented.Task.WaitAsync(token); }
        finally { window.FrameRendered -= Rendered; window.Closed -= Closed; }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);
        if (snapshot == null || SnapshotOpacity <= 0) return;
        context.Save();
        try { context.GlobalAlpha *= (float)SnapshotOpacity; context.DrawImage(snapshot, Bounds); }
        finally { context.Restore(); }
    }

    protected override void OnVisibilityChanged() { base.OnVisibilityChanged(); UpdateBounds(); }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot == null) browser?.SetVisible(false);
    }
    protected override void OnDpiChanged(uint oldDpi, uint newDpi) { base.OnDpiChanged(oldDpi, newDpi); UpdateBounds(); }

    internal Task StopAsync()
    {
        if (stopping != null) return stopping;
        stopped = true;
        StopWorker();
        presentationLifetime.Cancel();
        completed.TrySetCanceled();
        browser?.Dispose();
        return stopping = FinishStopAsync();
    }

    private async Task FinishStopAsync()
    {
        // Environment creation is asynchronous on Windows. Keep the lease until
        // even an immediately cancelled initialization has released its controller.
        try { await initialization.ConfigureAwait(false); if (browser != null) await browser.ShutdownAsync().ConfigureAwait(false); }
        finally { profile.Dispose(); }
    }

    internal void Stop() => _ = StopAsync();

    protected override void OnDispose()
    {
        Stop(); presentationLifetime.Dispose(); snapshot?.Dispose(); snapshot = null; SnapshotPng = default; base.OnDispose();
    }
}
