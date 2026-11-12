using System.Text;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using StbImageSharp;
using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly List<WidgetBrowserFixture> widgetBrowsers = [];

    private async Task VerifyShareWidgetAsync(SoundCloudTrack track)
    {
        await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible && shareWidgetPreview.LogicalHeight == 450);
        var browser = widgetBrowsers[^1];
        if (!browser.Page.Contains("api.js") || !browser.Page.Contains("auto_play=false") || !browser.Page.Contains("innerHeight/450") ||
            !shareWidgetPreview.PlayerUrl!.Contains("%2314abcd") || shareEmbedCopy.ActualWidth != 48 || shareEmbedCode.Focusable ||
            shareContent.BodyBounds.Height <= PlaylistDialogContent.MaximumBodyHeight || shareWidgetPreview.ActualHeight != 450 ||
            shareEmbedCode.Bounds.Y <= shareWidgetPreview.Bounds.Bottom)
            throw new InvalidOperationException("Widget lost its actual player page, responsive visual preview or single-line code/copy row.");
        var inputFocus = Window.FocusManager.FocusedElement is TextBox or MultiLineTextBox;
        if (inputFocus) throw new InvalidOperationException("Opening Widget activated an input field.");
        await VerifyWidgetColorControlsAsync();
        var savedSize = Window.WindowSize;
        try
        {
            Window.WindowSize = WindowSize.Resizable(1160, 680, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareWidgetPreview.ActualHeight < 450 && shareWidgetPreview.NativeVisible);
            if (shareContent.BodyBounds.Y < 16 || shareContent.BodyBounds.Bottom > Window.ClientSize.Height - 16 ||
                browser.Bounds.Bottom > shareEmbedCopy.Bounds.Y || shareWidgetPreview.LogicalHeight != 450)
                throw new InvalidOperationException("Visual widget preview overlaps the code row or clips on a small window.");
            CaptureUiPreview("share-widget-small");
        }
        finally
        {
            Window.WindowSize = savedSize;
            await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareWidgetPreview.ActualHeight == 450 && shareWidgetPreview.NativeVisible);
        }
        SelectShareTab(1);
        await WaitForLikedLayoutAsync(() => !shareContent.Resizing);
        if (browser.Visible && browser.Bounds.X >= 0 || browser.Pauses == 0 || shareRecipientSearch.IsFocused || shareComposeText.IsFocused)
            throw new InvalidOperationException("Leaving Widget retained its native view/audio or auto-focused Message.");
        SelectShareTab(2);
        await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible);
        if (shareEmbedColor.IsFocused) throw new InvalidOperationException("Returning to Widget focused its color editor.");
        Console.WriteLine("SHARE_WIDGET_OK: native player host, live color/layout settings, full-height visual preview, small-window scaling, single-line code/copy, pause/hide on tab switch and neutral initial focus");
    }

    private async Task VerifyShareWidgetNativeAsync(SoundCloudTrack track)
    {
        CloseShare(true); await shareWidgetPreview.Shutdown;
        var factory = shareWidgetPreview.BrowserFactory;
        var builder = shareWidgetPreview.PageBuilder;
        try
        {
            shareWidgetPreview.BrowserFactory = EmbeddedBrowserFactory.Create;
            var realLink = Environment.GetEnvironmentVariable("POOKIE_WIDGET_REAL_LINK");
            shareWidgetPreview.PageBuilder = realLink != null ? EmbeddedWidgetPreview.BuildPage : (_, _, nonce) => "<!doctype html><html><head><style>" + EmbeddedWidgetPreview.ViewportCss +
                "</style></head><body><div id='viewport' style='background:#236bad;color:white;font:20px sans-serif'><h2>Widget native fixture</h2><p>Local browser preview</p></div><script>" +
                "function post(kind,url){const value=JSON.stringify({nonce:'" + nonce + "',kind,url});if(window.chrome?.webview)window.chrome.webview.postMessage(value);else window.webkit.messageHandlers.pookie.postMessage(value);}window.__pookiePause=()=>{};window.__pookieReload=url=>{document.getElementById('viewport').style.background='#18181c';post('paused');setTimeout(()=>{document.getElementById('viewport').style.background='#236bad';post('updated',url);},280);};setTimeout(()=>post('ready'),100);</script></body></html>";
            ShareTrack(realLink == null ? track : track with { PermalinkUrl = realLink }); SelectShareTab(2);
            if (!shareWidgetPreview.Loading) throw new InvalidOperationException("Widget did not show its SoundCloud/spinner loader while initializing its browser.");
            await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Animating && !shareContent.Resizing && shareWidgetPreview.Loading);
            CaptureUiPreview("share-widget-loading");
            var logo = shareWidgetPreview.LoadingLogoBounds; var spinner = shareWidgetPreview.LoadingSpinnerBounds;
            if (Math.Abs(spinner.Y - logo.Bottom - 16) > .5 || Math.Abs((logo.Y + spinner.Bottom) / 2 - shareWidgetPreview.Bounds.Center.Y) > .5 ||
                Math.Abs(logo.Center.X - shareWidgetPreview.Bounds.Center.X) > .5 || Math.Abs(spinner.Center.X - logo.Center.X) > .5)
                throw new InvalidOperationException("Visible SoundCloud mark and spinner are not a centered, tightly spaced loading group.");
            await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible && shareWidgetPreview.HasSnapshot, 25000);
            if (realLink != null) { await Task.Delay(1800, lifetime.Token); if (!shareWidgetPreview.Ready) throw new InvalidOperationException("SoundCloud player failed after READY."); }
            VerifyWidgetRoundedPixels();
            CaptureUiPreview(realLink == null ? "share-widget-native" : "share-widget-soundcloud");
            await ObserveWidgetDesktopPixelsAsync("copy-tooltip", VerifyWidgetCopyTooltipAsync, realLink == null);
            var bounds = shareWidgetPreview.Bounds; var player = shareWidgetPreview.PlayerUrl;
            await ObserveWidgetDesktopPixelsAsync("popup-open", async () =>
            {
                await ToggleShareWidgetColorAsync();
                await WaitForLikedLayoutAsync(() => shareWidgetColorPopup?.Reveal == 1);
            }, realLink == null);
            if (shareWidgetPreview.NativeVisible || !shareWidgetPreview.PopupFrozen || !shareWidgetPreview.HasSnapshot ||
                shareWidgetPreview.Loading || shareWidgetPreview.Bounds != bounds || shareWidgetPreview.PlayerUrl != player)
                throw new InvalidOperationException("Actual color popup blanked/reloaded/resized its native player.");
            VerifyWidgetRoundedPixels();
            CaptureUiPreview(realLink == null ? "share-widget-native-color-menu" : "share-widget-soundcloud-color-menu");
            await ObserveWidgetDesktopPixelsAsync("popup-close", async () =>
            {
                RouteShareKey(Key.Escape);
                if (!shareWidgetPreview.PopupFrozen) throw new InvalidOperationException("Native player returned before its popup's exit animation.");
                await WaitForLikedLayoutAsync(() => shareWidgetColorPopup?.IsVisible == false && shareWidgetPreview.NativeVisible);
            }, realLink == null);
            if (!shareWidgetPreview.Ready || shareWidgetPreview.Loading || shareWidgetPreview.Bounds != bounds || shareWidgetPreview.PlayerUrl != player)
                throw new InvalidOperationException("Closing the actual color popup restarted or moved its player.");
            Console.WriteLine("WIDGET_POPUP_NATIVE_OK: current native frame retained through in-app popup enter/exit, no player reload, loading flash, geometry change or squared corners");
            await ObserveWidgetDesktopPixelsAsync("preset-refresh", async () =>
            {
                RouteWaveformClick(SettingsCenter(shareWidgetSwatches[1].Button));
                await WaitForLikedLayoutAsync(() => !shareWidgetPreview.Refreshing && shareWidgetPreview.NativeVisible &&
                    shareWidgetPreview.PlayerUrl == System.Xml.Linq.XElement.Parse(shareEmbedCode.Text).Attribute("src")!.Value);
                // The debounce must have completed, not merely queued the edit.
                await Task.Delay(400, lifetime.Token);
                await WaitForLikedLayoutAsync(() => !shareWidgetPreview.Refreshing && shareWidgetPreview.NativeVisible);
                if (shareWidgetPreview.Loading) throw new InvalidOperationException("Native preset color edit displayed the full loader.");
            }, realLink == null);
            if (realLink != null)
            {
                var swatches = shareWidgetColors.ToArray();
                SetShareWidgetSwatches([Color.FromRgb(35, 199, 232), Color.FromRgb(211, 41, 184), .. swatches.Skip(2)]);
                foreach (var visual in new[] { false, true })
                {
                    if (visual) { SetShareEmbedLayout(true); await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible && shareWidgetPreview.LogicalHeight == 450); }
                    foreach (var index in new[] { 0, 1 })
                    {
                        var selected = shareWidgetColors[index];
                        RouteWaveformClick(SettingsCenter(shareWidgetSwatches[index].Button));
                        await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible && !shareWidgetPreview.Refreshing &&
                            WidgetColorPixels(selected) > 100, 25000);
                        if (shareWidgetPreview.Loading) throw new InvalidOperationException("Actual color update restarted the host loader.");
                        CaptureUiPreview($"share-widget-color-{(visual ? "visual" : "compact")}-{index}");
                        Console.WriteLine($"WIDGET_RENDERED_COLOR_OK visual={visual} color={WidgetHex(selected)} pixels={WidgetColorPixels(selected)}");
                    }
                }
                SetShareWidgetSwatches(swatches);
            }
            await ObserveWidgetDesktopPixelsAsync("overlay-close", async () =>
            { CloseShare(); await WaitForLikedLayoutAsync(() => !shareDialog.IsOpen); }, realLink == null);
            await WaitForLikedLayoutAsync(() => !shareDialog.IsVisible);
            CloseShare(true); await shareWidgetPreview.Shutdown;
            if (shareWidgetPreview.Ready) throw new InvalidOperationException("Native widget engine survived modal closing.");
            Console.WriteLine(realLink == null ? "SHARE_WIDGET_NATIVE_OK: actual platform browser paints an isolated local page, bridges readiness, captures a closing frame and shuts down"
                : "SHARE_WIDGET_SOUNDCLOUD_OK: production host loads the actual SoundCloud player and API, receives widget READY, captures its pixels and shuts down");
        }
        finally
        {
            CloseShare(true); await shareWidgetPreview.Shutdown;
            shareWidgetPreview.BrowserFactory = factory; shareWidgetPreview.PageBuilder = builder;
        }
    }

    private int WidgetColorPixels(Color target)
    {
        if (shareWidgetPreview.SnapshotPng.IsEmpty) return 0;
        var image = ImageResult.FromMemory(shareWidgetPreview.SnapshotPng.ToArray(), ColorComponents.RedGreenBlueAlpha);
        var count = 0;
        for (var offset = 0; offset < image.Data.Length; offset += 4)
            if (Math.Abs(image.Data[offset] - target.R) <= 3 && Math.Abs(image.Data[offset + 1] - target.G) <= 3 &&
                Math.Abs(image.Data[offset + 2] - target.B) <= 3 && image.Data[offset + 3] >= 250) count++;
        return count;
    }

    private async Task VerifyWidgetCopyTooltipAsync()
    {
        Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
        MoveActionPointer(SettingsCenter(shareEmbedCopy));
        await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Owner == shareEmbedCopy && tooltipLayer.Reveal == 1 && !shareWidgetPreview.NativeVisible);
        var overlap = tooltipLayer.SurfaceBounds.Intersect(shareWidgetPreview.Bounds);
        if (overlap.Width <= 12 || overlap.Height <= 6) throw new InvalidOperationException("Copy tooltip probe did not overlap the native widget.");
        if (OperatingSystem.IsLinux())
        {
            await Task.Delay(40, lifetime.Token);
            var display = XOpenDisplay(0);
            try
            {
                var pixel = ReadNativeChatVideoPixel(display, new(overlap.X + 5, overlap.Y + 4));
                var surface = MaterialTooltipLayer.Surface;
                if (Math.Abs((int)((pixel >> 16) & 255) - surface.R) > 4 || Math.Abs((int)((pixel >> 8) & 255) - surface.G) > 4 || Math.Abs((int)(pixel & 255) - surface.B) > 4)
                    throw new InvalidOperationException($"Native widget occludes the copy tooltip: #{pixel:x6}.");
            }
            finally { XCloseDisplay(display); }
        }
        CaptureUiPreview("share-widget-copy-tooltip");
        MoveActionPointer(new(2, 2));
        await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible && shareWidgetPreview.NativeVisible);
        Console.WriteLine("WIDGET_TOOLTIP_NATIVE_OK: copy hint paints above the widget on actual desktop pixels; native presentation resumes after the complete hint fade");
    }

    private async Task VerifyWidgetColorControlsAsync()
    {
        if (VisualTree.Find(shareEmbedColorRow, element => element is TextBlock { Text: "Цвет", Foreground: var color } && color == Color.White) == null)
            throw new InvalidOperationException("Widget color caption is not white.");
        if (shareEmbedColorRow.Bounds.Y < shareEmbedLayout.Bounds.Bottom + 13 || shareWidgetSwatches.Count != 5 ||
            Math.Abs(shareEmbedLayout.SelectionBounds.X - shareEmbedVisualButton.Bounds.X) > .5)
            throw new InvalidOperationException("Widget lost its separate color row or shared segmented selector.");
        var right = shareEmbedLayout.SelectionBounds.X;
        var rasterizations = shareEmbedLayout.ReflectionRasterizations;
        var label = (TextBlock)VisualTree.Find(shareEmbedCompact, element => element is TextBlock)!;
        MoveActionPointer(SettingsCenter(shareEmbedCompact)); await Task.Delay(280, lifetime.Token);
        if (shareEmbedCompact.Background.A != 0 || label.Foreground != Color.White)
            throw new InvalidOperationException("Inactive mode hover painted a background or failed to brighten its ink.");
        MoveActionPointer(new(2, 2)); await Task.Delay(280, lifetime.Token);
        if (label.Foreground != Color.FromRgb(166, 166, 173)) throw new InvalidOperationException("Inactive mode text did not return to its muted color.");
        RouteWaveformClick(SettingsCenter(shareEmbedCompact));
        await Task.Delay(60, lifetime.Token); await WaitForLoginFrameAsync();
        if (!shareEmbedLayout.Animating || shareEmbedLayout.SelectionBounds.Height >= 40 || shareEmbedLayout.SelectionBounds.X >= right || shareEmbedLayout.SelectionBounds.X <= shareEmbedCompact.Bounds.X)
            throw new InvalidOperationException("Widget mode pill did not animate between its options.");
        await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible && shareWidgetPreview.LogicalHeight == 166);
        RouteWaveformClick(SettingsCenter(shareEmbedVisualButton));
        await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible && shareWidgetPreview.LogicalHeight == 450);
        if (shareEmbedLayout.ReflectionRasterizations != rasterizations) throw new InvalidOperationException("Liquid mode switching rebuilt its optical raster.");
        if (!widgetBrowsers[^1].Page.Contains("border-radius:12px") || !widgetBrowsers[^1].Page.Contains("id=\"viewport\"") || shareWidgetPreview.CornerRadius != 12)
            throw new InvalidOperationException("Live player and transition frame do not share stable rounded corners.");
        var expected = new[] { Color.FromRgb(220, 35, 55), Color.FromRgb(35, 75, 220), Color.FromRgb(35, 180, 90), Color.FromRgb(230, 190, 30), Color.FromRgb(175, 40, 195) };
        var pixels = new byte[100 * 40 * 4];
        for (var y = 0; y < 40; y++) for (var x = 0; x < 100; x++)
        {
            var color = expected[x < 35 ? 0 : x < 60 ? 1 : x < 80 ? 2 : x < 92 ? 3 : 4]; var offset = (y * 100 + x) * 4;
            pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R; pixels[offset + 3] = 255;
        }
        var artwork = ImageSource.FromBgraPixels(100, 40, pixels);
        var colors = ArtworkSwatches.FromArtwork(artwork);
        if (colors.Length != 5 || colors[0] != expected[0] || expected.Any(color => !colors.Contains(color)) ||
            !colors.SequenceEqual(ArtworkSwatches.FromArtwork(artwork)))
            throw new InvalidOperationException("Artwork swatches lost a dominant color, population order or determinism.");
        var before = shareEmbedColor.Text;
        SetShareWidgetSwatches(colors);
        if (shareEmbedColor.Text != before) throw new InvalidOperationException("Late artwork replaced the selected widget color.");
        var presetLoads = widgetBrowsers[^1].Navigations; var presetRefreshes = widgetBrowsers[^1].Refreshes;
        RouteWaveformClick(SettingsCenter(shareWidgetSwatches[1].Button));
        if (shareEmbedColor.Text != WidgetHex(colors[1]) || !shareWidgetSwatches[1].Swatch.Selected)
            throw new InvalidOperationException("Preset click did not synchronize HEX, picker and selection ring.");
        await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible && !shareWidgetPreview.Refreshing && widgetBrowsers[^1].Refreshes > presetRefreshes);
        if (widgetBrowsers[^1].Navigations != presetLoads || shareWidgetPreview.Loading || !shareWidgetPreview.HasSnapshot)
            throw new InvalidOperationException("A color preset reloaded the host page or showed the full loading state.");
        var bounds = shareWidgetPreview.Bounds; var loads = widgetBrowsers[^1].Navigations;
        if (Math.Abs(shareWidgetColorPicker.Bounds.X - shareWidgetHexField.Bounds.Right - 2) > .5 ||
            !((MaterialTrackButton)shareWidgetColorPicker).IsConnected || shareWidgetHexField.Corners.TopRight != 8)
            throw new InvalidOperationException("HEX and palette action lost their connected glass shape.");
        MoveActionPointer(SettingsCenter(shareWidgetColorPicker)); await Task.Delay(260, lifetime.Token);
        if (shareWidgetColorPicker.Background.A <= 32) throw new InvalidOperationException("Palette action has no glass hover feedback.");
        RouteWaveformClick(SettingsCenter(shareWidgetColorPicker));
        await WaitForLikedLayoutAsync(() => shareWidgetColorPopup?.Reveal == 1 && !shareWidgetPreview.NativeVisible);
        var popup = shareWidgetColorPopup!;
        if (popup.FindVisualRoot() != Window || !shareWidgetPreview.HasSnapshot || !shareWidgetPreview.PopupFrozen ||
            shareWidgetPreview.Bounds != bounds || widgetBrowsers[^1].Navigations != loads || shareWidgetPreview.Loading ||
            KeyboardFocusFeedback.IsVisible(popup.Spectrum) || popup.SelectedColor != colors[1])
            throw new InvalidOperationException("Opening the in-app color menu reloaded/resized the widget or lost its painted frame.");
        RouteWaveformClick(popup.Spectrum.SpectrumBounds.Center);
        CaptureUiPreview("share-widget-color-picker");
        if (shareEmbedColor.Text != WidgetHex(popup.SelectedColor) || popup.SelectedColor == colors[1])
            throw new InvalidOperationException("Color spectrum did not update the actual widget color.");
        if (!shareWidgetPreview.HasSnapshot || shareWidgetPreview.Loading)
            throw new InvalidOperationException("Editing a color blanked the widget under its popup.");
        RouteShareKey(Key.Escape);
        if (popup.IsOpen || !shareDialog.IsOpen || !shareWidgetPreview.PopupFrozen) throw new InvalidOperationException("Escape closed Share or released the frame before the popup finished closing.");
        await WaitForLikedLayoutAsync(() => !popup.IsVisible);
        shareEmbedColor.Text = before;
        await WaitForLikedLayoutAsync(() => shareWidgetPreview.NativeVisible);
        CaptureUiPreview("share-widget-colors");
        Console.WriteLine("WIDGET_COLORS_OK: liquid glass pill, connected glass HEX/palette with hover, five artwork families, synchronized HSV/RGB spectrum, in-app popup with a stable native frame and focus/Escape");
    }

    private async Task ObserveWidgetDesktopPixelsAsync(string name, Func<Task> action, bool verify)
    {
        if (!verify || !OperatingSystem.IsLinux()) { await action(); return; }
        Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
        await WaitForLoginFrameAsync();
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for widget handoff verification.");
        var bounds = shareWidgetPreview.Bounds;
        var point = new Point(bounds.X + bounds.Width * .12, bounds.Y + bounds.Height * .8);
        var expected = ReadNativeChatVideoPixel(display, point);
        string? failure = null; var samples = 0;
        void Sample()
        {
            if (!shareDialog.IsOpen || shareDialog.Running) return;
            var actual = ReadNativeChatVideoPixel(display, point); samples++;
            for (var shift = 0; shift <= 16; shift += 8)
                if (Math.Abs((double)((actual >> shift) & 255) - (double)((expected >> shift) & 255)) > 4)
                    failure ??= $"Widget flashed during {name}: actual=#{actual:x6}, expected=#{expected:x6}, visible={shareWidgetPreview.NativeVisible}, frozen={shareWidgetPreview.PopupFrozen}, refreshing={shareWidgetPreview.Refreshing}.";
        }
        using var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5));
        timer.Tick += Sample; Window.FrameRendered += Sample; timer.Start();
        try { Sample(); await action(); Sample(); }
        finally { timer.Stop(); Window.FrameRendered -= Sample; XCloseDisplay(display); }
        if (failure != null) throw new InvalidOperationException(failure);
        if (samples < 3) throw new InvalidOperationException("Too few native widget pixel samples during " + name);
        Console.WriteLine($"WIDGET_DESKTOP_PIXELS_OK {name}: samples={samples}, continuous native/snapshot pixels #{expected:x6}");
    }

    private void VerifyWidgetRoundedPixels()
    {
        var image = ImageResult.FromMemory(shareWidgetPreview.SnapshotPng.ToArray(), ColorComponents.RedGreenBlueAlpha);
        var offset = ((int)(image.Height * .002) * image.Width + (int)(image.Width * .002)) * 4;
        if (Math.Abs(image.Data[offset] - 24) > 3 || Math.Abs(image.Data[offset + 1] - 24) > 3 || Math.Abs(image.Data[offset + 2] - 28) > 3)
            throw new InvalidOperationException("Native browser paints a square player corner instead of the shared rounded viewport.");
        var width = (int)Math.Ceiling(shareWidgetPreview.ActualWidth * Window.DpiScale);
        var height = (int)Math.Ceiling(shareWidgetPreview.ActualHeight * Window.DpiScale);
        using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, Window.DpiScale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-shareWidgetPreview.Bounds.X, -shareWidgetPreview.Bounds.Y);
        shareWidgetPreview.Render(context); context.EndFrame();
        var cpu = (ICpuPixelSurface)surface; var rgba = cpu.GetReadOnlyPixelSpan();
        if (rgba[cpu.StrideBytes + 7] > 5 || rgba[(height / 2) * cpu.StrideBytes + (width / 2) * 4 + 3] < 250)
            throw new InvalidOperationException("Cached transition image does not preserve the native preview's corner radius.");
        Console.WriteLine("WIDGET_CORNERS_OK: pixel-verified rounded native viewport and clipped cached transition frame");
    }

    private sealed class WidgetBrowserFixture(EmbeddedBrowserEvents events) : IEmbeddedBrowser
    {
        internal Rect Bounds;
        internal bool Visible, Disposed;
        internal int Pauses;
        internal int Navigations;
        internal int Refreshes;
        internal string Page = "";
        private string document = "", nonce = "";
        public Task InitializeAsync(nint parent, string profile, string script, bool isolated) => Task.CompletedTask;
        public void Navigate(string url)
        {
            document = url;
            Navigations++;
            if (!events.Navigate(url, true)) throw new InvalidOperationException("Widget rejected its own host page.");
            if (!events.Navigate("https://w.soundcloud.com/player/?url=fixture", true) || events.Navigate("https://soundcloud.com/unrequested-navigation", true))
                throw new InvalidOperationException("Widget misrouted its iframe or allowed an unsolicited top-level navigation.");
            Page = Encoding.UTF8.GetString(Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]));
            const string marker = "nonce:\"";
            var start = Page.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            nonce = Page.Substring(start, 32);
            events.Message(JsonSerializer.Serialize(new { nonce, kind = "ready" }), url);
        }
        public Task ExecuteAsync(string script)
        {
            if (script.Contains("Pause")) Pauses++;
            if (script.StartsWith("window.__pookieReload?.(", StringComparison.Ordinal))
            {
                Refreshes++; var url = JsonSerializer.Deserialize<string>(script[(script.IndexOf('(') + 1)..^1]);
                events.Message(JsonSerializer.Serialize(new { nonce, kind = "updated", url }), document);
            }
            return Task.CompletedTask;
        }
        public void SetBounds(Rect bounds, double scale) => Bounds = bounds;
        public void SetVisible(bool visible) => Visible = visible;
        public Task<byte[]> CaptureAsync() => Task.FromResult(ArtworkFixtureBitmap(100));
        public void Dispose() { Disposed = true; Visible = false; }
    }
}
