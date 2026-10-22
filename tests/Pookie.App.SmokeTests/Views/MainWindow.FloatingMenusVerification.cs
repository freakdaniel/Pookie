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
                    RouteWaveformClick(Center(button));
                    await WaitForLikedLayoutAsync(() => layer.Reveal > .999 && layer.Card.ActualWidth > 0);
                    if (!state.Value || layer.Card.BorderThickness != 0 ||
                        layer is FloatingMenuLayer && (layer.Card.Background != chatComposer.Background || layer.Card.CornerRadius != 14))
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
                    RouteWaveformClick(new Point(layer.Card.Bounds.X + 20, layer.Card.Bounds.Y + 20));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.IsFocusWithin);
                    samples = closing;
                    RouteWaveformClick(Center(button));
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (state.Value || closing.Count(value => value > 0 && value < 1) < 3)
                        throw new InvalidOperationException(name + " menu did not fade out after clicking its trigger again.");
                    MoveActionPointer(Center(button));
                    await Task.Delay(350, lifetime.Token);
                    if (state.Value) throw new InvalidOperationException(name + " menu reopened on hover after being toggled off.");
                    RouteWaveformClick(Center(button));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.Reveal > .999);
                    RouteWaveformClick(new Point(2, 2));
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (state.Value) throw new InvalidOperationException(name + " menu did not dismiss after an outside click.");
                    RouteWaveformClick(Center(button));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.Reveal > .999);
                    settingsButton.Focus();
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (state.Value) throw new InvalidOperationException(name + " menu retained focus after focus moved to another control.");
                    RouteWaveformClick(Center(button));
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
                        RouteWaveformClick(Center(button));
                        await WaitForLikedLayoutAsync(() => layer.Reveal > .999);
                        var close = (Button)VisualTree.Find(layer.Card, element => element is Button { Content: Image })!;
                        RouteWaveformClick(Center(close));
                        await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                        if (state.Value) throw new InvalidOperationException("The queue drawer close button did not dismiss it.");
                    }
                    RouteWaveformClick(Center(button));
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
                            card.Width != 460 || card.Y < startupHeader.Bounds.Bottom || card.Right > Window.ClientSize.Width + .5)
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
            RouteWaveformClick(Center(profileButton));
            await WaitForLikedLayoutAsync(() => shellMenus.Single(menu => ReferenceEquals(menu.State, profileOpen)).Layer.Reveal > .999);
            RouteWaveformClick(Center(queueButton));
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
}
