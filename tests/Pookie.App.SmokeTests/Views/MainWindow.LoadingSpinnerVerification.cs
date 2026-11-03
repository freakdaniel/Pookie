using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyLoadingSpinnerAsync()
    {
        var root = (Panel)Window.Content!;
        var sizes = new[] { 12, 16, 22, 24, 34 };
        var spinners = sizes.Select(size => new LoadingSpinner().Width(size).Height(size).Foreground(Color.White).IsActive()).ToArray();
        var host = new Border().Width(360).Height(110).Left().Top().Margin(24, 24, 0, 0).CornerRadius(18)
            .Background(Color.FromRgb(32, 32, 36)).Padding(24)
            .Child(new StackPanel().Horizontal().Spacing(24).Center().Children(spinners.Select((spinner, index) =>
                new StackPanel().Vertical().Spacing(10).Width(42).Children(spinner.CenterHorizontal(),
                    new TextBlock().Text(sizes[index].ToString()).FontSize(11).Foreground(Muted).CenterHorizontal())).ToArray()));
        root.Add(host);
        try
        {
            await WaitForLikedLayoutAsync(() => spinners.All(spinner => spinner.ActualWidth > 0 && spinner.Running));
            foreach (var spinner in spinners)
            {
                var pixels = CaptureSpinnerPixels(spinner);
                if (!pixels.Any(value => value != 0)) throw new InvalidOperationException("A loading spinner rendered no pixels");
                var width = (int)Math.Ceiling(spinner.ActualWidth * Window.DpiScale);
                var height = (int)Math.Ceiling(spinner.ActualHeight * Window.DpiScale);
                if (pixels[((height / 2) * width + width / 2) * 4 + 3] != 0)
                    throw new InvalidOperationException("A loading spinner filled its transparent centre");
            }
            var first = CaptureSpinnerPixels(spinners[^1]); var phase = spinners[^1].Phase;
            await WaitForLikedLayoutAsync(() => (spinners[^1].Phase - phase + 1) % 1 > .12);
            if (first.AsSpan().SequenceEqual(CaptureSpinnerPixels(spinners[^1])))
                throw new InvalidOperationException("The spinner clock advanced without changing its pixels");
            CaptureUiPreview("loading-spinners", host);

            host.IsVisible = false;
            await WaitForLikedLayoutAsync(() => spinners.All(spinner => !spinner.Running));
            host.IsVisible = true;
            await WaitForLikedLayoutAsync(() => spinners.All(spinner => spinner.Running));
            host.Opacity = 0;
            await WaitForLikedLayoutAsync(() => spinners.All(spinner => !spinner.Running));
            host.Opacity = 1;
            await WaitForLikedLayoutAsync(() => spinners.All(spinner => spinner.Running));
            root.Remove(host);
            if (spinners.Any(spinner => spinner.Running)) throw new InvalidOperationException("Detached spinners kept their clocks alive");
            root.Add(host);
            await WaitForLikedLayoutAsync(() => spinners.All(spinner => spinner.Running));
            foreach (var spinner in spinners) spinner.IsActive = false;
            if (spinners.Any(spinner => spinner.Running || CaptureSpinnerPixels(spinner).Any(value => value != 0)))
                throw new InvalidOperationException("Inactive spinners retained animation or visible pixels");
            Console.WriteLine("LOADING_SPINNER_OK: all five application sizes render and animate; transparent centres, hidden/transparent/detached suspension, reattachment and inactive cleanup");
        }
        finally { root.Remove(host); host.Dispose(); }
    }

    private byte[] CaptureSpinnerPixels(LoadingSpinner spinner)
    {
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        var width = (int)Math.Ceiling(spinner.ActualWidth * Window.DpiScale);
        var height = (int)Math.Ceiling(spinner.ActualHeight * Window.DpiScale);
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(width, height, Window.DpiScale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-spinner.Bounds.X, -spinner.Bounds.Y);
        spinner.Render(context); context.EndFrame();
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }
}
