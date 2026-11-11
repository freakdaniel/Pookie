using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Native window managers can clamp requests to the monitor's work area,
    // including XWayland on a scaled display. Test the largest available size.
    private int VerificationWindowWidth(int requested)
    {
        var work = ScreenHelper.GetWorkAreaForPoint(Window.PointToScreen(new(Window.ClientSize.Width / 2, Window.ClientSize.Height / 2)));
        if (work.Width <= 0) return requested;
        var available = Window.PointFromScreen(work.TopRight).X - Window.PointFromScreen(work.TopLeft).X;
        var actual = Math.Min(requested, (int)Math.Floor(available - 32));
        if (actual < 760) throw new InvalidOperationException("Native UI verification requires a 760-DIP viewport.");
        if (actual != requested) Console.WriteLine($"UI_VIEWPORT_LIMIT: requested={requested} actual={actual} workArea={available:F1} DPI={Window.Dpi}");
        return actual;
    }
}
