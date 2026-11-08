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
        var label = new TextBlock().Text(title).FontSize(16).SemiBold()
            .Foreground(Active(selected.Value) ? Color.White : Muted).IsHitTestVisible(false);
        label.Transitions = [Transition.Create(Control.ForegroundProperty, 180)];
        var underline = new Border().Height(2).Background(Color.White)
            .Bind(UIElement.OpacityProperty, selected, value => Active(value) ? 1d : 0d).IsHitTestVisible(false);
        underline.Transitions = [Transition.Create(UIElement.OpacityProperty, 180)];
        var button = new Button().StyleSheet(sectionButtonStyles).StyleName("section-button")
            .Background(Color.Transparent).BorderThickness(0).Padding(0).Left()
            .BindIsEnabled(selected, value => !Active(value))
            .Content(new StackPanel().Vertical().Spacing(9).Children(label, underline).IsHitTestVisible(false))
            .OnClick(() => { if (!Active(selected.Value)) action(); });
        void RefreshInk() => label.Foreground = Active(selected.Value) || button.IsMouseOver ? Color.White : Muted;
        button.MouseEnter += RefreshInk;
        button.MouseLeave += RefreshInk;
        selected.Changed += RefreshInk;
        return button;
    }
}
