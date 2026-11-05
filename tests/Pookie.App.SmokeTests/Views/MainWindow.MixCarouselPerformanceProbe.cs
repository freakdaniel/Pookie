using System.Diagnostics;
using Aprillz.MewUI;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task ProbeHomeMixCarouselAsync(bool live)
    {
        Window.WindowSize = WindowSize.Resizable(1400, 980, minWidth: 1000, minHeight: 680);
        if (live)
        {
            using var vault = SessionVault.Open(new AppDataPaths());
            var account = vault.Load() ?? throw new InvalidOperationException("No saved account for the live carousel probe.");
            AttachBrowser(new NativeBrowserSession(account, profilePath: Path.Combine(dataPaths.Browser, "MixProbe")));
            me = await browser!.GetMeAsync(lifetime.Token);
            api.Session = browser.Account;
        }
        else
        {
            var fixture = new HomeFixtureTransport(); fixture.RelatedReady.SetResult();
            api.Session = new("homefixture", "fixture-token", "Test"); api.BrowserTransport = fixture;
            me = new() { Id = 42, Username = "Home fixture" };
            await imageDiskCache.WriteAsync(HomeFixtureTransport.Artwork, ArtworkFixtureBitmap(500), lifetime.Token);
        }
        signedIn.Value = true;
        await ShowWorkspaceAsync();
        await NavigateHomeAsync();
        Window.Activate();
        await WaitForLikedLayoutAsync(() => Window.IsActive && homeMixCarousel.ActualHeight > 290 && HomeItems("mixes").Length > 1);
        await Task.WhenAll(HomeItems("mixes").Select(GetCollectionArtworkAsync));
        MoveActionPointer(new Point(homeMixCarousel.Bounds.X + 2, homeMixCarousel.Bounds.Y + 2));
        await ObserveAsync(TimeSpan.FromSeconds(2));
        Action? beginInput = null;
        Console.WriteLine($"MIX_PROBE_SETUP live={live}; mixes={HomeItems("mixes").Length}; dpi={Window.DpiScale}; size={Window.ClientSize}");
        await MeasureAsync("auto", async () => await ObserveAsync(TimeSpan.FromSeconds(4)));
        await MeasureAsync("rear-click", async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var rear = (homeMixCarousel.FrontIndex + HomeItems("mixes").Length / 2) % HomeItems("mixes").Length;
                var inputStarted = Stopwatch.GetTimestamp();
                var point = RearPoint(rear);
                var searchMs = Stopwatch.GetElapsedTime(inputStarted).TotalMilliseconds;
                inputStarted = Stopwatch.GetTimestamp();
                beginInput?.Invoke();
                MoveActionPointer(point);
                RouteWaveformClick(point);
                Console.WriteLine($"MIX_CLICK_INPUT index={i} search_ms={searchMs:F2} route_ms={Stopwatch.GetElapsedTime(inputStarted).TotalMilliseconds:F2}");
                await ObserveAsync(TimeSpan.FromSeconds(1.1));
            }
        });
        MoveActionPointer(FindHomeMixPoint(homeMixCarousel.FrontIndex));
        await ObserveAsync(TimeSpan.FromSeconds(1));
        await MeasureAsync("hover-idle", async () => await ObserveAsync(TimeSpan.FromSeconds(1)));
        CaptureUiPreview("mix-carousel-probe");
        Console.WriteLine("MIX_CAROUSEL_PROBE_OK");

        Point RearPoint(int index)
        {
            var bounds = homeMixCarousel.Bounds;
            for (var y = bounds.Y + 10; y < bounds.Bottom - 10; y += 16)
                for (var x = bounds.X + 10; x < bounds.Right - 10; x += 16)
                    if (homeMixCarousel.CardAt(new Point(x, y), out var badge) == index && !badge &&
                        homeMixCarousel.CardAt(new Point(x + 4, y + 4), out badge) == index && !badge) return new(x, y);
            throw new InvalidOperationException("No visible rear cover body in the carousel probe.");
        }

        async Task MeasureAsync(string phase, Func<Task> action)
        {
            var gaps = new List<double>(); var draw = new List<double>(); var present = new List<double>();
            var slow = new List<string>();
            var layouts = new List<double>(); var allocations = new List<long>(); var calls = new List<int>();
            var previous = Stopwatch.GetTimestamp(); long lastIndex = -1;
            void Frame()
            {
                var now = Stopwatch.GetTimestamp(); gaps.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now;
                var stats = Window.LastFramePerformanceStats;
                if (gaps[^1] > 50) slow.Add($"MIX_SLOW {phase} gap_ms={gaps[^1]:F2} frame={stats.FrameIndex} frame_ms={stats.FrameMs:F2} draw_ms={stats.RenderBodyMs:F2} present_ms={stats.PresentMs:F2} layout_ms={(stats.LayoutRan ? stats.LayoutMs : 0):F2} gen2={stats.Gen2Collections}");
                if (stats.IsEmpty || stats.FrameIndex == lastIndex) return;
                lastIndex = stats.FrameIndex;
                draw.Add(stats.RenderBodyMs); present.Add(stats.PresentMs); if (stats.LayoutRan) layouts.Add(stats.LayoutMs);
                allocations.Add(stats.AllocatedBytes); calls.Add(stats.DrawCalls);
            }
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var rasterBuilds = Icons.RasterBuildCount;
            var watch = Stopwatch.StartNew();
            // Completed turns deliberately stop rendering while waiting for the next click.
            // Start the next latency interval at input, excluding that idle dwell.
            beginInput = () => previous = Stopwatch.GetTimestamp();
            Window.FrameRendered += Frame;
            try { await action(); }
            finally { Window.FrameRendered -= Frame; beginInput = null; }
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            gaps.Sort(); draw.Sort(); present.Sort(); layouts.Sort();
            double P95(List<double> values) => values.Count == 0 ? 0 : values[Math.Min(values.Count - 1, (int)(values.Count * .95))];
            Console.WriteLine($"MIX_PERF {phase} live={live} frames={gaps.Count} fps={(gaps.Count == 0 ? 0 : gaps.Count * 1000 / gaps.Sum()):F1} gap_p95={P95(gaps):F2} gap_max={gaps.DefaultIfEmpty().Max():F2} over25={gaps.Count(value => value > 25)} draw_p95={P95(draw):F2} present_p95={P95(present):F2} layout_p95={P95(layouts):F2} calls_mean={calls.DefaultIfEmpty().Average():F1} allocated_mean={allocations.DefaultIfEmpty().Average():F0} cpu_ms={cpuMs:F0} icon_raster_builds={Icons.RasterBuildCount - rasterBuilds}");
            foreach (var line in slow) Console.WriteLine(line);
        }
        async Task ObserveAsync(TimeSpan duration)
        {
            using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < duration) await ticks.WaitForNextTickAsync(lifetime.Token);
        }
    }
}
