using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class DialogSearchField : GlassCard
{
    internal TextBox Input { get; } = new TextBox().FontSize(14).Height(28).Padding(0)
        .Background(Color.Transparent).Foreground(TrackButtons.OnSurface).BorderThickness(0);
    internal TextBlock PlaceholderLabel { get; }
    internal SearchActivityGlyph Glyph { get; }
    internal Button ClearButton { get; }

    internal DialogSearchField(string placeholder, Color iconColor)
    {
        HoverEnabled = ShadowEnabled = false;
        Height = 48; CornerRadius = 24; Padding = new Thickness(16, 0);
        Background = GlassSheen.LightControlTint(TrackButtons.GlassSurface);
        PlaceholderLabel = new TextBlock().Text(placeholder).FontSize(13).Foreground(TextAppearance.Secondary)
            .IsHitTestVisible(false).CenterVertical().Left();
        Glyph = new SearchActivityGlyph(iconColor).Center();
        ClearButton = TrackButtons.Icon(Icons.View("x", 16, iconColor), () => { Input.Text = ""; Input.Focus(); }, TrackButtons.Text, 28)
            .Hint(UiText.Get("search.clear")).IsVisible(false);
        Child(new Grid().Columns("20,*,Auto").Rows("*").Spacing(10).Children(Glyph,
            new Grid().Columns("*").Rows("*").Column(1).Children(Input.CenterVertical(), PlaceholderLabel),
            ClearButton.Column(2).CenterVertical()));
        Input.GotFocus += () => SetEmphasized(true);
        Input.LostFocus += () => SetEmphasized(false);
        MouseDown += e => { if (!e.Handled && e.Button == MouseButton.Left) Input.Focus(); };
        Input.OnTextChanged(_ =>
        {
            var empty = Input.Text.Length == 0;
            PlaceholderLabel.IsVisible = empty;
            ClearButton.IsVisible = !empty;
            Glyph.SetInk(empty ? iconColor : Color.White);
        });
    }
}
