using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class WidgetColorPopup : InAppPopup
{
    internal WidgetColorSpectrum Spectrum { get; } = new();
    internal Color SelectedColor { get; private set; }
    internal GlassCard Surface { get; }
    internal Action<Color>? ColorChanged;
    internal Action? Hidden;
    private readonly TextBox[] rgb = new TextBox[3], hsv = new TextBox[3];
    private bool syncing;
    internal WidgetColorPopup(Func<Rect> anchor)
    {
        FrameworkElement Row(string[] labels, TextBox[] fields, bool rgbRow)
        {
            var row = new Grid().Columns("*,*,*").Spacing(8);
            for (var index = 0; index < 3; index++)
            {
                fields[index] = new TextBox().FontSize(13).MaxLength(3).Height(36).Padding(10, 0).CornerRadius(10)
                    .Background(GlassSheen.LightControlTint(TrackButtons.GlassSurface)).BorderThickness(0);
                fields[index].OnTextChanged(_ => Read(rgbRow));
                row.Add(new StackPanel().Vertical().Spacing(5).Children(new TextBlock().Text(labels[index]).FontSize(11)
                    .Foreground(TrackButtons.OnSurface), fields[index]).Column(index));
            }
            return row;
        }
        Surface = new GlassCard { HoverEnabled = false }.Width(292).Padding(16).CornerRadius(20).Background(PopupAppearance.Surface)
            .Child(new StackPanel().Vertical().Spacing(12).Children(Spectrum, Row(["R", "G", "B"], rgb, true), Row(["H", "S", "V"], hsv, false)));
        InitializePopup(Surface, anchor, () => MenuPlacement.Below);
        Spectrum.Changed = value => Publish(value);
        KeyboardFocusFeedback.Attach(Spectrum);
    }
    internal void SetColor(Color color)
    {
        if (syncing || color == SelectedColor) return;
        SelectedColor = color; var value = HsvColor.FromColor(color);
        if (value.S == 0) value = new(Spectrum.Hsv.H, value.S, value.V);
        Synchronize(value);
    }
    internal void Show(UIElement trigger)
    {
        var keyboard = trigger is Control control && KeyboardFocusFeedback.IsVisible(control);
        ShowPopup(trigger, Spectrum); KeyboardFocusFeedback.Set(Spectrum, keyboard);
    }
    private void Synchronize(HsvColor value)
    {
        syncing = true;
        try
        {
            Spectrum.Set(value);
            int[] rgbValues = [SelectedColor.R, SelectedColor.G, SelectedColor.B];
            int[] hsvValues = [(int)Math.Round(value.H) % 360, (int)Math.Round(value.S * 100), (int)Math.Round(value.V * 100)];
            for (var index = 0; index < 3; index++)
            { rgb[index].Text = rgbValues[index].ToString(CultureInfo.InvariantCulture); hsv[index].Text = hsvValues[index].ToString(CultureInfo.InvariantCulture); }
        }
        finally { syncing = false; }
    }
    private void Publish(HsvColor value)
    { SelectedColor = value.ToRgb(); Synchronize(value); syncing = true; try { ColorChanged?.Invoke(SelectedColor); } finally { syncing = false; } }
    private void Read(bool rgbRow)
    {
        if (syncing) return;
        var fields = rgbRow ? rgb : hsv;
        var values = new int[3];
        for (var index = 0; index < 3; index++)
            if (!int.TryParse(fields[index]?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out values[index]) ||
                values[index] < 0 || values[index] > (rgbRow ? 255 : index == 0 ? 359 : 100)) return;
        var value = rgbRow ? HsvColor.FromRgb((byte)values[0], (byte)values[1], (byte)values[2]) : new(values[0], values[1] / 100f, values[2] / 100f);
        // Keep the edited row intact so typing a multi-digit value does not
        // move its caret or round HSV through RGB after every keystroke.
        SelectedColor = value.ToRgb(); Spectrum.Set(value); syncing = true;
        try
        {
            var other = rgbRow ? hsv : rgb;
            int[] otherValues = rgbRow ? [(int)Math.Round(value.H) % 360, (int)Math.Round(value.S * 100), (int)Math.Round(value.V * 100)] :
                [SelectedColor.R, SelectedColor.G, SelectedColor.B];
            for (var index = 0; index < 3; index++) other[index].Text = otherValues[index].ToString(CultureInfo.InvariantCulture);
            ColorChanged?.Invoke(SelectedColor);
        }
        finally { syncing = false; }
    }
    internal void HandleKey(KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter) { Close(); e.Handled = true; }
        else if (e.Key == Key.Tab)
        {
            UIElement[] stops = [Spectrum, .. rgb, .. hsv];
            var index = Array.IndexOf(stops, (FindVisualRoot() as Window)?.FocusManager.FocusedElement);
            stops[(index + (e.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1) + stops.Length) % stops.Length].Focus(); e.Handled = true;
        }
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { base.OnVisualRootChanged(oldRoot, newRoot); if (newRoot == null && oldRoot != null) Hidden?.Invoke(); }
}
