using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyFloatingMenusAsync()
    {
        try
        {
            timer.Stop(); Window.Activate();
            playerVisible.Value = true;
            StartQueueContext(new("menu-fixture", "Понравившиеся треки", "Из подборки"), tracks.ToArray(), tracks[0]);
            RefreshQueue();
            await WaitForLikedLayoutAsync(() => Window.IsActive && workspace.IsVisible && startupHeader.Opacity > .999 &&
                queueButton.ActualWidth > 0 && playerChrome.ActualHeight >= PlayerBarHeight - 1);
            foreach (var (button, state, name) in new[] {
                (profileButton, profileOpen, "profile"), (queueButton, queueOpen, "queue") })
            {
                CloseShellMenus(true); MoveActionPointer(new Point(2, 2));
                var layer = shellMenus.Single(menu => ReferenceEquals(menu.State, state)).Layer;
                var opening = new List<double>(); var closing = new List<double>();
                var samples = opening;
                void Sample() => samples.Add(layer.Reveal);
                Window.FrameRendered += Sample;
                try
                {
                    RouteWaveformClick(Center(button));
                    await WaitForLikedLayoutAsync(() => layer.Reveal > .999 && layer.Card.ActualWidth > 0);
                    if (!state.Value || layer.Card.Background != chatComposer.Background || layer.Card.BorderThickness != 0 || layer.Card.CornerRadius != 14)
                        throw new InvalidOperationException(name + " menu lost its shared attachment-menu surface.");
                    CheckAnchor();
                    if (layer.HitTest(Center(button)) != null || layer.HitTest(new Point(2, 2)) != null)
                        throw new InvalidOperationException(name + " menu captures its trigger or the empty workspace.");
                    if (opening.Count(value => value > 0 && value < 1) < 3)
                        throw new InvalidOperationException(name + " menu opening snapped.");
                    CaptureUiPreview("menu-" + name);
                    MoveActionPointer(Center(layer.Card));
                    await Task.Delay(320, lifetime.Token);
                    if (!state.Value) throw new InvalidOperationException(name + " menu closed while its content was hovered.");
                    samples = closing; MoveActionPointer(new Point(2, 2));
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (state.Value || closing.Count(value => value > 0 && value < 1) < 3)
                        throw new InvalidOperationException(name + " menu did not fade out after leaving.");
                    MoveActionPointer(Center(button));
                    await WaitForLikedLayoutAsync(() => state.Value && layer.Reveal > .999);
                    // Crossing the small gap must not collapse the card.
                    var gapY = ReferenceEquals(state, queueOpen) ? button.Bounds.Y - 4 : button.Bounds.Bottom + 4;
                    MoveActionPointer(new Point(button.Bounds.X + button.ActualWidth / 2, gapY));
                    await Task.Delay(100, lifetime.Token);
                    if (!state.Value) throw new InvalidOperationException(name + " menu closed while crossing its gap.");
                    MoveActionPointer(Center(layer.Card));
                    foreach (var width in new[] { 1000, 1280 })
                    {
                        Window.WindowSize = WindowSize.Resizable(width, 840, minWidth: 1000, minHeight: 680);
                        await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - width) < 1 && Math.Abs(layer.ActualWidth - width) < 1 && !workspace.IsArrangeDirty && !layer.IsArrangeDirty);
                        CheckAnchor(); MoveActionPointer(Center(layer.Card));
                    }
                    state.Value = false; await Task.Delay(50, lifetime.Token);
                    var interrupted = layer.Reveal; state.Value = true;
                    if (Math.Abs(layer.Reveal - interrupted) > .0001)
                        throw new InvalidOperationException(name + " menu jumped when its dismissal was interrupted.");
                    await WaitForLikedLayoutAsync(() => layer.Reveal > .999);
                    var escape = new KeyEventArgs(Key.Escape, 0);
                    typeof(Window).GetMethod("RaisePreviewKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(Window, [escape]);
                    await WaitForLikedLayoutAsync(() => !layer.IsVisible);
                    if (!escape.Handled || state.Value) throw new InvalidOperationException(name + " menu did not handle Escape.");
                }
                finally { Window.FrameRendered -= Sample; }

                void CheckAnchor()
                {
                    var card = layer.Card.Bounds;
                    var gap = ReferenceEquals(state, queueOpen) ? button.Bounds.Y - card.Bottom : card.Y - button.Bounds.Bottom;
                    if (Math.Abs(card.Right - button.Bounds.Right) > 1 || Math.Abs(gap - 8) > 1 ||
                        card.X < 0 || card.Y < 0 || card.Right > Window.ClientSize.Width || card.Bottom > Window.ClientSize.Height)
                        throw new InvalidOperationException($"{name} menu lost its anchor after layout: card={card}, button={button.Bounds}.");
                }
            }
            MoveActionPointer(new Point(2, 2));
            profileOpen.Value = true;
            await WaitForLikedLayoutAsync(() => shellMenus.Single(menu => ReferenceEquals(menu.State, profileOpen)).Layer.Reveal > .999);
            queueOpen.Value = true;
            if (profileOpen.Value || !queueOpen.Value) throw new InvalidOperationException("Opening a shell menu left another menu open.");
            CloseShellMenus(true);
            if (shellMenus.Any(menu => menu.Layer.IsVisible || menu.Layer.IsOpen)) throw new InvalidOperationException("Immediate dismissal left an interactive menu behind.");
            Console.WriteLine("UI_FLOATING_MENUS_OK: queue/profile share the attachment surface, animated reveal/dismissal, trigger alignment, keyboard Escape, exclusive state and resize anchoring");
        }
        catch (Exception error)
        {
            VerificationFailure = error; Environment.ExitCode = 1;
            Console.Error.WriteLine("UI_FLOATING_MENUS_FAILED: " + error);
        }
        finally { CloseShellMenus(true); Window.Close(); }
        static Point Center(FrameworkElement element) => new(element.Bounds.X + element.ActualWidth / 2, element.Bounds.Y + element.ActualHeight / 2);
    }
}
