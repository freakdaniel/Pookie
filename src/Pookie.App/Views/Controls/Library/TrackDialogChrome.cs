using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal static class TrackDialogChrome
{
    internal const double PreviewHeight = 88;
    internal static TextBlock Heading(string title) => new TextBlock().Text(title).FontSize(21).Bold().CenterVertical();

    internal static PaletteGlassCard Preview(FrameworkElement artwork, TextBlock title, TextBlock author, FrameworkElement? trailing = null)
    {
        var card = new PaletteGlassCard { HoverEnabled = false }.CornerRadius(20).Padding(16).Height(PreviewHeight);
        var row = new Grid().Columns(trailing == null ? "56,*" : "56,*,Auto").Rows("*").Spacing(14).Children(
                new Border().CornerRadius(10).ClipToBounds().Child(artwork),
                new StackPanel().Vertical().Spacing(5).CenterVertical().Column(1).Children(title, author));
        if (trailing != null) row.Add(trailing.Column(2).CenterVertical());
        card.Child(row);
        return card;
    }
}
