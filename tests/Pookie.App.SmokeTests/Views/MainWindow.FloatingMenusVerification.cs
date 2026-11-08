using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyFloatingMenusAsync()
    {
        try
        {
            timer.Stop();
            await ShowWorkspaceAsync();
            async Task ClickMenuAsync(Point point)
            {
                if(!Window.IsActive){Window.Activate();await WaitForLikedLayoutAsync(()=>Window.IsActive);}
                RouteWaveformClick(point);
            }
            Window.Activate();
            playerVisible.Value = true;
            var fixture = tracks.Concat(Enumerable.Range(4, 9).Select(id => new SoundCloudTrack
                { Id = id, Title = "Тестовый звук " + id, Duration = 180000, User = new() { Username = "Pookie audio test" } })).ToArray();
            StartQueueContext(new("menu-fixture", "Понравившиеся треки", "Из подборки"), fixture, fixture[0]);
            playbackQueue.Enqueue(new() { Id = 50, Title = "Добавленный трек", Duration = 120000, User = new() { Username = "Твой выбор" } });
            RefreshQueue();
            playerBackdrop.SetPalette(new(Color.FromRgb(35, 47, 62), Color.FromRgb(43, 40, 60), Color.FromRgb(51, 37, 47)));
            await WaitForLikedLayoutAsync(() => Window.IsActive && workspace.IsVisible && startupHeader.Opacity > .999 &&
                queueButton.ActualWidth > 0 && playerChrome.ActualHeight >= PlayerBarHeight - 1);
            MoveActionPointer(new Point(2, 2));
            var context = PopupAppearance.CreateMenu();
            var contextCommand = new Command("pookie.menu.style-probe", "Настройки меню");
            var contextExecutions = 0;
            profileButton.Commands.Register(contextCommand, () => contextExecutions++);
            context.Item(contextCommand);
            var disabledCommand = new Command("pookie.menu.disabled-probe", "Недоступный пункт");
            profileButton.Commands.Register(disabledCommand, () => throw new InvalidOperationException("Disabled menu command executed."), () => false);
            context.Item(disabledCommand);
            var contextFrames = new List<double>();
            var openingPixels = false;
            void ContextFrame()
            {
                contextFrames.Add(context.Reveal);
                if (!openingPixels && context.Reveal is > .15 and < .8 && context.Card.ActualWidth > 0)
                { VerifyMenuSurfaceAlpha(context); openingPixels = true; CaptureUiPreview("menu-context-opening", context); }
            }
            Window.FrameRendered += ContextFrame;
            context.Show(profileButton);
            await WaitForLikedLayoutAsync(() => context.Card.ActualWidth > 100 && context.Reveal == 1);
            Window.FrameRendered -= ContextFrame;
            if (context.Parent != Window.Content || context.FindVisualRoot() != Window)
                throw new InvalidOperationException("Context menu created a separate popup surface instead of the chat overlay.");
            if (!openingPixels || contextFrames.Count(value => value > 0 && value < 1) < 3)
                throw new InvalidOperationException($"Context menu skipped its animated entrance: {string.Join(", ", contextFrames.Select(value => value.ToString("F2")))}.");
            if (context.Card.Background != PopupAppearance.Surface || context.Card.CornerRadius != PopupAppearance.Radius ||
                context.Buttons.Any(button => button.Height != PopupAppearance.RowHeight) || context.Card.FontSize != 14 || context.Card.Padding != new Thickness(PopupAppearance.Padding))
                throw new InvalidOperationException("Framework context menu did not inherit the shared popup appearance.");
            CaptureUiPreview("menu-context", context);
            var contextButton = context.Buttons[0];
            MoveActionPointer(new Point(2, 2));
            await WaitForLikedLayoutAsync(() => contextButton.Background == Color.Transparent);
            var hover = TrackButtons.HoverColor(Color.Transparent, TrackButtons.OnSurface);
            var hoverFrames = new List<Color>();
            void HoverFrame() => hoverFrames.Add(contextButton.Background);
            Window.FrameRendered += HoverFrame;
            try
            {
                MoveActionPointer(Center(contextButton));
                await WaitForLikedLayoutAsync(() => contextButton.Background == hover);
                if (!hoverFrames.Any(value => value != Color.Transparent && value != hover))
                    throw new InvalidOperationException("Context menu hover colour skipped intermediate rendered frames.");
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException($"Menu hover failed: open={context.IsOpen}, visible={context.IsVisible}, focus={Window.FocusManager.FocusedElement?.GetType().Name}, bounds={contextButton.Bounds}, over={contextButton.IsMouseOver}, colour={contextButton.Background}, hit={Window.HitTest(Center(contextButton))?.GetType().Name}, frames={hoverFrames.Count}.", error);
            }
            finally { Window.FrameRendered -= HoverFrame; }
            CaptureUiPreview("menu-context-hover", context);
            var contextClicks = 0;
            contextButton.Click += () => contextClicks++;
            var closingFrames = new List<double>(); var closingPixels = false;
            void ClosingFrame()
            {
                if (context.IsOpen) return;
                closingFrames.Add(context.Reveal);
                if (!closingPixels && context.Reveal is > .15 and < .8)
                { VerifyMenuSurfaceAlpha(context); closingPixels = true; CaptureUiPreview("menu-context-closing", context); }
            }
            Window.FrameRendered += ClosingFrame;
            RouteSettingsOptionClick(contextButton);
            try { await WaitForLikedLayoutAsync(() => !context.IsOpen && contextExecutions == 1); }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException($"Context command failed: open={context.IsOpen}, clicks={contextClicks}, executed={contextExecutions}, enabled={contextButton.IsEnabled}/{contextButton.IsEffectivelyEnabled}, ownerCan={Window.CommandRouter.CanExecute(contextCommand, CommandTarget.From(profileButton))}, rowCan={Window.CommandRouter.CanExecute(contextCommand, CommandTarget.From(contextButton))}, root={contextButton.FindVisualRoot()?.GetType().Name}.", error);
            }
            await WaitForLikedLayoutAsync(() => !context.IsVisible && context.Parent == null);
            Window.FrameRendered -= ClosingFrame;
            if (!closingPixels || closingFrames.Count(value => value > 0 && value < 1) < 3)
                throw new InvalidOperationException("Context menu detached before its closing animation completed.");
            Console.WriteLine("CONTEXT_MENU_SURFACE_OK: same main-window overlay as chat; opening/closing alpha pixels without opaque backing");
            Console.WriteLine("CONTEXT_MENU_MOTION_OK: intermediate entrance and hover colours, original owner command executes once and dismisses popup");
            var contextRows = context.Buttons.ToArray();
            PopupAppearance.Attach(profileButton, context);
            var trigger = Center(profileButton);
            RouteContextRightClick(trigger);
            await WaitForLikedLayoutAsync(() => context.IsOpen && context.Reveal == 1);
            if (context.Model.FindVisualRoot() is Window || !context.Buttons.SequenceEqual(contextRows) || context.Buttons[1].IsEffectivelyEnabled)
                throw new InvalidOperationException("Right click opened the native fallback, rebuilt rows, or enabled a disabled command.");
            RouteContextRightClick(new(profileButton.Bounds.X + 3, profileButton.Bounds.Y + 3));
            if (context.IsOpen) throw new InvalidOperationException("Repeated right click on the same owner reopened its menu.");
            await WaitForLikedLayoutAsync(() => !context.IsVisible);
            RouteContextRightClick(trigger);
            await WaitForLikedLayoutAsync(() => context.IsOpen && context.Reveal == 1);
            PopupAppearance.Attach(queueButton, context);
            RouteContextRightClick(new(queueButton.Bounds.X + 3, queueButton.Bounds.Y + 3));
            await WaitForLikedLayoutAsync(() => context.IsOpen && context.PlacementTarget == queueButton && context.Reveal == 1);
            context.Close(true); RouteContextRightClick(trigger);
            await WaitForLikedLayoutAsync(() => context.IsOpen && context.Reveal == 1);
            RouteSettingsKey(Key.End);
            await WaitForLikedLayoutAsync(() => contextButton.IsFocused);
            RouteSettingsKey(Key.Enter);
            await WaitForLikedLayoutAsync(() => !context.IsOpen && contextExecutions == 2);
            Console.WriteLine("CONTEXT_MENU_INPUT_OK: native right click, reused rows, disabled-command skipping and keyboard Enter use the original command scope");
            await WaitForLikedLayoutAsync(() => !context.IsVisible);
            await VerifyPopupReopenAsync(profileButton);
            foreach (var (button, state, name) in new[] {
                (profileButton, profileOpen, "profile"), (queueButton, queueOpen, "queue") })
            {
                CloseShellMenus(true); MoveActionPointer(new Point(2, 2));
                var layer = shellMenus.Single(menu => ReferenceEquals(menu.State, state)).Layer;
                var opening = new List<double>(); var closing = new List<double>();
                var drawerHeights = new List<double>();
                var samples = opening;
                void Sample()
                {
                    samples.Add(layer.Reveal);
                    if (layer is PlayerQueueDrawer drawer && layer.Reveal is > 0 and < 1)
                    {
                        drawerHeights.Add(drawer.SurfaceBounds.Height);
                        if (drawerHeights.Count == 3) CaptureUiPreview("queue-drawer-opening");
                    }
                }
                Window.FrameRendered += Sample;
                try
                {
                    MoveActionPointer(Center(button));
                    await Task.Delay(350, lifetime.Token);
                    if (state.Value || layer.IsVisible)
                        throw new InvalidOperationException(name + " menu opened on hover without a click.");
                    await ClickMenuAsync(Center(button));
                    await WaitForLikedLayoutAsync(() => layer.Reveal > .999 && layer.Card.ActualWidth > 0);
                    if (!state.Value || layer.Card.BorderThickness != 0 ||
                        layer is FloatingMenuLayer && (layer.Card.Background != PopupAppearance.Surface || layer.Card.CornerRadius != PopupAppearance.Radius))
                        throw new InvalidOperationException(name + " menu lost its surface styling.");
                    if (layer is PlayerQueueDrawer drawer && (drawerHeights.Distinct().Count() < 3 || drawer.ContentReveal < .99 ||
                        drawer.RenderedGradient != playerBackdrop.CurrentGradient))
                        throw new InvalidOperationException("Queue drawer lost its expanding reveal, content fade or shared player gradient.");
                    CheckAnchor();
                    if (layer.HitTest(Center(button)) != null || layer.HitTest(new Point(2, 2)) != null)
                        throw new InvalidOperationException(name + " menu captures its trigger or the empty workspace.");
                    if (opening.Count(value => value > 0 && value < 1) < 3)
                        throw new InvalidOperationException(name + " menu opening snapped.");
                    CaptureUiPreview("menu-" + name);
                    if (layer is PlayerQueueDrawer)
                    {
                        var scroll = (ScrollViewer)queueList.FindVisualChild<ScrollViewer>()!;
                        await WaitForLikedLayoutAsync(() => SmoothScroll.Maximum(scroll) > 100);
                        var offset = Math.Min(140, SmoothScroll.Maximum(scroll));
                        scroll.SetScrollOffsets(0, offset);
                        await WaitForLikedLayoutAsync(() => Math.Abs(scroll.VerticalOffset - offset) < 1);
                        CaptureUiPreview("queue-drawer-scrolled");
                        scroll.SetScrollOffsets(0, 0);
                        await WaitForLikedLayoutAsync(() => scroll.VerticalOffset == 0);
                        var expected = new[] { playbackQueue.Current! }.Concat(playbackQueue.Snapshot.Upcoming).Select(entry => (long?)entry.EntryId);
                        if (!popupQueueData.Where(block => block.Kind == QueueBlockKind.Track).Select(block => block.EntryId).SequenceEqual(expected) ||
                            !popupQueueData.Any(block => block.Key == "now") || !popupQueueData.Any(block => block.Key == "manual"))
                            throw new InvalidOperationException("Queue drawer lost the current/manual/next groups or actual playback order.");
                    }
                    MoveActionPointer(Center(layer.Card));
                    await Task.Delay(320, lifetime.Token);
                    if (!state.Value) throw new InvalidOperationException(name + " menu closed while its content was hovered.");
                    MoveActionPointer(new Point(2, 2));
                    await Task.Delay(400, lifetime.Token);
                    if (!state.Value) throw new InvalidOperationException(name + " menu closed merely because the pointer left.");
                    await ClickMenuAsync(new Point(layer.Card.Bounds.X + 20, layer.Card.Bounds.Y + 20));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.IsFocusWithin);
                    samples = closing;
                    await ClickMenuAsync(Center(button));
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (state.Value || closing.Count(value => value > 0 && value < 1) < 3)
                        throw new InvalidOperationException(name + " menu did not fade out after clicking its trigger again.");
                    MoveActionPointer(Center(button));
                    await Task.Delay(350, lifetime.Token);
                    if (state.Value) throw new InvalidOperationException(name + " menu reopened on hover after being toggled off.");
                    await ClickMenuAsync(Center(button));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.Reveal > .999);
                    await ClickMenuAsync(new Point(2, 2));
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (state.Value) throw new InvalidOperationException(name + " menu did not dismiss after an outside click.");
                    await ClickMenuAsync(Center(button));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.Reveal > .999);
                    settingsButton.Focus();
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (state.Value) throw new InvalidOperationException(name + " menu retained focus after focus moved to another control.");
                    await ClickMenuAsync(Center(button));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.Reveal > .999);
                    // Crossing the small gap must not collapse the card.
                    var gapY = ReferenceEquals(state, queueOpen) ? button.Bounds.Y - 4 : button.Bounds.Bottom + 4;
                    MoveActionPointer(new Point(button.Bounds.X + button.ActualWidth / 2, gapY));
                    await Task.Delay(100, lifetime.Token);
                    if (!state.Value) throw new InvalidOperationException(name + " menu closed while crossing its gap.");
                    MoveActionPointer(Center(layer.Card));
                    foreach (var (width, height) in new[] { (1000, 680), (1280, 840), (1600, 900) })
                    {
                        Window.WindowSize = WindowSize.Resizable(width, height, minWidth: 1000, minHeight: 680);
                        await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - width) < 1 && Math.Abs(Window.ClientSize.Height - height) < 1 &&
                            Math.Abs(layer.ActualWidth - width) < 1 && !workspace.IsArrangeDirty && !layer.IsArrangeDirty);
                        VerifyHeaderCardLayout();
                        CheckAnchor(); MoveActionPointer(Center(layer.Card));
                        if (layer is PlayerQueueDrawer) CaptureUiPreview($"queue-drawer-{width}x{height}");
                    }
                    state.Value = false; await Task.Delay(50, lifetime.Token);
                    var interrupted = layer.Reveal; state.Value = true;
                    if (Math.Abs(layer.Reveal - interrupted) > .0001)
                        throw new InvalidOperationException(name + " menu jumped when its dismissal was interrupted.");
                    await WaitForLikedLayoutAsync(() => layer.Reveal > .999);
                    layer.Card.Focus();
                    var escape = new KeyEventArgs(Key.Escape, 0);
                    typeof(Window).GetMethod("RaisePreviewKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(Window, [escape]);
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (!escape.Handled || state.Value) throw new InvalidOperationException(name + " menu did not handle Escape.");
                    if (!button.IsFocused) throw new InvalidOperationException(name + " menu did not return keyboard focus to its trigger.");
                    if (layer is PlayerQueueDrawer)
                    {
                        await ClickMenuAsync(Center(button));
                        await WaitForLikedLayoutAsync(() => layer.Reveal > .999);
                        var close = (Button)VisualTree.Find(layer.Card, element => element is Button { Content: Image })!;
                        await ClickMenuAsync(Center(close));
                        await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                        if (state.Value) throw new InvalidOperationException("The queue drawer close button did not dismiss it.");
                    }
                    await ClickMenuAsync(Center(button));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.Reveal > .999);
                    const System.Reflection.BindingFlags activationFlags = System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                    typeof(Window).GetMethod("RaiseDeactivated", activationFlags)!.Invoke(Window, []);
                    if (state.Value || layer.IsVisible)
                        throw new InvalidOperationException(name + " menu survived window deactivation.");
                    typeof(Window).GetMethod("RaiseActivated", activationFlags)!.Invoke(Window, []);
                }
                finally { Window.FrameRendered -= Sample; }

                void CheckAnchor()
                {
                    var card = layer.Card.Bounds;
                    if (layer is PlayerQueueDrawer)
                    {
                        if (Math.Abs(card.Bottom - playerChrome.Bounds.Y) > .5 || Math.Abs(card.Right - playerContentFrame.Bounds.Right) > .5 ||
                            Math.Abs(card.Width - 460) > 1 / Window.DpiScale || card.Y < startupHeader.Bounds.Bottom || card.Right > Window.ClientSize.Width + .5)
                            throw new InvalidOperationException($"Queue drawer detached from the player: card={card}, bar={playerChrome.Bounds}.");
                        return;
                    }
                    var gap = ReferenceEquals(state, queueOpen) ? button.Bounds.Y - card.Bottom : card.Y - button.Bounds.Bottom;
                    if (Math.Abs(card.Right - button.Bounds.Right) > 1 || Math.Abs(gap - 8) > 1 ||
                        card.X < 0 || card.Y < 0 || card.Right > Window.ClientSize.Width || card.Bottom > Window.ClientSize.Height)
                        throw new InvalidOperationException($"{name} menu lost its anchor after layout: card={card}, button={button.Bounds}.");
                }
            }
            MoveActionPointer(new Point(2, 2));
            await ClickMenuAsync(Center(profileButton));
            await WaitForLikedLayoutAsync(() => shellMenus.Single(menu => ReferenceEquals(menu.State, profileOpen)).Layer.Reveal > .999);
            await ClickMenuAsync(Center(queueButton));
            if (profileOpen.Value || !queueOpen.Value) throw new InvalidOperationException("Opening a shell menu left another menu open.");
            CloseShellMenus(true);
            if (shellMenus.Any(menu => menu.Layer.IsVisible || menu.Layer.IsOpen)) throw new InvalidOperationException("Immediate dismissal left an interactive menu behind.");
            queueOpen.Value = true;
            await WaitForLikedLayoutAsync(() => shellMenus.Single(menu => ReferenceEquals(menu.State, queueOpen)).Layer.Reveal > .999);
            playerVisible.Value = false;
            if (queueOpen.Value || shellMenus.Single(menu => ReferenceEquals(menu.State, queueOpen)).Layer.IsVisible)
                throw new InvalidOperationException("Hiding the player left its queue drawer visible.");
            Console.WriteLine("UI_FLOATING_MENUS_OK: player-attached queue morph and gradient, grouped playback order, scroll, close/hide, 1000x680/1280x840/1600x900 bounds; profile click/focus dismissal, animated reversal and keyboard Escape");
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            Console.Error.WriteLine($"UI_FLOATING_MENUS_STATE: active={Window.IsActive}, workspace={workspace.IsVisible}/{workspace.IsEnabled}, header={startupHeader.Opacity}, queue={queueButton.Bounds}, player={playerChrome.ActualHeight}");
            Console.Error.WriteLine("UI_FLOATING_MENUS_FAILED: " + error);
        }
        finally { CloseShellMenus(true); Window.Close(); }
        static Point Center(FrameworkElement element) => new(element.Bounds.X + element.ActualWidth / 2, element.Bounds.Y + element.ActualHeight / 2);
    }

    private void RouteContextRightClick(Point point)
    {
        if (OperatingSystem.IsWindows())
        { SendLikedActionMouse(0x204, 2, point); SendLikedActionMouse(0x205, 0, point); return; }
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", true)!;
        var mouse = router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "MouseButton" && method.GetParameters().Length == 10);
        mouse.Invoke(null, [Window, point, Window.ClientToScreen(point), MouseButton.Right, true, false, true, false, 1, ModifierKeys.None]);
        mouse.Invoke(null, [Window, point, Window.ClientToScreen(point), MouseButton.Right, false, false, false, false, 1, ModifierKeys.None]);
    }

    private async Task VerifyPopupReopenAsync(Button trigger)
    {
        using var menu = PopupAppearance.CreateMenu();
        for (var index = 0; index < 8; index++) menu.Item("Действие " + (index + 1));
        var points = new[] { new Point(240, 180), new Point(310, 210), new Point(275, 195), new Point(800, 500), new Point(180, 260), new Point(330, 290) };
        ChatPopupLayer? layer = null;
        var intermediate = 0;
        var missedLiveFrame = false;
        long previousPaint = 0;
        void Frame()
        {
            if (menu.Reveal is not (> 0 and < 1)) return;
            intermediate++;
            var paint = menu.Card.PopupBackdropPaintVersion;
            if (layer?.HasGlassBackdrop != true || paint == previousPaint ||
                OperatingSystem.IsLinux() && !layer.HasGpuGlassOptics) missedLiveFrame = true;
            previousPaint = paint;
        }
        Window.FrameRendered += Frame;
        try
        {
            foreach (var point in points)
            {
                var painted = menu.Card.PopupBackdropPaintVersion;
                menu.Show(trigger, point);
                layer ??= (ChatPopupLayer)VisualTree.Find(menu, element => element is ChatPopupLayer)!;
                if (menu.Reveal != 0 || layer.HasGlassBackdrop)
                    throw new InvalidOperationException("Repositioned menu reused its reveal or old background before a new capture.");
                var frames = intermediate;
                await WaitForLikedLayoutAsync(() => menu.Reveal == 1 && layer.HasGlassBackdrop && menu.Card.PopupBackdropPaintVersion > painted);
                if (intermediate - frames < 2 || missedLiveFrame || layer.GlassCaptureBounds != layer.SurfaceBounds.Inflate(40, 40))
                    throw new InvalidOperationException("Menu reopen skipped intermediate frames, missed a live sample or retained a capture from another position.");
            }
            menu.Show(trigger, points[0]);
            await WaitForLikedLayoutAsync(() => menu.Reveal is > .1 and < .8);
            menu.Show(trigger, points[1]);
            if (menu.Reveal != 0 || layer!.HasGlassBackdrop)
                throw new InvalidOperationException("Rapid relocation retained an unfinished reveal or stale glass.");
            await WaitForLikedLayoutAsync(() => menu.Reveal == 1 && layer.HasGlassBackdrop);
            menu.Close();
            await WaitForLikedLayoutAsync(() => menu.Reveal is > .1 and < .9);
            var closing = menu.Reveal;
            menu.Show(trigger, points[1]);
            if (Math.Abs(menu.Reveal - closing) > .001)
                throw new InvalidOperationException("Reversing a close at the same position snapped its animation.");
            await WaitForLikedLayoutAsync(() => menu.Reveal == 1 && layer.HasGlassBackdrop);
            CaptureUiPreview("menu-context-reopened", menu);
            Console.WriteLine("POPUP_REOPEN_OK: fresh glass on every visible entrance frame, rapid relocation, and continuous close reversal");
        }
        finally { Window.FrameRendered -= Frame; menu.Close(true); }
    }
    private void VerifyMenuSurfaceAlpha(AnimatedContextMenu menu)
    {
        var scale = Window.DpiScale;
        var pixels = (int)Math.Ceiling(8 * scale);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(
            Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(pixels, pixels, scale, premultiplied: true));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent);
        using var viewport = WindowPresentation.CaptureViewport(surface);
        context.Translate(-menu.Card.Bounds.X - 16, -menu.Card.Bounds.Y - 8);
        menu.Render(context); context.EndFrame();
        var bytes = ((Aprillz.MewUI.Rendering.ICpuPixelSurface)surface).CopyPixels();
        var alpha = Enumerable.Range(0, bytes.Length / 4).Max(index => bytes[index * 4 + 3]);
        if (alpha == 0 || alpha > menu.Reveal * 255 + 8)
            throw new InvalidOperationException($"Menu backing does not fade with its content: alpha={alpha}, reveal={menu.Reveal:F3}.");
    }

}
