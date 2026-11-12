using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyShareGlassAsync(SoundCloudTrack track)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var intervals = new List<double>();
            var last = Stopwatch.GetTimestamp();
            Rect? body = null;
            string? failure = null;
            var captures = shareDialog.SnapshotCaptures;
            var backdrops = shareDialog.BackdropVersion;
            void Frame()
            {
                var now = Stopwatch.GetTimestamp();
                var gap = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                intervals.Add(gap);
                if (gap > 33.4) Console.WriteLine($"SHARE_SLOW_FRAME gap={gap:F1}ms pose={shareDialog.Pose:F2} track={shareContent.TrackReveal:F2} captures={shareDialog.SnapshotCaptures - captures}");
                last = now;
                if (shareContent.BodyBounds.IsEmpty) return;
                if (body is { } previous && previous != shareContent.BodyBounds)
                    failure ??= "Share resized its body during the opening animation.";
                body = shareContent.BodyBounds;
            }
            Window.FrameRendered += Frame;
            double openMs;
            try
            {
                var openStarted = Stopwatch.GetTimestamp();
                ShareTrack(track);
                openMs = Stopwatch.GetElapsedTime(openStarted).TotalMilliseconds;
                await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running && !shareContent.Animating);
            }
            finally { Window.FrameRendered -= Frame; }
            var p95 = intervals.Order().ElementAt(Math.Min(intervals.Count - 1, (int)(intervals.Count * .95)));
            Console.WriteLine($"SHARE_OPEN_PERF attempt={attempt + 1} frames={intervals.Count} open={openMs:F1}ms p95={p95:F1}ms max={intervals.Max():F1}ms captures={shareDialog.SnapshotCaptures - captures} backdrops={shareDialog.BackdropVersion - backdrops}");
            if (failure != null) throw new InvalidOperationException(failure);
            if (!shareDialog.HasGlassBackdrop || !shareDialog.UsesCachedTransition || shareDialog.BackdropVersion - backdrops != 1)
                throw new InvalidOperationException("Share did not reuse a single glass backdrop while opening.");
            if (p95 > 33.4 || intervals.Max() > 100)
                throw new InvalidOperationException("Share opening exceeded the frame budget.");
            if (attempt == 0) CaptureUiPreview("share-glass");
            var backdrop = shareDialog.BackdropVersion;
            for (var hover = 0; hover < 3; hover++)
            {
                MoveActionPointer(SettingsCenter(shareDestinations[hover]));
                await Task.Delay(260, lifetime.Token);
            }
            MoveActionPointer(new(2, 2));
            if (shareDialog.BackdropVersion != backdrop)
                throw new InvalidOperationException("Hover rebuilt Share's static glass backdrop.");
            CloseShare();
            await WaitForLikedLayoutAsync(() => !shareDialog.IsVisible);
        }
        await VerifyShareGlassPixelsAsync(track);
        await VerifyShareResizeAsync(track);
        await VerifyShareGlassHandoffAsync(track);
    }

    private async Task VerifyShareResizeAsync(SoundCloudTrack track)
    {
        var saved = chatConversations.ToArray();
        try
        {
            chatConversations.Clear();
            for (var index = 0; index < 3; index++)
                chatConversations.Add(new("share-resize-" + index, new() { Id = 700 + index, Username = "Получатель " + index }, null, true));
            ShareTrack(track);
            await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Animating);
            var bodyGlass = (GlassCard)VisualTree.Find(shareContent, element => element is GlassCard and not PaletteGlassCard)!;
            foreach (var (tab, empty) in new[] { (1, false), (2, false), (0, false), (2, false), (1, false), (0, false),
                (1, true), (2, true), (0, true) })
            {
                if (empty) chatConversations.Clear();
                var intervals = new List<double>();
                var sizes = new HashSet<double>();
                var previous = Stopwatch.GetTimestamp();
                var backdrop = shareDialog.BackdropVersion;
                var reflections = bodyGlass.ReflectionRasterizations;
                var texture = bodyGlass.OpticalImage;
                var paints = shareDialog.OpticalPaints;
                var allocations = shareDialog.OpticalAllocations;
                var lostGlass = false;
                void Frame()
                {
                    var now = Stopwatch.GetTimestamp();
                    intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now;
                    sizes.Add(Math.Round(shareContent.BodyBounds.Height, 2));
                    if (!shareDialog.HasGlassBackdrop) lostGlass = true;
                    if (texture != null && (!ReferenceEquals(texture, bodyGlass.OpticalImage) || !bodyGlass.IsOpticsCurrent))
                        lostGlass = true;
                }
                Window.FrameRendered += Frame;
                try
                {
                    SelectShareTab(tab);
                    await WaitForLikedLayoutAsync(() => !shareContent.Resizing);
                    await WaitForLoginFrameAsync();
                }
                finally { Window.FrameRendered -= Frame; }
                var p95 = intervals.Order().ElementAt(Math.Min(intervals.Count - 1, (int)(intervals.Count * .95)));
                Console.WriteLine($"SHARE_RESIZE_PERF tab={tab} empty={empty} frames={intervals.Count} sizes={sizes.Count} p95={p95:F1}ms max={intervals.Max():F1}ms backdrops={shareDialog.BackdropVersion - backdrop} reflections={bodyGlass.ReflectionRasterizations - reflections} optical_paints={shareDialog.OpticalPaints - paints} optical_allocations={shareDialog.OpticalAllocations - allocations}");
                if (p95 > 16.7 || intervals.Max() > 50 || sizes.Count < 4 || lostGlass || shareDialog.BackdropVersion != backdrop ||
                    bodyGlass.ReflectionRasterizations != reflections || shareDialog.OpticalAllocations != allocations ||
                    texture != null && shareDialog.OpticalPaints - paints < sizes.Count - 1)
                    throw new InvalidOperationException("Share tab resize rebuilt glass per frame, lost its surface, or exceeded the frame budget.");
            }
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POOKIE_UI_PREVIEW")))
            {
                var captured = false;
                var initial = shareContent.BodyBounds.Height;
                void CaptureResize()
                {
                    if (captured || !shareContent.Resizing || Math.Abs(shareContent.BodyBounds.Height - initial) < 8 ||
                        Math.Abs(shareContent.BodyBounds.Height - shareContent.TargetBodyHeight) < 8) return;
                    captured = true; CaptureUiPreview("share-tab-resize");
                }
                Window.FrameRendered += CaptureResize;
                try { SelectShareTab(2); await WaitForLikedLayoutAsync(() => !shareContent.Resizing); }
                finally { Window.FrameRendered -= CaptureResize; }
                if (!captured) throw new InvalidOperationException("Share resize had no intermediate preview frame.");
            }
            SelectShareTab(1); await Task.Delay(60, lifetime.Token);
            SelectShareTab(2); await Task.Delay(60, lifetime.Token);
            SelectShareTab(0);
            await WaitForLikedLayoutAsync(() => !shareContent.Resizing);
            if (shareTab.Value != 0 || !shareLinksPanel.IsVisible || shareComposePanel.IsVisible || shareEmbedPanel.IsVisible)
                throw new InvalidOperationException("Interrupted Share resize settled on an outdated tab.");
            Console.WriteLine("SHARE_RESIZE_OK: nine tab transitions including empty recipients, retained background/reflection, smooth intermediate sizes and interrupted resize");
        }
        finally { CloseShare(true); chatConversations.Clear(); chatConversations.AddRange(saved); }
    }

    private Color ReadShareGlassPixel(Point point) => ReadDialogGlassPixel(shareDialog, point);

    private Color ReadDialogGlassPixel(ModalDialog dialog, Point point)
    {
        using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(4, 4, 1));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(2 - point.X, 2 - point.Y);
        dialog.Render(context); context.EndFrame();
        var cpu = (ICpuPixelSurface)surface; var pixels = cpu.GetReadOnlyPixelSpan(); var offset = 2 * cpu.StrideBytes + 8;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private async Task VerifyShareGlassHandoffAsync(SoundCloudTrack track)
    {
        var root = (Panel)Window.Content!;
        var fixture = new PopupGlassFixture { IsHitTestVisible = false, Split = double.MaxValue,
            LeftColor = Color.FromRgb(240, 30, 30) };
        root.Insert(root.Children.ToList().IndexOf(shareDialog), fixture.Row(0));
        Color? before = null;
        void Frame()
        {
            if (before != null || !shareDialog.IsOpen || !shareDialog.Running || shareDialog.Pose < .985 || !shareDialog.HasGlassBackdrop) return;
            before = ReadShareGlassPixel(new(shareContent.BodyBounds.X + 128, shareContent.BodyBounds.Y + 15));
        }
        try
        {
            await WaitForLoginFrameAsync();
            Window.FrameRendered += Frame;
            ShareTrack(track);
            await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Animating);
            Window.FrameRendered -= Frame;
            if (before is not { } earlier) throw new InvalidOperationException("No animated glass frame was observed before the live handoff.");
            var after = ReadShareGlassPixel(new(shareContent.BodyBounds.X + 128, shareContent.BodyBounds.Y + 15));
            var difference = Math.Max(Math.Abs(earlier.R - after.R), Math.Max(Math.Abs(earlier.G - after.G), Math.Abs(earlier.B - after.B)));
            Console.WriteLine($"SHARE_GLASS_HANDOFF before={earlier} after={after} max_delta={difference}");
            if (difference > 3) throw new InvalidOperationException("The opening animation changed its glass background at the live handoff.");
        }
        finally { Window.FrameRendered -= Frame; CloseShare(true); root.Remove(fixture); fixture.Dispose(); }
    }

    private async Task VerifyShareGlassPixelsAsync(SoundCloudTrack track)
    {
        var root = (Panel)Window.Content!;
        var fixture = new PopupGlassFixture { IsHitTestVisible = false, Split = double.MaxValue };
        root.Insert(root.Children.ToList().IndexOf(shareDialog), fixture.Row(0));
        async Task<Color> Sample(Color color)
        {
            fixture.LeftColor = fixture.RightColor = color; fixture.InvalidateVisual();
            await WaitForLoginFrameAsync();
            ShareTrack(track);
            await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Animating && shareDialog.HasGlassBackdrop);
            var point = new Point(shareContent.BodyBounds.X + 14, shareContent.BodyBounds.Center.Y);
            var result = ReadShareGlassPixel(point);
            CloseShare();
            await WaitForLikedLayoutAsync(() => !shareDialog.IsVisible);
            return result;
        }
        try
        {
            var red = await Sample(Color.FromRgb(240, 30, 30));
            var blue = await Sample(Color.FromRgb(30, 30, 240));
            if (red.R - red.B is < 4 or > 20 || blue.B - blue.R is < 4 or > 20)
                throw new InvalidOperationException($"Share matte glass lost its subtle backdrop or allowed too much color through: red={red}, blue={blue}.");
            Console.WriteLine("SHARE_GLASS_OK: background colors reach the glass, one backdrop per opening, stable body, cached standard animation and hover");
        }
        finally { CloseShare(true); root.Remove(fixture); fixture.Dispose(); }
    }
}
