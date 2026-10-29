using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace Pookie.App;

// Keep a native-DPI backdrop on the rendering device. Reading its pixels back
// to the CPU forces the UI thread to wait for the capture to finish on the GPU.
internal sealed class NativeBackdropBlur(IGraphicsFactory graphics, double radius) : IDisposable
{
    private readonly IImageFilterExecutor executor = graphics.CreateImageFilterExecutor();
    private readonly BlurFilter filter = new(radius);
    private IRenderSurface? source;
    private Rect sourceBounds;
    private IImage? image;
    private DefaultFilterContext? context;
    internal FilterResult? Result { get; private set; }

    internal void Update(IRenderSurface captured, Rect bounds)
    {
        if (source != captured || sourceBounds != bounds)
        {
            Dispose(); source = captured; sourceBounds = bounds;
            image = graphics.CreateImageView(captured);
            context = new(captured, image, bounds, graphics, captured.DpiScale, captured.DpiScale);
        }
        // Keep the previous result alive until the new pass has acquired its
        // scratch. Image views retain surfaces still referenced by a scene.
        var next = executor.Execute(filter, context!);
        var previous = Result; Result = next; previous?.Dispose();
    }

    public void Dispose()
    {
        Result?.Dispose(); Result = null; context?.Dispose(); context = null;
        image?.Dispose(); image = null; source = null;
    }
}
