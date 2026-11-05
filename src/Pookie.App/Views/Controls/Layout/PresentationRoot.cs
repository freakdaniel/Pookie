// SPDX-License-Identifier: BSD-3-Clause
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The X11 backend still requests frames for minimized native windows. Keep
// retained children alive, but do not traverse/render their presentation tree.
internal sealed class PresentationRoot : Grid
{
    protected override ChildClip? GetChildClip(UIElement child) =>
        FindVisualRoot() is Window { WindowState: WindowState.Minimized } ? new ChildClip(Rect.Empty) : base.GetChildClip(child);
}
