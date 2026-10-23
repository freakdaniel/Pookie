using System.Diagnostics;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.Lyrics;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task ProbeExpandedRenderingAsync()
    {
        workspace.IsVisible = true;
        ShowLibraryTracks();
        await WaitForLoginFrameAsync();
        await WaitForLikedLayoutAsync(() => shellPageScroll.IsReady);
        shellScroll.SetScrollOffsets(0, 320);
        await WaitForLoginFrameAsync();
        ToggleExpandedPanel(PlayerPanel.Lyrics);
        CancelLyricsRequest();
        ApplyLyrics(new(LyricsStatus.Synced, new LyricsFixtureProvider().Record.ToDocument()));
        await WaitForLikedLayoutAsync(() => !panelAnimation.IsRunning && !lyricsReveal.IsRunning && !expandedBackdrop.Running);

        async Task Sample(string label)
        {
            var times = new List<double>();
            for (var i = 0; i < 90; i++)
            {
                var watch = Stopwatch.StartNew();
                TickLyrics(12 + i * .12, true);
                await WaitForLoginFrameAsync();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            Console.WriteLine($"RENDER_PROBE {label}: mean={times.Average():F1}ms p95={times[85]:F1}ms max={times[^1]:F1}ms frames={times.Count}");
        }
        await Sample("lyrics-workspace-visible-" + workspace.IsVisible);
        workspace.IsVisible = false;
        await Sample("lyrics-workspace-hidden");

        var buttons = new List<ExpandedGlassButton>();
        VisualTree.Visit(expandedCover, element => { if (element is ExpandedGlassButton { IsConnected: true } button) buttons.Add(button); });
        var transport = (UIElement)((Grid)expandedCover.Child!).Children[1];
        RouteExpandedPointer(SettingsCenter(expandedCover));
        await WaitForLikedLayoutAsync(() => transport.Opacity > .999);
        smoothScrolls[lyricsScroll].Stop();
        // Start-Process -WindowStyle Hidden suppresses the first native ShowWindow
        // even though MewUI has made its logical window visible. Screen pixels
        // are meaningful only after explicitly showing this diagnostic window.
        ProbeShowWindow(Window.Handle, 5);
        if (!ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53))
            throw new InvalidOperationException("Could not expose the native probe window.");
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive);
        expandedHost.InvalidateVisual();
        await WaitForLoginFrameAsync();
        if (!ProbeIsWindowVisible(Window.Handle))
            throw new InvalidOperationException("The native rendering probe window is not visible.");
        if (ProbeDwmFlush() < 0) throw new InvalidOperationException("Could not synchronize native window presentation.");
        var points = new[] { new Point(10, 10), new Point(Window.ClientSize.Width - 10, 10),
            new Point(Window.ClientSize.Width - 10, Window.ClientSize.Height - 10) };
        uint[] Read()
        {
            var dc = ProbeGetDC(0);
            try { return points.Select(point => { var screen = Window.ClientToScreen(point); return ProbeGetPixel(dc, (int)screen.X, (int)screen.Y); }).ToArray(); }
            finally { ProbeReleaseDC(0, dc); }
        }
        var expected = Read();
        var settling = Stopwatch.StartNew();
        var stable = Stopwatch.StartNew();
        while (stable.Elapsed < TimeSpan.FromMilliseconds(120))
        {
            if (settling.Elapsed > TimeSpan.FromSeconds(3)) throw new InvalidOperationException("Native backdrop did not settle before the hover probe.");
            expandedBackdrop.InvalidateVisual();
            await WaitForLoginFrameAsync();
            if (ProbeDwmFlush() < 0) throw new InvalidOperationException("Could not synchronize native window presentation.");
            var next = Read();
            if (!next.SequenceEqual(expected)) { expected = next; stable.Restart(); }
        }
        var changes = 0; var sampled = 0;
        foreach (var point in points)
        {
            var screen = Window.ClientToScreen(point);
            if (ProbeWindowFromPoint(new LikedActionPoint { X = (int)screen.X, Y = (int)screen.Y }) != Window.Handle)
                throw new InvalidOperationException("Another native window occludes the hover probe.");
        }
        var nativeColors = new Dictionary<string, int>();
        if (expected.Any(color => color == uint.MaxValue)) throw new InvalidOperationException("Native hover pixels are unavailable.");
        void Frame()
        {
            var actual = Read();
            var key = string.Join(',', actual.Select(color => color.ToString("X8")));
            nativeColors[key] = nativeColors.GetValueOrDefault(key) + 1;
            if (!actual.SequenceEqual(expected)) changes++;
            sampled++;
        }
        Window.FrameRendered += Frame;
        try
        {
            foreach (var button in buttons)
            {
                RouteExpandedPointer(SettingsCenter(button));
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(1.2)) await WaitForLoginFrameAsync();
            }
        }
        finally { Window.FrameRendered -= Frame; ProbeSetWindowPos(Window.Handle, -2, 0, 0, 0, 0, 0x13); }
        Console.WriteLine($"NATIVE_HOVER_PROBE: background={string.Join(',', expected.Select(color => color.ToString("X8")))}, changed={changes}/{sampled}, window={Window.WindowState}, size={Window.ClientSize}");
        Console.WriteLine("NATIVE_HOVER_COLORS: " + string.Join("; ", nativeColors.Select(pair => $"{pair.Key}:{pair.Value}")));
        if (changes > 0) throw new InvalidOperationException("Hovering feedback buttons changed the background outside the cover.");
        SetExpandedPlayer(false);
        await WaitForLikedLayoutAsync(() => !expandedHost.IsVisible && shellPageScroll.IsReady);
        var scrollTimes = new List<double>();
        for (var step = 0; step < 90; step++)
        {
            var watch = Stopwatch.StartNew();
            shellScroll.SetScrollOffsets(0, 320 + step * 12);
            await WaitForLoginFrameAsync();
            scrollTimes.Add(watch.Elapsed.TotalMilliseconds);
        }
        scrollTimes.Sort();
        Console.WriteLine($"SCROLL_PROBE: mean={scrollTimes.Average():F1}ms p95={scrollTimes[85]:F1}ms max={scrollTimes[^1]:F1}ms frames={scrollTimes.Count}");
    }

    [DllImport("user32.dll", EntryPoint = "GetDC")] private static extern nint ProbeGetDC(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "ReleaseDC")] private static extern int ProbeReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll", EntryPoint = "GetPixel")] private static extern uint ProbeGetPixel(nint dc, int x, int y);
    [DllImport("user32.dll", EntryPoint = "ShowWindow")] private static extern bool ProbeShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool ProbeIsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool ProbeSetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")] private static extern nint ProbeWindowFromPoint(LikedActionPoint point);
    [DllImport("dwmapi.dll", EntryPoint = "DwmFlush")] private static extern int ProbeDwmFlush();
}
