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
            if (element is Button { StyleName: "track-action" } button) buttons.Add(button);
        });
        if (buttons.Count != 2) throw new InvalidOperationException("Like/copy actions are missing.");
        var count = ((StackPanel)buttons[0].Content!).Children.OfType<TextBlock>().Single();
        if (count.FontWeight != FontWeight.Bold) throw new InvalidOperationException("Like count lost its bold weight.");
        if (!OperatingSystem.IsWindows()) return;
        if (!GetLikedActionCursor(out var originalCursor)) throw new InvalidOperationException("Could not save cursor position.");
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
                    var center = new Point(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
                    MoveLikedActionCursor(center);
                    SendLikedActionMouse(0x200, 0, center);
                    await WaitForLikedLayoutAsync(() => button.IsMouseOver && button.Background == Color.FromRgb(64, 64, 64));
                    if (button.BorderThickness != 0 || button.BorderBrush != Color.Transparent || button.Cursor != CursorType.Hand)
                        throw new InvalidOperationException("Action hover gained an outline or lost its hand cursor.");
                    CaptureUiPreview(button == buttons[0] ? "like-hover" : "copy-hover");
                    SendLikedActionMouse(0x201, 1, center);
                    await WaitForLikedLayoutAsync(() => button.IsPressed && button.Background == Color.FromRgb(86, 86, 86));
                    // Release outside the action so this visual check never invokes a like or clipboard write.
                    MoveLikedActionCursor(new Point(1, 1));
                    SendLikedActionMouse(0x200, 1, new Point(1, 1));
                    SendLikedActionMouse(0x202, 0, new Point(1, 1));
                    await WaitForLikedLayoutAsync(() => !button.IsPressed && !button.IsMouseOver && button.Background == Raised);
                }
                finally { button.IsEnabled = enabled; }
            }
            Console.WriteLine("UI_LIKED_ACTIONS_OK: bold counter; native mouse hover/press/release, animated backgrounds, no outline and hand cursor for like/copy");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Like/copy interaction failed: bounds={active?.Bounds}, " +
                $"hover={active?.IsMouseOver}, pressed={active?.IsPressed}, background={active?.Background}", error);
        }
        finally
        {
            Window.ReleaseMouseCapture(); timer.Start();
            if (!SetLikedActionCursor(originalCursor.X, originalCursor.Y)) throw new InvalidOperationException("Could not restore cursor position.");
        }
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
