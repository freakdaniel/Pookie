using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyOffscreenTextGpuLifetime()
    {
        var factory = Window.GraphicsFactory;
        using var scope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(700, 120));
        using var context = factory.CreateContext(surface);
        var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest {
            Text = "Проверка памяти текста в стеклянной шапке".AsMemory(), Dpi = 96,
            DefaultStyle = new TextRunStyle("Google Sans", 22),
            Paragraph = new() { MaxWidth = 680 }
        });
        long warmBytes = 0;
        for (var frame = 0; frame < 400; frame++)
        {
            context.BeginFrame(surface);
            try
            {
                context.Clear(Color.Transparent);
                context.Text.Draw(layout, new Point(4, 4), new TextDrawOptions(Color.White, Transient: true));
            }
            finally { context.EndFrame(); }
            if (frame == 7) warmBytes = RenderResourceMetrics.Snapshot().TextCacheBytes;
        }
        var growth = RenderResourceMetrics.Snapshot().TextCacheBytes-warmBytes;
        Console.WriteLine($"GPU_OFFSCREEN_TEXT: growth_mib={growth/1048576d:F1}");
        if (growth > 1024*1024)
            throw new InvalidOperationException("Offscreen text textures accumulate between frames instead of reusing their scratch slots.");
    }

    private async Task VerifyAnimatedIconLifetimeAsync()
    {
        var root = (Panel)Window.Content!;
        var icon = Icons.View("heart", 32).Left().Top();
        root.Add(icon);
        try
        {
            var normal = Color.FromRgb(222,222,222);
            var selected = Color.FromRgb(252,147,172);
            TrackButtons.SetGlyph(icon,"heart",normal,animate:false);
            _ = Icons.Source("heart", selected);
            await PaintGpuLifetimeFrameAsync();
            var baseline = Icons.CachedSourceCount;
            // Interrupt transitions at different times, as repeated pointer moves
            // and playback-state changes do. Temporary shades must not become
            // permanent icon/texture entries.
            for (var iteration = 0; iteration < 60; iteration++)
            {
                TrackButtons.SetGlyph(icon,"heart",iteration%2==0 ? selected : normal);
                await Task.Delay(13+iteration%4*7,lifetime.Token);
            }
            TrackButtons.SetGlyph(icon,"heart",normal);
            await Task.Delay(300,lifetime.Token);
            var growth = Icons.CachedSourceCount-baseline;
            Console.WriteLine($"GPU_ICON_ANIMATION: permanent_sources_added={growth}");
            if (growth > 2) throw new InvalidOperationException("Intermediate icon colours were retained in the global SVG/GPU cache.");
        }
        finally { root.Remove(icon); icon.Dispose(); }
    }

    private async Task VerifyGlassGpuLifetimeAsync()
    {
        var root = (Panel)Window.Content!;
        var host = new Grid().Width(700).Height(350).Left().Top();
        var removed = new List<GlassCard>();
        root.Add(host);
        try
        {
            long? baseline = null;
            for (var iteration = 0; iteration < 28; iteration++)
            {
                var card = new GlassCard { HoverEnabled = false };
                host.Add(card);
                await PaintGpuLifetimeFrameAsync();
                host.Remove(card); removed.Add(card);
                await PaintGpuLifetimeFrameAsync();
                if (iteration == 3) baseline = ReadGpuAllocationBytes();
                if (iteration % 8 == 3)
                {
                    var gpu = ReadGpuAllocationBytes();
                    var metrics = RenderResourceMetrics.Snapshot();
                    Console.WriteLine($"GPU_LIFETIME: iteration={iteration+1}; gpu_mib={gpu/1048576d:F1}; scratch_mib={(metrics.ScratchActiveBytes+metrics.ScratchPooledBytes)/1048576d:F1}; text_mib={metrics.TextCacheBytes/1048576d:F1}");
                    if (baseline is { } start && gpu is { } end && end-start > 24L*1024*1024)
                        throw new InvalidOperationException($"Removed glass cards retained {(end-start)/1048576d:F1} MiB of GPU allocations after warmup.");
                }
            }
            // A view removed for recycling must remain reusable after releasing its pixels.
            host.Add(removed[0]); await PaintGpuLifetimeFrameAsync();
            host.Remove(removed[0]); await PaintGpuLifetimeFrameAsync();
            Console.WriteLine("GPU_LIFETIME_OK: rendered glass detaches without accumulating GPU allocations and can be reattached");
        }
        finally
        {
            root.Remove(host);
            foreach (var card in removed) card.Dispose();
            host.Dispose();
        }
    }

    private async Task PaintGpuLifetimeFrameAsync()
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Presented() => frame.TrySetResult();
        Window.FrameRendered += Presented;
        try
        {
            Window.InvalidateVisual();
            await frame.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { Window.FrameRendered -= Presented; }
    }

    // Account by DRM client, not file descriptor: duplicated handles report the
    // same allocation. Total includes GPU buffers evicted from VRAM into RAM.
    private static long? ReadGpuAllocationBytes()
    {
        if (!OperatingSystem.IsLinux()) return null;
        var clients = new HashSet<string>();
        long total = 0; var found = false;
        foreach (var path in Directory.EnumerateFiles("/proc/self/fdinfo"))
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (IOException) { continue; }
            var client = lines.FirstOrDefault(line => line.StartsWith("drm-client-id:", StringComparison.Ordinal));
            var device = lines.FirstOrDefault(line => line.StartsWith("drm-pdev:", StringComparison.Ordinal));
            if (client == null || !clients.Add(device+client)) continue;
            foreach (var line in lines)
            {
                if (!line.StartsWith("drm-total-vram:", StringComparison.Ordinal) && !line.StartsWith("drm-total-gtt:", StringComparison.Ordinal)) continue;
                var value = line[(line.IndexOf(':')+1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (long.TryParse(value[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kib)) { total += kib*1024; found = true; }
            }
        }
        return found ? total : null;
    }
}
