using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal static class TrackDialogChrome
{
    internal const double PreviewHeight = 88;
    internal static readonly Color MatteTint = Color.FromArgb(244, 24, 24, 28);
    internal static TextBlock Heading(string title) => new TextBlock().Text(title).FontSize(21).Bold().CenterVertical();

    internal static GlassCard MatteBody(FrameworkElement content, Func<bool> resizing) =>
        new GlassCard { HoverEnabled = false, ResizeInProgress = resizing }.Padding(0).CornerRadius(24)
            .MaxWidth(PlaylistDialogContent.MaximumWidth).Child(
                new Border().Background(Color.Transparent).BorderThickness(0).CornerRadius(24).ClipToBounds().Child(content));

    internal static ModalDialog MatteDialog(PlaylistDialogContent content, GlassCard card, UIElement focus,
        FrameworkElement close, Func<bool>? refreshing = null, bool reserveTransitionHeight = false, bool tall = false) =>
        new(content, focus, close, staticBackdrop: true, cacheTransition: true, backdropTint: MatteTint,
            glassSurface: card, refreshTransition: () => content.Animating || content.Resizing || refreshing?.Invoke() == true,
            glassMaximumHeight: tall ? 960 : PlaylistDialogContent.MaximumBodyHeight, glassVerticalOffset: PlaylistDialogContent.BodyCenterOffset,
            transitionMaximumHeight: reserveTransitionHeight
                ? PlaylistDialogContent.MaximumBodyHeight + PlaylistDialogContent.BodyCenterOffset * 2 : null);

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
