using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Variable-height rows refine their estimates during layout. Resolve the saved visible
// anchor in that same layout pass, before the window paints the new collection.
internal sealed class ScrollLayoutHost(Func<bool> restoreAnchor) : ContentControl
{
    protected override void ArrangeContent(Rect bounds)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            base.ArrangeContent(bounds);
            if (!restoreAnchor()) break;
            Content?.Measure(new Size(bounds.Width, bounds.Height));
        }
    }
}
