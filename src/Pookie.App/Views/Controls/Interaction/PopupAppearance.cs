using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using System.Runtime.CompilerServices;

namespace Pookie.App;

internal static class PopupAppearance
{
    internal static readonly Color AppBackground = Color.FromRgb(18, 18, 20);
    internal static readonly Color Surface = Color.FromRgb(30, 30, 34);
    internal const double Radius = 16;
    internal const double Padding = 4;
    internal const double RowHeight = 36;
    internal const double IconSize = 21;

    internal static Command MenuCommand(string id, string label, string icon) =>
        new(id, label, new IconTemplate(size => Icons.View(icon, size.Dip).IsHitTestVisible(false)));

    private static readonly ConditionalWeakTable<ContextMenu, AnimatedContextMenu> menus = new();
    internal static AnimatedContextMenu CreateMenu(MenuPlacement placement = MenuPlacement.Pointer, Point offset = default)
    {
        var menu = new AnimatedContextMenu(placement, offset); menus.Add(menu.Model, menu); return menu;
    }
    internal static AnimatedContextMenu MenuFor(FrameworkElement owner) => menus.GetValue(owner.ContextMenu!, _ => throw new InvalidOperationException("Menu was not created by Pookie."));
    internal static void Attach(FrameworkElement owner, AnimatedContextMenu menu)
    {
        owner.ContextMenu = menu.Model;
        owner.MouseDown += e =>
        {
            if (e.Handled || e.Button != MouseButton.Right || owner.ContextMenu is not { } model ||
                !menus.TryGetValue(model, out var current)) return;
            e.Handled = true;
            if (owner.FindVisualRoot() is Window window) current.Toggle(owner, e.GetPosition(window));
        };
        owner.KeyDown += e =>
        {
            if (e.Handled || e.Key != Key.F10 || !e.Modifiers.HasFlag(ModifierKeys.Shift) ||
                owner.ContextMenu is not { } model || !menus.TryGetValue(model, out var current)) return;
            e.Handled = true; current.Show(owner, new Point(owner.Bounds.X, owner.Bounds.Bottom));
        };
    }

    internal static StyleSheet CreateStyles()
    {
        var styles = new StyleSheet();
        // This also reaches framework-created submenus and text edit menus.
        // Keep their command routing, focus and keyboard navigation intact.
        styles.Define<ContextMenu>(Style.DeriveFromDefault<ContextMenu>(setters:
        [
            Setter.Create(Control.BackgroundProperty, Surface),
            Setter.Create(Control.BorderBrushProperty, Color.FromArgb(24, 255, 255, 255)),
            Setter.Create(Control.BorderThicknessProperty, 1d),
            Setter.Create(Control.CornerRadiusProperty, Radius),
            Setter.Create(Control.PaddingProperty, new Thickness(Padding)),
            Setter.Create(TextElement.FontSizeProperty, 14d),
            Setter.Create(ContextMenu.ItemHeightProperty, RowHeight)
        ]));
        return styles;
    }
}
