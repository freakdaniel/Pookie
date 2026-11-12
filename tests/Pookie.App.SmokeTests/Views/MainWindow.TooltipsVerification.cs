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
                Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
                Window.FocusManager.ClearFocus(); MoveActionPointer(new(2, 2)); MoveActionPointer(SettingsCenter(middle));
                await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Reveal == 1);
                RouteWaveformClick(SettingsCenter(middle));
                await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                if (clicks != 1) throw new InvalidOperationException("A tooltip interfered with its anchor's click.");
                await VerifyTooltipModalReturnAsync(fixture);
                await VerifyAvatarGroupAsync(fixture, initialOverlayCount);
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

    private async Task VerifyTooltipModalReturnAsync(Grid fixture)
    {
        ModalDialog? dialog = null;
        var close = TrackButtons.Icon(Icons.View("x", 20), () => dialog!.Close());
        var content = new Border().Width(240).Height(100).Background(PopupAppearance.Surface)
            .Child(new TextBlock().Text("Оверлей").Center());
        dialog = new ModalDialog(content, close, close);
        var triggers = new[] { "Поделиться", "Добавить в плейлист" }.Select((text, index) =>
            TrackButtons.Action(new TextBlock().Text("Открыть"), dialog.Show, TrackButtons.Glass, 40)
                .Width(120).Left().Top().Margin(340 + index * 160, 340, 0, 0).Hint(text)).ToArray();
        foreach (var trigger in triggers) fixture.Add(trigger);
        fixture.Add(dialog);
        try
        {
            await WaitForLoginFrameAsync();
            foreach (var trigger in triggers)
            {
                MoveActionPointer(SettingsCenter(trigger)); RouteWaveformClick(SettingsCenter(trigger));
                await WaitForLikedLayoutAsync(() => dialog.IsOpen && !dialog.Running);
                MoveActionPointer(SettingsCenter(close)); RouteWaveformClick(SettingsCenter(close));
                await WaitForLikedLayoutAsync(() => !dialog.IsVisible && trigger.IsFocused);
                await Task.Delay(MaterialTooltipLayer.HoverDelayMs + 80, lifetime.Token);
                if (trigger.IsMouseOver || tooltipLayer.Owner == trigger || KeyboardFocusFeedback.IsVisible(trigger))
                    throw new InvalidOperationException($"Pointer dismissal restored a stuck tooltip on {TooltipHints.TextFor(trigger)}: hover={trigger.IsMouseOver}, focus={trigger.IsFocused}, cue={KeyboardFocusFeedback.IsVisible(trigger)}, owner={tooltipLayer.Owner == trigger}, open={tooltipLayer.IsOpen}.");
                MoveActionPointer(SettingsCenter(trigger));
                await WaitForLikedLayoutAsync(() => tooltipLayer.Owner == trigger && tooltipLayer.Reveal == 1);
                MoveActionPointer(new(2, 2));
                await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
                Window.FocusManager.ClearFocus(); trigger.Focus();
                await WaitForLikedLayoutAsync(() => tooltipLayer.Owner == trigger && tooltipLayer.Reveal == 1);
                dialog.Show();
                await WaitForLikedLayoutAsync(() => dialog.IsOpen && !dialog.Running);
                dialog.Close();
                await WaitForLikedLayoutAsync(() => !dialog.IsVisible && trigger.IsFocused && tooltipLayer.Owner == trigger && tooltipLayer.Reveal == 1);
                Window.FocusManager.ClearFocus();
                await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);
            }
            Console.WriteLine("MODAL_TOOLTIP_RETURN_OK: pointer dismissal keeps restored triggers quiet, real hover still works, keyboard focus restores its hint");
        }
        finally
        {
            dialog.Close(true); fixture.Remove(dialog); dialog.Dispose();
            foreach (var trigger in triggers) { fixture.Remove(trigger); trigger.Dispose(); }
        }
    }

    private async Task VerifyAvatarGroupAsync(Grid fixture, int initialOverlayCount)
    {
        var selected = -1;
        var group = new AvatarGroup().Left().Top().Margin(340, 400, 0, 0);
        for (var index = 0; index < 7; index++)
        {
            var person = index;
            group.AddAvatar(new Border().Background(Color.FromRgb((byte)(60 + index * 25), 110, 160))
                .CornerRadius(21).Child(new TextBlock().Text((index + 1).ToString()).Center()),
                index == 1 ? "Второй слушатель" : "Слушатель " + (index + 1), () => selected = person);
        }
        fixture.Add(group);
        await WaitForLikedLayoutAsync(() => group.ActualWidth > 250 && !group.IsArrangeDirty);
        var first = (Button)((ContentControl)group.Children[0]).Content!;
        var second = (Button)((ContentControl)group.Children[1]).Content!;
        if (Math.Abs(second.Bounds.X - first.Bounds.Right + AvatarGroup.Overlap) > .5)
            throw new InvalidOperationException("Follower avatars did not overlap by their shared border.");
        var overlap = new Point(first.Bounds.Right - 5, first.Bounds.Center.Y);
        var hit = Window.HitTest(overlap);
        if (hit != first && (hit == null || !first.IsAncestorOf(hit)))
            throw new InvalidOperationException("Inverted avatar overlap disagreed with pointer targeting.");
        Window.FocusManager.ClearFocus(); MoveActionPointer(SettingsCenter(first));
        if (!tooltipLayer.IsOpen || tooltipLayer.Owner != first)
            throw new InvalidOperationException("The avatar group kept the ordinary tooltip hover delay.");
        await WaitForLikedLayoutAsync(() => first.TranslatePoint(default, Window).Y < first.Bounds.Y - 10 && tooltipLayer.Reveal == 1);
        await Task.Delay(500);
        if (!first.IsMouseOver) throw new InvalidOperationException("Lifting an avatar lost its resting hover target.");
        var positions = new HashSet<double>();
        void Sample() { if (tooltipLayer.IsOpen) positions.Add(tooltipLayer.SurfaceBounds.X); }
        Window.FrameRendered += Sample;
        try
        {
            MoveActionPointer(SettingsCenter(second));
            if (!tooltipLayer.IsOpen || tooltipLayer.Owner != second || tooltipLayer.Text != "Второй слушатель")
                throw new InvalidOperationException("Moving between avatars closed the shared tooltip or kept its previous label.");
            await WaitForLikedLayoutAsync(() => !tooltipLayer.Running && second.TranslatePoint(default, Window).Y < second.Bounds.Y - 10);
            await Task.Delay(100);
            var sharedEdge = new Point(first.Bounds.Right - 5, first.Bounds.Center.Y - 11);
            var overlapHit = Window.HitTest(sharedEdge);
            if (overlapHit != first && (overlapHit == null || !first.IsAncestorOf(overlapHit)))
                throw new InvalidOperationException("Hovering promoted an avatar above its neighbor.");
            if (Math.Abs(second.TranslatePoint(default, Window).Y - tooltipLayer.SurfaceBounds.Bottom - MaterialTooltipLayer.AvatarGap) > .5)
                throw new InvalidOperationException("Avatar tooltip kept an excessive gap from the lifted avatar.");
            if (positions.Count < 3 || Window.OverlayLayer.Count != initialOverlayCount + 1)
                throw new InvalidOperationException("Avatar tooltip jumped between anchors or created multiple overlays.");
            CaptureUiPreview("avatar-group-hover");
            CaptureUiPreview("avatar-group-tooltip", tooltipLayer);
            // Click the raised cap, outside the avatar's original rectangular bounds.
            RouteWaveformClick(new(second.Bounds.Center.X, second.TranslatePoint(default, Window).Y + 3));
            if (selected != 1) throw new InvalidOperationException("The lifted avatar cap did not open its own profile.");
        }
        finally { Window.FrameRendered -= Sample; }
        MoveActionPointer(new(2, 2)); Window.FocusManager.ClearFocus();
        await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible && Math.Abs(second.TranslatePoint(default, Window).Y - second.Bounds.Y) < .01);
        if (tooltipLayer.Running || Window.OverlayLayer.Count != initialOverlayCount)
            throw new InvalidOperationException("Leaving an avatar group retained its tooltip animation or overlay.");
        fixture.Remove(group); group.Dispose();
        Console.WriteLine("AVATAR_GROUP_UI_OK: stable inverted overlap while hovered, close anchored tooltip with animated retarget/resize, spring lift without hover loss, raised-cap click, return and overlay cleanup");
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
