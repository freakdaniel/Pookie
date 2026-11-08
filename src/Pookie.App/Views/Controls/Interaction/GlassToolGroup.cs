using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Filters sit directly on the page surface, which is also their fallback backdrop.
internal sealed class GlassToolGroup : GlassCard
{
    private readonly LiveGlassBackdrop backdrop;

    internal GlassToolGroup(FrameworkElement content, Thickness padding)
    {
        Height = 40;
        Padding = padding;
        Content = content;
        backdrop = new LiveGlassBackdrop(this, static (_, _) => { }, tint: ShellHeader.GlassTint);
        UseLiveBackdrop(backdrop);
    }

    internal void CaptureLiveGlass(IGraphicsContext context) => backdrop.CaptureLive(context);

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        backdrop.Attach(newRoot as Window);
    }

    protected override void OnDispose()
    {
        backdrop.Dispose();
        base.OnDispose();
    }
}
