using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class PlaylistPrivacySelector : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private double position;
    private bool selectedPrivate;
    internal Button Public { get; }
    internal Button Private { get; }
    internal bool Animating => motion.IsRunning;
    internal Rect SelectionBounds => new(Bounds.X + 4 + position * (Bounds.Width - 8) / 2, Bounds.Y + 4, (Bounds.Width - 8) / 2, 40);
    internal PlaylistPrivacySelector(Action<bool> select)
    {
        Public = Choice("globe", "Публичный", () => select(false));
        Private = Choice("lock-simple", "Приватный", () => select(true));
        Add(Public); Add(Private); Select(false, animate: false);
        KeyDown += e =>
        {
            if (e.Key is not (Key.Left or Key.Right) || !Public.IsEnabled) return;
            e.Handled = true;
            var value = e.Key == Key.Right;
            select(value); (value ? Private : Public).Focus();
        };
    }
    private static Button Choice(string icon, string label, Action action) => TrackButtons.Action(
        new StackPanel().Horizontal().Spacing(8).Center().Children(
            Icons.View(icon, 16, TrackButtons.OnSurface).CenterVertical(),
            new TextBlock().Text(label).FontSize(13).CenterVertical()), action, TrackButtons.Text, 40);
    internal void Select(bool isPrivate, bool animate = true)
    {
        ((MaterialTrackButton)Public).SetPassive(!isPrivate);
        ((MaterialTrackButton)Private).SetPassive(isPrivate);
        if (selectedPrivate == isPrivate && animate) return;
        selectedPrivate = isPrivate; motion.Stop();
        var target = isPrivate ? 1d : 0d;
        if (!animate || FindVisualRoot() is not Window) { position = target; InvalidateVisual(); return; }
        var from = position;
        motion.TickCallback = t => { position = from + (target - from) * t; InvalidateVisual(); };
        motion.CompletedCallback = () => { position = target; InvalidateVisual(); };
        motion.Start();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 320;
        Public.Measure(new(Math.Max(0, (width - 8) / 2), 40)); Private.Measure(new(Math.Max(0, (width - 8) / 2), 40));
        return new(width, 48);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Max(0, (finalSize.Width - 8) / 2);
        Public.Arrange(new(Bounds.X + 4, Bounds.Y + 4, width, 40));
        Private.Arrange(new(Bounds.X + 4 + width, Bounds.Y + 4, width, 40));
        return finalSize;
    }
    protected override void OnRender(IGraphicsContext context)
    {
        context.FillRoundedRectangle(Bounds, 24, 24, Color.FromRgb(37, 37, 41));
        context.FillRoundedRectangle(SelectionBounds, 20, 20, Color.FromArgb(24, 255, 255, 255));
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
