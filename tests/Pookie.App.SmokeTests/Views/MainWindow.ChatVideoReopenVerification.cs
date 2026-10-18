using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyChatVideoDirtyClip()
    {
        using var overlay = new ChatVideoSurface(new Aprillz.MewUI.Controls.Grid()).Initialize();
        _ = overlay.SetOpen(true, immediate: true);
        overlay.Measure(new Size(32, 32)); overlay.Arrange(new Rect(0, 0, 32, 32));
        using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(32, 32));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        context.FillRectangle(new Rect(0, 0, 32, 32), Color.FromRgb(100, 100, 100));
        context.Save(); context.SetClip(new Rect(8, 8, 8, 8));
        overlay.Render(context); context.Restore(); context.EndFrame();
        var pixels = ((Aprillz.MewUI.Rendering.ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        if (pixels[0] != 100 || pixels[1] != 100 || pixels[2] != 100)
            throw new InvalidOperationException($"Video scrim escaped a partial repaint clip: outside RGB={pixels[2]},{pixels[1]},{pixels[0]} (expected 100,100,100).");
    }
    private async Task VerifyChatVideoPlaybackBackdropAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var info = await ChatVideoProbe.ReadAsync(bytes, lifetime.Token);
        var file = new ChatAttachment(Path.GetFileName(path), "video/quicktime", bytes) { Video = info };
        for (var pass = 0; pass < 3; pass++)
        {
            var originalSize = Window.WindowSize;
            if (pass == 2)
            {
                Window.WindowSize = WindowSize.Resizable(4096, 2100, minWidth: 760, minHeight: 600);
                await WaitForLikedLayoutAsync(() => Window.ClientSize.Width > 3000);
            }
            await WaitForLoginFrameAsync(); await Task.Delay(100); RememberNativeChatVideoBackdrop();
            await OpenChatVideoAsync(file, "local-playback-verification", 0);
            await WaitForLikedLayoutAsync(() => chatVideoVersion > 0 && chatVideoSurface.Reveal > .999);
            var display = OperatingSystem.IsLinux() ? XOpenDisplay(0) : 0;
            var frames = 0; string? failure = null;
            void Sample()
            {
                frames++;
                if (display == 0) return;
                foreach (var point in ChatVideoBackdropPoints())
                {
                    var pixel = ReadNativeChatVideoPixel(display, point);
                    var underlay = chatVideoNativeUnderlay[point];
                    var darkened = true;
                    for (var shift = 0; shift <= 16; shift += 8)
                    {
                        var minimum = Math.Floor(((underlay >> shift) & 255) * (255 - ChatVideoSurface.NormalBackdropAlpha) / 255d);
                        darkened &= (double)((pixel >> shift) & 255) < minimum - 1;
                    }
                    if (!darkened) continue;
                    // A live conversation can finish loading media behind the scrim.
                    // Distinguish that legitimate change from an extra layer of black.
                    var currentUnderlay = ReadChatVideoUnderlayPixel(point);
                    for (var shift = 0; shift <= 16; shift += 8)
                        darkened &= (double)((pixel >> shift) & 255) < Math.Floor(((currentUnderlay >> shift) & 255) * (255 - ChatVideoSurface.NormalBackdropAlpha) / 255d) - 2;
                    if (darkened)
                        failure ??= $"Playback backdrop flashed black: pass={pass}, frame={frames}, time={chatVideoSession?.Snapshot().Time}, reveal={chatVideoSurface.Reveal}, expansion={chatVideoExpansion}, point={point}, pixel=#{pixel:x6}, underlay=#{currentUnderlay:x6}.";
                }
            }
            Window.FrameRendered += Sample;
            try
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
                var interaction = 0; var nextInteraction = DateTimeOffset.UtcNow;
                while (chatVideoSession?.Snapshot().Ended != true && DateTimeOffset.UtcNow < deadline && failure == null)
                {
                    if (pass > 0 && DateTimeOffset.UtcNow >= nextInteraction)
                    {
                        var element = interaction++ % 3 == 0 ? chatVideoGear.Bounds : interaction % 3 == 1 ? chatVideoVolume.Bounds : chatVideoImage.Bounds;
                        MoveActionPointer(new Point(element.X + element.Width / 2, element.Y + element.Height / 2));
                        if (pass == 2 && browser == null && interaction % 4 == 0) await RefreshChatsAsync();
                        nextInteraction = DateTimeOffset.UtcNow.AddMilliseconds(420);
                    }
                    await Task.Delay(25, lifetime.Token);
                }
                if (failure != null) { CaptureUiPreview("chats-video-black-flash"); throw new InvalidOperationException(failure); }
                if (chatVideoSession?.Snapshot().Ended != true || frames < 100)
                    throw new InvalidOperationException($"Playback backdrop verification did not exercise a full video: frames={frames}, state={chatVideoSession?.Snapshot()}.");
                Console.WriteLine($"VIDEO_BACKDROP_OK: pass={pass}, frames={frames}, duration={chatVideoSession.Snapshot().Length}");
            }
            finally
            {
                Window.FrameRendered -= Sample; if (display != 0) XCloseDisplay(display);
                CloseChatVideo(); await chatVideoClosing;
                Window.WindowSize = originalSize;
            }
        }
    }
    private nuint ReadChatVideoUnderlayPixel(Point point)
    {
        using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(1, 1, Window.DpiScale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Translate(-point.X, -point.Y);
        context.FillRectangle(new Rect(0, 0, Window.ClientSize.Width, Window.ClientSize.Height), Window.Background);
        foreach (var child in ((Aprillz.MewUI.Controls.Grid)Window.Content!).Children)
            if (!ReferenceEquals(child, chatVideoSurface)) child.Render(context);
        context.EndFrame();
        var pixels = ((Aprillz.MewUI.Rendering.ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        return (nuint)(pixels[2] << 16 | pixels[1] << 8 | pixels[0]);
    }
    private async Task ObserveChatVideoOpeningAsync(Func<Task> open)
    {
        if (!OperatingSystem.IsLinux()) { await open(); return; }
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for viewer animation verification.");
        var frames = new List<(double Reveal, Point Point, nuint Pixel)>();
        void Sample()
        {
            foreach (var point in ChatVideoBackdropPoints())
                frames.Add((chatVideoSurface.Reveal, point, ReadNativeChatVideoPixel(display, point)));
        }
        Window.FrameRendered += Sample;
        try
        {
            await open();
            await WaitForLikedLayoutAsync(() => chatVideoSurface.Reveal > .999);
            await WaitForLoginFrameAsync();
        }
        finally { Window.FrameRendered -= Sample; XCloseDisplay(display); }
        if (!frames.Any(f => f.Reveal is > 0 and < 1)) throw new InvalidOperationException("Viewer opening produced no intermediate native frame.");
        foreach (var frame in frames)
        for (var shift = 0; shift <= 16; shift += 8)
        {
            var underlay = (chatVideoNativeUnderlay[frame.Point] >> shift) & 255;
            var minimum = Math.Floor(underlay * (255 - ChatVideoSurface.NormalBackdropAlpha) / 255d);
            if ((double)((frame.Pixel >> shift) & 255) < minimum - 1)
                throw new InvalidOperationException($"Viewer flashed dark during opening: reveal={frame.Reveal}, point={frame.Point}, pixel=#{frame.Pixel:x6}, minimum={minimum}.");
        }
    }
    private readonly Dictionary<Point, nuint> chatVideoNativeUnderlay = [];
    private Point[] ChatVideoBackdropPoints() => [new Point(40, 200), new Point(Window.ClientSize.Width - 40, 200),
        new Point(Window.ClientSize.Width / 2, 24), new Point(Window.ClientSize.Width / 2, Window.ClientSize.Height - 16),
        new Point(64, Window.ClientSize.Height / 2), new Point(Window.ClientSize.Width - 64, Window.ClientSize.Height / 2)];
    private void RememberNativeChatVideoBackdrop()
    {
        if (!OperatingSystem.IsLinux()) return;
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for video repaint verification.");
        try
        {
            chatVideoNativeUnderlay.Clear();
            foreach (var point in ChatVideoBackdropPoints()) chatVideoNativeUnderlay[point] = ReadNativeChatVideoPixel(display, point);
        }
        finally { XCloseDisplay(display); }
    }
    private void VerifyNativeChatVideoBackdrop(nuint? expected = null)
    {
        if (!OperatingSystem.IsLinux()) return;
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for video repaint verification.");
        try
        {
            // Enlarged portrait video legitimately reaches the top/bottom center.
            var points = expected == 0 ? ChatVideoBackdropPoints().Take(2) : ChatVideoBackdropPoints().AsEnumerable();
            foreach (var point in points)
            {
                var actual = ReadNativeChatVideoPixel(display, point);
                var underlay = chatVideoNativeUnderlay.GetValueOrDefault(point);
                var factor = (255 - ChatVideoSurface.NormalBackdropAlpha) / 255d;
                for (var shift = 0; shift <= 16; shift += 8)
                {
                    var channel = (actual >> shift) & 255;
                    var target = expected is { } solid ? (solid >> shift) & 255 : (nuint)Math.Round(((underlay >> shift) & 255) * factor);
                    if (Math.Abs((double)channel - target) > 1)
                        throw new InvalidOperationException($"Incorrect video backdrop compositing at {point}: #{actual:x6}, underlay=#{underlay:x6}.");
                }
            }
        }
        finally { XCloseDisplay(display); }
    }
    private nuint ReadNativeChatVideoPixel(nint display, Point point)
    {
        var image = ChatXGetImage(display, Window.Handle, (int)(point.X * Window.DpiScale), (int)(point.Y * Window.DpiScale), 1, 1, nuint.MaxValue, 2);
        if (image == 0) throw new InvalidOperationException("Could not read native viewer pixels.");
        try { return ChatXGetPixel(image, 0, 0) & 0xffffff; }
        finally { ChatXDestroyImage(image); }
    }
    [DllImport("libX11.so.6", EntryPoint = "XGetImage")] private static extern nint ChatXGetImage(nint display, nint drawable, int x, int y, uint width, uint height, nuint planes, int format);
    [DllImport("libX11.so.6", EntryPoint = "XGetPixel")] private static extern nuint ChatXGetPixel(nint image, int x, int y);
    [DllImport("libX11.so.6", EntryPoint = "XDestroyImage")] private static extern int ChatXDestroyImage(nint image);
}
