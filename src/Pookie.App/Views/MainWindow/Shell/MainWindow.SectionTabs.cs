using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private static readonly StyleSheet sectionButtonStyles = CreateSectionButtonStyles();

    private static StyleSheet CreateSectionButtonStyles()
    {
        var sheet = new StyleSheet();
        sheet.Define("section-button", () => Style.DeriveFromDefault<Button>(setters: [
            Setter.Create(Control.BackgroundProperty, Color.Transparent),
            Setter.Create(Control.BorderBrushProperty, Color.Transparent),
            Setter.Create(Control.BorderThicknessProperty, 0d)
        ]));
        return sheet;
    }

    private static Button SectionTab<T>(string title, ObservableValue<T> selected, T target, Action action)
    {
        bool Active(T value) => EqualityComparer<T>.Default.Equals(value, target);
        var hover = new ObservableValue<bool>(false);
        var normal = new TextBlock().Text(title).FontSize(16).SemiBold()
            .Bind(TextElement.ForegroundProperty, selected, value => Active(value) ? Color.White : Muted);
        var bright = new TextBlock().Text(title).FontSize(16).SemiBold().Foreground(Color.White)
            .Bind(UIElement.OpacityProperty, hover, value => value ? 1d : 0d);
        bright.Transitions = [Transition.Create(UIElement.OpacityProperty, 180)];
        var underline = new Border().Height(2).Background(Color.White)
            .Bind(UIElement.OpacityProperty, selected, value => Active(value) ? 1d : 0d);
        underline.Transitions = [Transition.Create(UIElement.OpacityProperty, 180)];
        var button = new Button().StyleSheet(sectionButtonStyles).StyleName("section-button")
            .Background(Color.Transparent).BorderThickness(0).Padding(0).Left()
            .BindIsEnabled(selected, value => !Active(value))
            .Content(new StackPanel().Vertical().Spacing(9).Children(
                new Grid().Columns("*").Rows("Auto").Children(normal, bright), underline))
            .OnMouseEnter(() => hover.Value = !Active(selected.Value)).OnMouseLeave(() => hover.Value = false)
            .OnClick(() => { if (!Active(selected.Value)) action(); });
        selected.Changed += () => hover.Value = !Active(selected.Value) && button.IsMouseOver;
        return button;
    }
}
