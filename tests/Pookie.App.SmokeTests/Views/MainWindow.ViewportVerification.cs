using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Native window managers can clamp requests to the monitor's work area,
    // including XWayland on a scaled display. Test the largest available size.
    private int VerificationWindowWidth(int requested)
        => VerificationWindowSize(requested, 0).Width;

    private (int Width, int Height) VerificationWindowSize(int requestedWidth, int requestedHeight)
    {
        var work = ScreenHelper.GetWorkAreaForPoint(Window.PointToScreen(new(Window.ClientSize.Width / 2, Window.ClientSize.Height / 2)));
        if (work.IsEmpty) return (requestedWidth, requestedHeight);
        var topLeft = Window.PointFromScreen(work.TopLeft);
        var bottomRight = Window.PointFromScreen(work.BottomRight);
        var width = Math.Min(requestedWidth, (int)Math.Floor(bottomRight.X - topLeft.X - 32));
        // The client area also needs room for the native title bar and borders.
        var height = Math.Min(requestedHeight, (int)Math.Floor(bottomRight.Y - topLeft.Y - 64));
        if (width < 760) throw new InvalidOperationException("Native UI verification requires a 760-DIP viewport.");
        if (width != requestedWidth || height != requestedHeight)
            Console.WriteLine($"UI_VIEWPORT_LIMIT: requested={requestedWidth}x{requestedHeight} actual={width}x{height} DPI={Window.Dpi}");
        return (width, height);
    }
}
