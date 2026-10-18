using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class ChatFileCard : ContentControl
{
    internal readonly TextBlock NameLabel, DetailLabel;
    private readonly FileLayout layout;
    internal void ReserveFooter(double width) => layout.ReserveFooter(width);
    internal ChatFileCard(string name, string detail, bool own, bool video, Action activate)
    {
        Background = Color.Transparent; BorderThickness = 0;
        Padding = new Thickness(0); CornerRadius = 8; HorizontalAlignment = HorizontalAlignment.Left;
        NameLabel = new TextBlock().Text(name).FontSize(14).Foreground(TrackButtons.OnSurface).TextTrimming(TextTrimming.CharacterEllipsis);
        DetailLabel = new TextBlock().Text(detail).FontSize(11).Foreground(Color.FromRgb(174, 193, 209)).TextTrimming(TextTrimming.CharacterEllipsis);
        var icon = new Border().Width(44).Height(44).CornerRadius(22).BorderThickness(0)
            .Background(own ? Color.FromRgb(77, 157, 224) : Color.FromRgb(83, 101, 120))
            .Child(Icons.View(video ? "play-solid" : "file", 22, Color.White).Center());
        layout = new(icon, NameLabel, DetailLabel);
        Content = layout.Initialize(); Cursor = CursorType.Hand; Focusable = true;
        MouseDown += e => { if (e.Button == MouseButton.Left) { e.Handled = true; activate(); } };
        KeyDown += e => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; activate(); } };
    }

    private sealed class FileLayout(FrameworkElement icon, FrameworkElement name, FrameworkElement detail) : Panel
    {
        private double footer;
        internal FileLayout Initialize() { Add(icon); Add(name); Add(detail); return this; }
        internal void ReserveFooter(double width) { if (Math.Abs(width - footer) < .1) return; footer = width; InvalidateMeasure(); }
        protected override Size MeasureContent(Size availableSize)
        {
            name.Measure(new Size(double.PositiveInfinity, 24));
            var width = Math.Min(double.IsFinite(availableSize.Width) ? availableSize.Width : 320, Math.Clamp(name.DesiredSize.Width + 58, 220, 320));
            icon.Measure(new Size(44, 44)); name.Measure(new Size(Math.Max(1, width - 56), 24));
            detail.Measure(new Size(Math.Max(1, width - 56 - footer), 20));
            return new(width, 52);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            icon.Arrange(new Rect(Bounds.X, Bounds.Y + 4, 44, 44));
            name.Arrange(new Rect(Bounds.X + 56, Bounds.Y + 6, Math.Max(1, finalSize.Width - 56), name.DesiredSize.Height));
            detail.Arrange(new Rect(Bounds.X + 56, Bounds.Bottom - 7 - detail.DesiredSize.Height,
                Math.Max(1, finalSize.Width - 56 - footer), detail.DesiredSize.Height));
            return finalSize;
        }
    }
}
