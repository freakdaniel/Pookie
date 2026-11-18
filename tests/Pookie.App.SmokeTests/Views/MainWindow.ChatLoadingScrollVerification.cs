using Aprillz.MewUI.Controls;
using System.Runtime.InteropServices;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatLoadingScrollAsync(ChatFixtureTransport transport, ScrollViewer viewer)
    {
        RequestChatBottom();
        await WaitForLikedLayoutAsync(() => !chatScrollBottom && !chatWindowBusy && !chatThreadBusy);
        var stationaryPoint = new Aprillz.MewUI.Point(viewer.Bounds.Center.X, viewer.Bounds.Center.Y);
        MoveActionPointer(stationaryPoint);
        RouteSearchTestWheel(stationaryPoint, 3);
        if (ChatIsAtBottom())
            throw new InvalidOperationException("An upward wheel gesture re-enabled bottom following before its first animation frame.");
        CaptureChatPanelAnchor();
        if (chatFollowLayout || chatScrollBottom)
            throw new InvalidOperationException("A media/layout change recaptured bottom-follow while the user was scrolling up.");
        await WaitForLikedLayoutAsync(() => !smoothScrolls[viewer].IsRunning && chatRestoreOffset == null);
        RequestChatBottom();
        await WaitForLikedLayoutAsync(() => !chatScrollBottom);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            SendStationaryChatWheel(stationaryPoint, 3);
            await WaitForLikedLayoutAsync(() => !smoothScrolls[viewer].IsRunning && chatRestoreOffset == null &&
                SmoothScroll.Maximum(viewer) - viewer.VerticalOffset > 60);
            SendStationaryChatWheel(stationaryPoint, -100);
            await WaitForLikedLayoutAsync(() => !smoothScrolls[viewer].IsRunning && chatRestoreOffset == null &&
                SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 2);
        }
        Console.WriteLine("CHAT_STATIONARY_WHEEL_OK: repeated upward/downward wheel gestures at one fixed pointer position");
        var refresh = Task.CompletedTask;
        var trace = new Queue<string>();
        void Trace()
        {
            trace.Enqueue($"offset={viewer.VerticalOffset:F1} max={SmoothScroll.Maximum(viewer):F1} running={smoothScrolls[viewer].IsRunning} bottom={chatScrollBottom}/{chatWasAtBottom}/{chatFollowLayout} restore={chatRestoreAnchor}/{chatRestoreOffset} animate={chatAnimateBottom} resizes={string.Join(',', ChatRows.Where(r => r.Bound && r.Content.IsResizing).Select(r => $"{r.Message?.Id}:{r.Content.ResizeAnimationRunning}:{r.Root.Bounds.Y:F0}"))}");
            if (trace.Count > 24) trace.Dequeue();
        }
        WindowPresentation.FramePresented(Window) += Trace;
        try
        {
            BeginChatUserScroll();
            var fractional = SmoothScroll.Maximum(viewer) - 120.37;
            smoothScrolls[viewer].ScrollTo(fractional);
            await WaitForLikedLayoutAsync(() => !smoothScrolls[viewer].IsRunning);
            // Extent refinement carries the destination with its reading
            // anchor. Completion, rather than the old absolute coordinate,
            // is the invariant for a virtualized fractional destination.
            if (!double.IsFinite(viewer.VerticalOffset))
                throw new InvalidOperationException("Fractional destination produced an invalid offset.");
            RequestChatBottom();
            await WaitForLikedLayoutAsync(() => !chatScrollBottom);
            transport.DelayThread = true; transport.ThreadReady = null;
            refresh = RefreshChatThreadAsync();
            await WaitForLikedLayoutAsync(() => transport.ThreadReady != null);
            var point = new Aprillz.MewUI.Point(viewer.Bounds.Center.X, viewer.Bounds.Center.Y);
            RouteSearchTestWheel(point, 4);
            await WaitForLikedLayoutAsync(() => !smoothScrolls[viewer].IsRunning && !chatScrollBottom &&
                SmoothScroll.Maximum(viewer) - viewer.VerticalOffset > 100);
            var anchor = ReadChatReadingAnchor(viewer)!.Value;
            transport.ThreadReady!.TrySetResult(); await refresh;
            await WaitForLikedLayoutAsync(() => !chatThreadBusy && chatRestoreOffset == null);
            var row = ChatRows.FirstOrDefault(row => row.Bound && row.Message?.Id == anchor.Id);
            if (row == null || Math.Abs(row.Bubble.Bounds.Y - viewer.Bounds.Y - anchor.Y) > 1.5 || chatScrollBottom)
                throw new InvalidOperationException("A delayed poll pulled the user back down after upward scrolling.");

            // A geometry correction must carry the remaining wheel distance,
            // including root anchors used by media and drawer layout changes.
            CaptureChatPanelAnchor();
            var before = ReadChatReadingAnchor(viewer)!.Value;
            var growing = ChatRows.Where(row => row.Bound && row.Message != null && row.Attachments.Children.Count == 0 &&
                row.Root.Bounds.Y <= viewer.Bounds.Y).OrderByDescending(row => row.Root.Bounds.Y).First();
            growing.Content.AnimateMediaChange();
            growing.Attachments.IsVisible = true;
            growing.Attachments.Add(new Border().Width(80).Height(140));
            // Real media can leave the saved bubble coordinate between physical
            // pixels. A running resize must still yield to rendering/input even
            // when the native viewport cannot represent that exact coordinate.
            chatRestoreAnchorY += .28 / Window.DpiScale;
            RouteSearchTestWheel(point, 2);
            for (var pass = 0; pass < 4; pass++) Window.PerformLayout();
            if (chatRestorePasses > 128)
            {
                var passes = chatRestorePasses;
                CancelChatScrollRestoration(); growing.Content.StopResize();
                growing.Attachments.Clear(); growing.Attachments.IsVisible = false;
                throw new InvalidOperationException($"Chat anchor restoration monopolized layout during an animation: {passes} passes without presenting a frame.");
            }
            await WaitForLikedLayoutAsync(() => !smoothScrolls[viewer].IsRunning && chatRestoreOffset == null);
            row = ChatRows.FirstOrDefault(row => row.Bound && row.Message?.Id == before.Id);
            if (row == null || Math.Abs(row.Bubble.Bounds.Y - viewer.Bounds.Y - before.Y -
                2 * Aprillz.MewUI.ThemeManager.DefaultMetrics.ScrollWheelStep) > 2)
                throw new InvalidOperationException("A reading anchor consumed the upward wheel gesture.");

            CaptureChatPanelAnchor();
            growing.Content.AnimateMediaChange(); growing.Attachments.Clear(); growing.Attachments.IsVisible = false;
            await WaitForLikedLayoutAsync(() => !growing.Content.IsResizing && chatRestoreOffset == null);
            var offsets = new HashSet<double>();
            void Frame() => offsets.Add(Math.Round(viewer.VerticalOffset, 1));
            Window.FrameRendered += Frame;
            try
            {
                var active = chatInboxBindings.First(pair => pair.Value.Recipient.Id == chatRecipient!.Id).Key;
                RouteWaveformClick(SettingsCenter(active));
                await WaitForLikedLayoutAsync(() => !chatScrollBottom && !chatJumpingLatest &&
                    !chatThreadBusy && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 2);
                if (offsets.Count < 4) throw new InvalidOperationException("Repeated active-chat click snapped instead of scrolling smoothly.");
            }
            finally { Window.FrameRendered -= Frame; }
            transport.DelayThread = true; transport.ThreadReady = null;
            refresh = RefreshChatThreadAsync();
            await WaitForLikedLayoutAsync(() => transport.ThreadReady != null);
            await SelectChatAsync(chatRecipient!);
            await WaitForLikedLayoutAsync(() => !chatJumpingLatest && !chatThreadBusy);

            // A newer user gesture cancels both the jump and its held read.
            BeginChatUserScroll(); smoothScrolls[viewer].Stop();
            viewer.SetScrollOffsets(0, Math.Max(0, SmoothScroll.Maximum(viewer) - 300));
            transport.DelayThread = true; transport.ThreadReady = null;
            var jumping = SelectChatAsync(chatRecipient!);
            await WaitForLikedLayoutAsync(() => transport.ThreadReady != null);
            RouteSearchTestWheel(point, 4);
            await jumping;
            await WaitForLikedLayoutAsync(() => !chatJumpingLatest && !chatScrollBottom && !smoothScrolls[viewer].IsRunning);
            if (SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 100)
                throw new InvalidOperationException("Latest-message animation ignored a new upward gesture.");
            transport.ThreadReady?.TrySetResult();

            // Repeat-click must also leave an older bounded window, not just
            // scroll to the end of the currently displayed slice.
            var lastId = chatMessages[^1].Id;
            chatMessages.RemoveRange(chatMessages.Count - 10, 10); chatWindowHasNewer = true;
            RefreshChatTimeline(); viewer.SetScrollOffsets(0, 0);
            await SelectChatAsync(chatRecipient!);
            await WaitForLikedLayoutAsync(() => !chatScrollBottom && !chatJumpingLatest &&
                chatMessages[^1].Id == lastId && SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 2);
            Console.WriteLine("CHAT_LOADING_SCROLL_OK: fractional target settles; delayed poll and resizing media preserve upward input; active inbox click restores latest window smoothly; new input cancels pending jump");
        }
        catch { Trace(); Console.Error.WriteLine("CHAT_LOADING_SCROLL_TRACE: " + string.Join("; ", trace)); throw; }
        finally
        {
            WindowPresentation.FramePresented(Window) -= Trace;
            transport.ThreadReady?.TrySetResult(); await refresh;
            transport.DelayThread = false;
            RequestChatBottom();
        }
    }

    private void SendStationaryChatWheel(Aprillz.MewUI.Point point, int notches)
    {
        if (!OperatingSystem.IsLinux()) { RouteSearchTestWheel(point, notches); return; }
        SendX11Button(point, notches > 0 ? 4u : 5u, Math.Abs(notches));
    }

    private void SendX11Button(Aprillz.MewUI.Point point, uint detail, int count = 1)
    {
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No display for native mouse verification.");
        var buffer = Marshal.AllocHGlobal(24 * IntPtr.Size);
        try
        {
            var screen = Window.ClientToScreen(point);
            var button = new LinuxSearchInput.XKeyEvent
            {
                Display = display, Window = Window.Handle, Root = XDefaultRootWindow(display),
                X = (int)Math.Round(point.X * Window.DpiScale), Y = (int)Math.Round(point.Y * Window.DpiScale),
                RootX = (int)Math.Round(screen.X), RootY = (int)Math.Round(screen.Y),
                KeyCode = detail, SameScreen = 1
            };
            for (var i = 0; i < count; i++)
            {
                button.Type = 4; Marshal.StructureToPtr(button, buffer, false);
                if (XSendEvent(display, Window.Handle, 0, 4, buffer) == 0) throw new InvalidOperationException("Native wheel press was not sent.");
                button.Type = 5; Marshal.StructureToPtr(button, buffer, false);
                XSendEvent(display, Window.Handle, 0, 8, buffer);
            }
            XFlush(display);
        }
        finally { Marshal.FreeHGlobal(buffer); XCloseDisplay(display); }
    }
}
