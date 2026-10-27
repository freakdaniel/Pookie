using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLikedActionHoverAsync(LikedTrackRow row)
    {
        var buttons = new List<Button>();
        VisualTree.Visit(row.Root, element =>
        {
            if (element is Button button && button.StyleName is TrackButtons.Glass or TrackButtons.Selected or TrackButtons.Reposted) buttons.Add(button);
        });
        if (buttons.Count != 4) throw new InvalidOperationException("Like/repost/playlist/copy actions are missing.");
        if (buttons[0] is not MaterialTrackButton { IsConnected: true } || buttons[1] is not MaterialTrackButton { IsConnected: true } ||
            Math.Abs(buttons[1].Bounds.X - buttons[0].Bounds.Right - 2) > 1)
            throw new InvalidOperationException("List like/repost buttons did not form a connected pair.");
        var count = ((StackPanel)buttons[0].Content!).Children.OfType<TextBlock>().Single();
        if (count.FontWeight != FontWeight.Bold) throw new InvalidOperationException("Like count lost its bold weight.");
        var originalCursor = default(LikedActionPoint);
        if (OperatingSystem.IsWindows() && !GetLikedActionCursor(out originalCursor)) throw new InvalidOperationException("Could not save cursor position.");
        if (OperatingSystem.IsWindows())
        {
            ProbeShowWindow(Window.Handle, 5);
            if (!ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53)) throw new InvalidOperationException("Could not expose native action check.");
            await WaitForLoginFrameAsync();
        }
        CaptureUiPreview("list-before-actions");
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive);
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && Window.Content is FrameworkElement root &&
            Math.Abs(root.ActualHeight - Window.ClientSize.Height) < 2);
        // A realized row can belong to the prefetch band. Showing the native
        // window may also reduce the client height; move the tested row into
        // the actual viewport before delivering physical pointer input.
        var visibleTop = Math.Max(headerGlass.Bounds.Bottom + 16, shellScroll.Bounds.Y + 16);
        var visibleBottom = Math.Min(shellScroll.Bounds.Bottom, Window.ClientSize.Height - PlayerBarHeight) - 16;
        if (row.Root.Bounds.Y < visibleTop || row.Root.Bounds.Bottom > visibleBottom)
        {
            shellScroll.SetScrollOffsets(0, shellScroll.VerticalOffset + row.Root.Bounds.Y - visibleTop);
            await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady && !row.Root.IsArrangeDirty &&
                row.Root.Bounds.Y >= visibleTop - 1 && row.Root.Bounds.Bottom <= visibleBottom + 1);
        }
        timer.Stop();
        Button? active = null;
        try
        {
            foreach (var button in buttons)
            {
                active = button;
                var enabled = button.IsEnabled;
                button.IsEnabled = true;
                try
                {
                    Window.FocusManager.ClearFocus();
                    var surface = TrackButtons.Surface(button.StyleName!);
                    var foreground = TrackButtons.Foreground(button.StyleName!);
                    var center = new Point(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
                    MoveActionPointer(center);
                    await WaitForLikedLayoutAsync(() => button.IsMouseOver && button.Background == TrackButtons.HoverColor(surface, foreground));
                    if (button.BorderThickness != 0 || button.CornerRadius != TrackRowLayout.ActionSize / 2 || Math.Abs(button.ActualHeight - TrackRowLayout.ActionSize) > 1 / Window.DpiScale || button.Cursor != CursorType.Hand)
                        throw new InvalidOperationException("Material action lost its pill shape, size or hand cursor.");
                    CaptureUiPreview(button == buttons[0] ? "like-hover" : button == buttons[1] ? "repost-hover" : button == buttons[2] ? "playlist-hover" : "copy-hover");
                    var connected = button is MaterialTrackButton { IsConnected: true };
                    var pressedSurface = connected ? TrackButtons.HoverColor(surface, foreground) : TrackButtons.PressedColor(surface, foreground);
                    var cornersBeforePress = ((MaterialTrackButton)button).ConnectedCorners;
                    var pressStarted = Environment.TickCount64;
                    PressActionPointer(center, true);
                    // X11 capture can queue a native enter/leave for the physical cursor,
                    // which differs from the fixture's routed pointer. Keep the held
                    // pointer over this button while its state transition settles.
                    await WaitForLikedLayoutAsync(() =>
                    {
                        if (!OperatingSystem.IsWindows()) RouteActionHeldPointer(center);
                        return button.IsPressed && button.Background == pressedSurface && (!connected || Environment.TickCount64 - pressStarted >= 240);
                    });
                    if (connected && ((MaterialTrackButton)button).ConnectedCorners != cornersBeforePress)
                        throw new InvalidOperationException("Connected action introduced an intermediate pressed shape.");
                    // Release outside the action so this visual check never invokes a like or clipboard write.
                    MoveActionPointer(new Point(1, 1));
                    PressActionPointer(new Point(1, 1), false);
                    Window.FocusManager.ClearFocus();
                    await WaitForLikedLayoutAsync(() => !button.IsPressed && !button.IsMouseOver && button.Background == surface);
                    button.Focus();
                    await WaitForLikedLayoutAsync(() => button.IsFocused && button.Background == surface);
                    if (button.BorderThickness != 0) throw new InvalidOperationException("Focused action introduced an outline.");
                    Window.FocusManager.ClearFocus();
                }
                finally { button.IsEnabled = enabled; }
            }
            Console.WriteLine("UI_LIKED_ACTIONS_OK: Material 3 pill/icon geometry, selected like, hover/press/release state layers and keyboard focus for like/copy");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Like/copy interaction failed: bounds={active?.Bounds}, " +
                $"enabled={active?.IsEnabled}, hover={active?.IsMouseOver}, pressed={active?.IsPressed}, background={active?.Background}", error);
        }
        finally
        {
            if (OperatingSystem.IsWindows()) ProbeSetWindowPos(Window.Handle, -2, 0, 0, 0, 0, 0x13);
            Window.ReleaseMouseCapture(); timer.Start();
            Window.FocusManager.ClearFocus();
            if (OperatingSystem.IsWindows() && !SetLikedActionCursor(originalCursor.X, originalCursor.Y)) throw new InvalidOperationException("Could not restore cursor position.");
        }
    }

    private void MoveActionPointer(Point point)
    {
        if (OperatingSystem.IsWindows()) { MoveLikedActionCursor(point); SendLikedActionMouse(0x200, 0, point); }
        else RouteWaveformPointer(point);
    }

    private void PressActionPointer(Point point, bool pressed)
    {
        if (OperatingSystem.IsWindows()) { SendLikedActionMouse(pressed ? 0x201u : 0x202u, pressed ? 1u : 0u, point); return; }
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
        router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "MouseButton" && method.GetParameters().Length == 10)
            .Invoke(null, [Window, point, new Point(0, 0), MouseButton.Left, pressed, pressed, false, false, 1, ModifierKeys.None]);
    }

    private void RouteActionHeldPointer(Point point)
    {
        var router = typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", throwOnError: true)!;
        router.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "MouseMove" && method.GetParameters().Length == 7)
            .Invoke(null, [Window, point, Window.ClientToScreen(point), true, false, false, ModifierKeys.None]);
    }

    private void MoveLikedActionCursor(Point clientPoint)
    {
        var screen = Window.ClientToScreen(clientPoint);
        if (!SetLikedActionCursor((int)Math.Round(screen.X), (int)Math.Round(screen.Y)))
            throw new InvalidOperationException($"Could not move cursor into the test window: client={clientPoint}, screen={screen}, dpi={Window.DpiScale}, active={Window.IsActive}.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }

    private void SendLikedActionMouse(uint message, nuint buttons, Point position)
    {
        var scale = Window.DpiScale;
        var x = (int)Math.Round(position.X * scale);
        var y = (int)Math.Round(position.Y * scale);
        // Return after the native window has handled the input. An immediate
        // assertion must not race a mouse message still waiting in the queue.
        if (SendLikedActionMessage(Window.Handle, message, buttons, (nint)((y << 16) | (x & 0xffff)), 2, 1000, out _) == 0)
            throw new InvalidOperationException("Could not deliver native action input.");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendLikedActionMessage(nint window, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostLikedActionMessage(nint window, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential)] private struct LikedActionPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool GetLikedActionCursor(out LikedActionPoint point);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos", SetLastError = true)] private static extern bool SetLikedActionCursor(int x, int y);
}
