using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal static class MaterialSearchField
{
    internal static Border Create(TextBox input)
    {
        input.Background(Color.Transparent).BorderThickness(0).Padding(0).Height(28).FontSize(13)
            .Foreground(TrackButtons.OnSurface);
        var field = new Border().Background(TrackButtons.TonalSurface).BorderThickness(0)
            .CornerRadius(20).Padding(16, 0).Height(40).CenterVertical().ClipToBounds()
            .Child(new Grid().Columns("20,*").Rows("*").Spacing(10).Children(
                Icons.View("magnifying-glass", 20, Color.FromArgb(175, 232, 232, 235)).CenterVertical().Column(0),
                input.CenterVertical().Column(1)));
        field.Transitions = [Transition.Create(Control.BackgroundProperty, 150)];
        void Refresh() => field.Background = TrackButtons.StateColor(TrackButtons.TonalSurface, TrackButtons.OnSurface,
            input.IsFocused ? .12 : field.IsMouseOver ? .08 : 0);
        field.MouseEnter += Refresh; field.MouseLeave += Refresh;
        field.MouseDown += args => { if (args.Button == MouseButton.Left) input.Focus(); };
        input.GotFocus += Refresh; input.LostFocus += Refresh;
        return field;
    }
}
