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
            if (element is Button button && button.StyleName is TrackButtons.Tonal or TrackButtons.Selected or TrackButtons.Reposted) buttons.Add(button);
        });
        if (buttons.Count != 4) throw new InvalidOperationException("Like/repost/playlist/copy actions are missing.");
        var count = ((StackPanel)buttons[0].Content!).Children.OfType<TextBlock>().Single();
        if (count.FontWeight != FontWeight.Bold) throw new InvalidOperationException("Like count lost its bold weight.");
        var originalCursor = default(LikedActionPoint);
        if (OperatingSystem.IsWindows() && !GetLikedActionCursor(out originalCursor)) throw new InvalidOperationException("Could not save cursor position.");
        CaptureUiPreview("list-before-actions");
        Window.Activate();
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
                    var surface = button.StyleName == TrackButtons.Selected ? TrackButtons.SelectedSurface : button.StyleName == TrackButtons.Reposted ? TrackButtons.RepostedSurface : TrackButtons.TonalSurface;
                    var foreground = button.StyleName == TrackButtons.Selected ? TrackButtons.OnSelected : button.StyleName == TrackButtons.Reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface;
                    var center = new Point(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
                    MoveActionPointer(center);
                    await WaitForLikedLayoutAsync(() => button.IsMouseOver && button.Background == TrackButtons.HoverColor(surface, foreground));
                    if (button.BorderThickness != 0 || button.CornerRadius != 20 || Math.Abs(button.ActualHeight - 40) > 1 / Window.DpiScale || button.Cursor != CursorType.Hand)
                        throw new InvalidOperationException("Material action lost its pill shape, size or hand cursor.");
                    CaptureUiPreview(button == buttons[0] ? "like-hover" : button == buttons[1] ? "repost-hover" : button == buttons[2] ? "playlist-hover" : "copy-hover");
                    PressActionPointer(center, true);
                    await WaitForLikedLayoutAsync(() => button.IsPressed && button.Background == TrackButtons.PressedColor(surface, foreground));
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

    private void MoveLikedActionCursor(Point clientPoint)
    {
        var screen = Window.ClientToScreen(clientPoint);
        if (!SetLikedActionCursor((int)Math.Round(screen.X), (int)Math.Round(screen.Y)))
            throw new InvalidOperationException("Could not move cursor into the test window.");
    }

    private void SendLikedActionMouse(uint message, nuint buttons, Point position)
    {
        var scale = Window.DpiScale;
        var x = (int)Math.Round(position.X * scale);
        var y = (int)Math.Round(position.Y * scale);
        if (!PostLikedActionMessage(Window.Handle, message, buttons, (nint)((y << 16) | (x & 0xffff))))
            throw new InvalidOperationException("Could not deliver native action input.");
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostLikedActionMessage(nint window, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential)] private struct LikedActionPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool GetLikedActionCursor(out LikedActionPoint point);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool SetLikedActionCursor(int x, int y);
}
