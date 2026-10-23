using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// A header capture has a much smaller clip than the list's own viewport.
// Do not enter the row's text and controls when none of it reaches that clip.
internal sealed class ViewportGrid : Grid
{
    protected override void RenderSubtree(IGraphicsContext context)
    {
        var clip = context.GetClipBoundsLocal();
        if (clip is { } region && !Bounds.IntersectsWith(region)) return;
        if (ClipToBounds) { context.Save(); context.SetClip(Bounds); }
        try
        {
            foreach (var child in Children)
                if (clip == null || child.Bounds.IntersectsWith(clip.Value)) child.Render(context);
        }
        finally { if (ClipToBounds) context.Restore(); }
    }
}
