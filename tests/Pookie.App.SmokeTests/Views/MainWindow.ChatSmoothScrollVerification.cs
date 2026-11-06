using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatSmoothScrollAsync(ScrollViewer viewer)
    {
        await WaitForLikedLayoutAsync(() => smoothScrolls.ContainsKey(viewer));
        var motion = smoothScrolls[viewer];
        var start = viewer.VerticalOffset;
        var startGap = SmoothScroll.Maximum(viewer) - start;
        var offsets = new HashSet<double>();
        var trace = new Queue<string>();
        void Sample()
        {
            offsets.Add(Math.Round(viewer.VerticalOffset, 2));
            trace.Enqueue($"offset={viewer.VerticalOffset:F2}, max={SmoothScroll.Maximum(viewer):F2}, motion={motion.IsRunning}, bottom={chatScrollBottom}/{chatWasAtBottom}/{chatFollowLayout}, restore={chatRestoreOffset}, prepend={chatPrependAnchor}, composer={chatComposerSurface.IsResizing}");
            if (trace.Count > 50) trace.Dequeue();
        }
        Window.FrameRendered += Sample;
        try
        {
            var point = new Point(viewer.Bounds.X + viewer.ActualWidth * .5, viewer.Bounds.Y + viewer.ActualHeight * .5);
            RouteSearchTestWheel(point, 3);
            if (Math.Abs(viewer.VerticalOffset - start) > .1 || chatScrollBottom || chatFollowLayout || chatWasAtBottom)
                throw new InvalidOperationException("Chat wheel skipped its animation or retained automatic bottom-follow intent.");
            // History may prepend while the gesture is running. Compare the
            // reading position relative to the end, not a now-shifted offset.
            await WaitForLikedLayoutAsync(() => !motion.IsRunning && chatRestoreOffset == null &&
                SmoothScroll.Maximum(viewer) - viewer.VerticalOffset > startGap + 20);
            if (Math.Abs(SmoothScroll.Maximum(viewer) - viewer.VerticalOffset - startGap - 3 * ThemeManager.DefaultMetrics.ScrollWheelStep) > 2)
                throw new InvalidOperationException("Chat history pagination changed the gesture's remaining scroll distance.");
            if (offsets.Count < 4)
                throw new InvalidOperationException("Chat scrolling did not render intermediate positions.");
            var reverseGap = SmoothScroll.Maximum(viewer) - viewer.VerticalOffset;
            RouteSearchTestWheel(point, -.25);
            await WaitForLikedLayoutAsync(() => !motion.IsRunning && chatRestoreOffset == null &&
                SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < reverseGap - 1);
            if (chatScrollBottom || chatFollowLayout)
                throw new InvalidOperationException("Touchpad interpolation re-enabled chat automatic bottom-follow.");
            Console.WriteLine($"CHAT_SMOOTH_SCROLL_OK: routed wheel and fractional touchpad input, {offsets.Count} distinct rendered offsets, bottom-follow remains canceled while reading");
        }
        catch { Console.Error.WriteLine("CHAT_SMOOTH_TRACE: " + string.Join("; ", trace)); throw; }
        finally { Window.FrameRendered -= Sample; motion.Stop(); RequestChatBottom(); }
        await WaitForLikedLayoutAsync(() => !chatScrollBottom && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 10);
    }
}
