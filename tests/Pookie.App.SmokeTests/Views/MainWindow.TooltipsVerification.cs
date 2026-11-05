using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyMaterialTooltipsAsync()
    {
        Grid? fixture = null;
        try
        {
            if (OperatingSystem.IsWindows() && !ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53))
                throw new InvalidOperationException("Could not expose the native tooltip fixture.");
            timer.Stop(); Window.Activate(); await ShowWorkspaceAsync();
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            await WaitForLikedLayoutAsync(() => Window.IsActive && workspace.IsVisible && startupHeader.Opacity > .999);
            var clicks = 0;
            Button Button(string text) => TrackButtons.Action(new TextBlock().Text("Подсказка"), () => clicks++, TrackButtons.Text, 40).Width(120).Hint(text);
            var middle = Button("Добавить в плейлист").Left().Top().Margin(340, 240, 0, 0);
            var top = Button("Настройки воспроизведения").Right().Top().Margin(0, 8, 8, 0);
            var left = Button("Длинная подсказка с переносом текста на несколько строк 🎧").Left().Top().Margin(8, 240, 0, 0);
            var scrollButton = Button("Действие внутри списка");
            var scroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0)
                .Content(new StackPanel().Vertical().Children(scrollButton, new Border().Height(1000)))
                .Width(180).Height(100).Left().Top().Margin(600, 240, 0, 0);
            fixture = new Grid().Columns("*").Rows("*").Children(middle, top, left, scroll);
            ((Panel)Window.Content!).Add(fixture);
            await WaitForLikedLayoutAsync(() => Math.Abs(middle.ActualWidth - 120) <= 1 / Window.DpiScale && top.Bounds.Right > 1000);
            MoveActionPointer(new(2, 2)); Window.FocusManager.ClearFocus();
            var initialOverlayCount = Window.OverlayLayer.Count;
            var popupCountProperty = typeof(Window).GetProperty("DebugPopupCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
            var initialPopupCount = (int)popupCountProperty.GetValue(Window)!;
            var opening = new List<double>(); var closing = new List<double>();
            var openingPixels = false; var closingPixels = false;
            void Sample()
            {
                if (!tooltipLayer.IsVisible || tooltipLayer.Reveal is <= 0 or >= 1) return;
                (tooltipLayer.IsOpen ? opening : closing).Add(tooltipLayer.Reveal);
                if (tooltipLayer.IsOpen ? !openingPixels : !closingPixels)
                {
                    VerifyTooltipAlpha();
                    if (tooltipLayer.IsOpen) openingPixels = true; else closingPixels = true;
                }
            }
            Window.FrameRendered += Sample;
            try
            {
                MoveActionPointer(SettingsCenter(middle));
                await Task.Delay(100);
                if (tooltipLayer.IsVisible) throw new InvalidOperationException("A transient pointer crossing showed a tooltip without its hover delay.");
                MoveActionPointer(new(2, 2)); await Task.Delay(MaterialTooltipLayer.HoverDelayMs + 80);
                if (tooltipLayer.IsVisible || tooltipLayer.Running) throw new InvalidOperationException("A cancelled hover retained tooltip work.");
                OpenTopSearch(); await WaitForLoginFrameAsync(); topSearchInput.Focus();
                MoveActionPointer(SettingsCenter(middle)); RouteChatEscape();
                if (searching.Value || tooltipLayer.Running)
                    throw new InvalidOperationException("A pending tooltip consumed Escape intended for the underlying UI.");
                Window.FocusManager.ClearFocus();
                MoveActionPointer(new(2, 2));
                MoveActionPointer(SettingsCenter(middle)); middle.Hint(null);
                if (tooltipLayer.Running) throw new InvalidOperationException("Removing a pending hint retained its hover timer.");
                middle.Hint("Добавить в плейлист"); MoveActionPointer(new(2, 2));
                MoveActionPointer(SettingsCenter(middle));
                await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Reveal == 1);
                if (opening.Count < 3 || !openingPixels) throw new InvalidOperationException("Tooltip entrance skipped its translucent animation frames.");
                var bounds = tooltipLayer.SurfaceBounds;
                if (Math.Abs(bounds.Bottom + MaterialTooltipLayer.Gap - middle.Bounds.Y) > .5 ||
                    Math.Abs(bounds.X + bounds.Width / 2 - middle.Bounds.X - middle.ActualWidth / 2) > .5)
                    throw new InvalidOperationException("Tooltip followed the pointer instead of its element's centre.");
                var hit = Window.HitTest(SettingsCenter(middle));
                if (tooltipLayer.HitTest(SettingsCenter(middle)) != null || hit == null || hit != middle && !middle.IsAncestorOf(hit) ||
                    Window.FocusManager.FocusedElement != null || Window.OverlayLayer.Count != initialOverlayCount + 1 ||
                    (int)popupCountProperty.GetValue(Window)! != initialPopupCount)
                    throw new InvalidOperationException($"Tooltip stole input/focus or created a native popup: hit={hit?.GetType().Name}, focus={Window.FocusManager.FocusedElement?.GetType().Name}, overlays={Window.OverlayLayer.Count}/{initialOverlayCount}, popups={popupCountProperty.GetValue(Window)}/{initialPopupCount}.");
                CaptureUiPreview("tooltip-above", tooltipLayer);
                MoveActionPointer(new(middle.Bounds.X + 10, middle.Bounds.Y + 10)); await WaitForLoginFrameAsync();
                if (tooltipLayer.SurfaceBounds != bounds) throw new InvalidOperationException("Moving within the anchor moved its tooltip.");
                MoveActionPointer(new(2, 2));
                if (tooltipLayer.SurfaceBounds != bounds) throw new InvalidOperationException("Tooltip jumped before its exit animation.");
                await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                if (closing.Count < 2 || !closingPixels || tooltipLayer.Owner != null || tooltipLayer.Running)
                    throw new InvalidOperationException("Tooltip exit skipped frames or retained the old anchor/timer.");
                if (Window.OverlayLayer.Count != initialOverlayCount || tooltipLayer.Card.CacheMode != null)
                    throw new InvalidOperationException("Closed tooltip retained an overlay or its raster cache.");

                middle.Focus();
                for (var frame = 0; frame < 30 && tooltipLayer.Reveal < .15; frame++) await Task.Delay(5);
                if (tooltipLayer.Reveal is <= 0 or >= 1) throw new InvalidOperationException("Could not observe an in-progress tooltip for reversal.");
                var interrupted = tooltipLayer.Reveal;
                Window.FocusManager.ClearFocus(); middle.Focus();
                if (!tooltipLayer.IsOpen || Math.Abs(tooltipLayer.Reveal - interrupted) > .001)
                    throw new InvalidOperationException("Tooltip reversal jumped back to an animation endpoint.");
                await WaitForLikedLayoutAsync(() => tooltipLayer.Reveal == 1);

                top.Focus(); await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Reveal == 1);
                if (Math.Abs(tooltipLayer.SurfaceBounds.Y - top.Bounds.Bottom - MaterialTooltipLayer.Gap) > .5 ||
                    tooltipLayer.SurfaceBounds.Right > Window.ClientSize.Width - 7.5 || tooltipLayer.TransformOrigin.Y != tooltipLayer.SurfaceBounds.Y)
                    throw new InvalidOperationException("Top-edge tooltip did not flip below its anchor or retain its transform origin.");
                CaptureUiPreview("tooltip-below", tooltipLayer);
                RouteChatEscape(); await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                if (!top.IsFocused) throw new InvalidOperationException("Dismissing a tooltip lost keyboard focus.");
                await Task.Delay(100);
                if (tooltipLayer.IsOpen) throw new InvalidOperationException("Escape immediately reopened a focused tooltip.");
                left.Focus(); await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Reveal == 1);
                if (tooltipLayer.SurfaceBounds.X < 7.5 || tooltipLayer.SurfaceBounds.Width > 200.5 || tooltipLayer.SurfaceBounds.Height < 32)
                    throw new InvalidOperationException("Tooltip text did not wrap or escaped the left window edge.");
                CaptureUiPreview("tooltip-wrapped", tooltipLayer);
                var nativeDpi = Window.Dpi;
                var setDpi = typeof(Window).GetMethod("SetDpi", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                try
                {
                    foreach (uint dpi in new uint[] { 96, 110, 144, 192 })
                    {
                        setDpi.Invoke(Window, [dpi]); Window.PerformLayout(); await WaitForLoginFrameAsync();
                        var placed = tooltipLayer.SurfaceBounds;
                        if (placed.X < 7.5 || placed.Right > Window.ClientSize.Width - 7.5 || placed.Width > 200.5 || placed.Y < 7.5)
                            throw new InvalidOperationException($"Tooltip escaped its viewport after DPI {dpi}.");
                    }
                }
                finally { setDpi.Invoke(Window, [nativeDpi]); Window.PerformLayout(); }
                left.Hint("Обновлённая подсказка"); await WaitForLoginFrameAsync();
                if (tooltipLayer.Text != "Обновлённая подсказка") throw new InvalidOperationException("Rebinding retained stale tooltip content.");
                left.Hint(null); await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                Window.FocusManager.ClearFocus();
                scrollButton.Focus(); await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Reveal == 1);
                scroll.SetScrollOffsets(0, 30); await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                if (tooltipLayer.Running) throw new InvalidOperationException("Scrolling retained a tooltip or its observer work.");
                Window.FocusManager.ClearFocus();
                for (var i = 0; i < 10000; i++) middle.Hint(i % 2 == 0 ? "Воспроизвести" : "Приостановить");
                middle.Focus(); await WaitForLikedLayoutAsync(() => tooltipLayer.Reveal == 1);
                if (tooltipLayer.Text != "Приостановить" || middle.ToolTip != null)
                    throw new InvalidOperationException("A recycled hint retained stale text or a second native tooltip.");
                middle.IsEnabled = false; await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                middle.IsEnabled = true; Window.FocusManager.ClearFocus();
                middle.Focus(); await WaitForLikedLayoutAsync(() => tooltipLayer.Reveal == 1);
                typeof(Window).GetMethod("RaiseDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(Window, []);
                if (tooltipLayer.IsVisible || tooltipLayer.Running || tooltipLayer.Owner != null)
                    throw new InvalidOperationException("Deactivation retained tooltip animation or its anchor.");
                typeof(Window).GetMethod("RaiseActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(Window, []);
                Window.FocusManager.ClearFocus(); MoveActionPointer(SettingsCenter(middle));
                await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Reveal == 1);
                RouteWaveformClick(SettingsCenter(middle));
                await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                if (clicks != 1) throw new InvalidOperationException("A tooltip interfered with its anchor's click.");
                Console.WriteLine("MATERIAL_TOOLTIPS_UI_OK: anchor-centred placement, top/side edge fallback, DPI 96/110/144/192, wrapped emoji text, translucent enter/exit and continuous reversal, no native popup or focus/input capture, delayed-hover cancellation, keyboard/Escape, dynamic/recycled content, scroll/disable/deactivate/click cleanup and one idle-free overlay");
            }
            finally { Window.FrameRendered -= Sample; }
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine("MATERIAL_TOOLTIPS_UI_FAILED: " + error); }
        finally
        {
            if (OperatingSystem.IsWindows()) ProbeSetWindowPos(Window.Handle, -2, 0, 0, 0, 0, 0x13);
            if (fixture != null) { ((Panel)Window.Content!).Remove(fixture); fixture.Dispose(); }
            Window.Close();
        }
    }

    private void VerifyTooltipAlpha()
    {
        var bounds = tooltipLayer.SurfaceBounds;
        if (bounds.Width < 1 || bounds.Height < 1) return;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(bounds.Width * Window.DpiScale), (int)Math.Ceiling(bounds.Height * Window.DpiScale), Window.DpiScale, premultiplied: true));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-bounds.X, -bounds.Y);
        using var captureViewport = WindowPresentation.CaptureViewport(surface);
        tooltipLayer.Render(context); context.EndFrame();
        var pixels = ((ICpuPixelSurface)surface).CopyPixels();
        var maximum = Enumerable.Range(0, pixels.Length / 4).Max(index => pixels[index * 4 + 3]);
        if (maximum == 0 || maximum > tooltipLayer.Reveal * 255 + 3)
            throw new InvalidOperationException($"Tooltip backing did not fade with its text: alpha={maximum}, reveal={tooltipLayer.Reveal}, layer={tooltipLayer.Bounds}, card={tooltipLayer.Card.Bounds}, surface={bounds}, origin={tooltipLayer.TransformOrigin}.");
    }
}
