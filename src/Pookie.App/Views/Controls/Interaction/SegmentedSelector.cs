using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal class SegmentedSelector : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private double position;
    private double stretch;
    private bool selectedSecond;
    private readonly bool liquidGlass;
    private readonly GlassSheenRaster sheen = new();
    private readonly List<(Button Button, Image Icon, TextBlock Label, string Name, bool Second)> choices = [];
    internal Button First { get; }
    internal Button Second { get; }
    internal bool Animating => motion.IsRunning;
    internal int ReflectionRasterizations => sheen.Rasterizations;
    internal Rect SelectionBounds
    {
        get
        {
            var width = Math.Max(0, (Bounds.Width - 8) / 2);
            var left = Bounds.X + 4 + position * width - width * stretch / 2;
            var right = left + width * (1 + stretch);
            left = Math.Max(Bounds.X + 4, left); right = Math.Min(Bounds.Right - 4, right);
            return new(left, Bounds.Y + 4 + stretch * 6, Math.Max(0, right - left), 40 - stretch * 12);
        }
    }
    internal SegmentedSelector(string firstIcon, string firstLabel, string secondIcon, string secondLabel, Action<bool> select, bool liquidGlass = false)
    {
        this.liquidGlass = liquidGlass;
        First = Choice(firstIcon, firstLabel, () => select(false), false);
        Second = Choice(secondIcon, secondLabel, () => select(true), true);
        Add(First); Add(Second); Select(false, animate: false);
        KeyDown += e =>
        {
            if (e.Key is not (Key.Left or Key.Right) || !First.IsEnabled) return;
            e.Handled = true;
            var value = e.Key == Key.Right;
            select(value); (value ? Second : First).Focus();
        };
    }
    private Button Choice(string icon, string label, Action action, bool second)
    {
        var image = Icons.View(icon, 16, TrackButtons.OnSurface).CenterVertical();
        var text = new TextBlock().Text(label).FontSize(13).CenterVertical();
        var button = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Center().Children(image, text), action, TrackButtons.Text, 40);
        choices.Add((button, image, text, icon, second));
        if (liquidGlass)
        {
            ((MaterialTrackButton)button).SuppressStateLayer();
            button.MouseEnter += () => RefreshInk(); button.MouseLeave += () => RefreshInk();
        }
        return button;
    }
    private void RefreshInk(bool animate = true)
    {
        if (!liquidGlass) return;
        foreach (var choice in choices)
        {
            var color = choice.Second == selectedSecond || choice.Button.IsMouseOver ? Color.White : Color.FromRgb(166, 166, 173);
            TrackButtons.SetGlyph(choice.Icon, choice.Name, color, animate);
            TrackButtons.SetTextColor(choice.Label, color, animate);
        }
    }
    internal void Select(bool second, bool animate = true)
    {
        ((MaterialTrackButton)First).SetPassive(!second);
        ((MaterialTrackButton)Second).SetPassive(second);
        if (selectedSecond == second && animate) return;
        selectedSecond = second; motion.Stop(); RefreshInk(animate);
        var target = second ? 1d : 0d;
        if (!animate || FindVisualRoot() is not Window) { position = target; stretch = 0; InvalidateVisual(); return; }
        var from = position; var fromStretch = stretch;
        motion.TickCallback = t => { position = from + (target - from) * t;
            stretch = liquidGlass ? fromStretch * (1 - t) + .16 * Math.Abs(target - from) * Math.Sin(Math.PI * t) : 0; InvalidateVisual(); };
        motion.CompletedCallback = () => { position = target; stretch = 0; InvalidateVisual(); };
        motion.Start();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 320;
        First.Measure(new(Math.Max(0, (width - 8) / 2), 40)); Second.Measure(new(Math.Max(0, (width - 8) / 2), 40));
        return new(width, 48);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Max(0, (finalSize.Width - 8) / 2);
        First.Arrange(new(Bounds.X + 4, Bounds.Y + 4, width, 40));
        Second.Arrange(new(Bounds.X + 4 + width, Bounds.Y + 4, width, 40));
        return finalSize;
    }
    protected override void OnRender(IGraphicsContext context)
    {
        context.FillRoundedRectangle(Bounds, 24, 24, Color.FromRgb(37, 37, 41));
        if (!liquidGlass) { context.FillRoundedRectangle(SelectionBounds, 20, 20, Color.FromArgb(24, 255, 255, 255)); return; }
        var bounds = SelectionBounds; var width = Math.Max(1, (Bounds.Width - 8) / 2);
        // Stretch a fixed optical surface on the GPU. Re-rasterizing a moving
        // rim at every intermediate width would make switching noticeably slow.
        context.Save(); context.Translate(bounds.X, bounds.Y); context.Scale(bounds.Width / width, bounds.Height / 40);
        var local = new Rect(0, 0, width, 40);
        context.FillRoundedRectangle(local, 20, 20, Color.FromArgb(32, 255, 255, 255));
        sheen.Draw(context, local, new CornerRadius(20), .4, GetDpi() / 96d);
        context.Restore();
    }
    protected override void OnDispose() { motion.Stop(); sheen.Dispose(); base.OnDispose(); }
}
