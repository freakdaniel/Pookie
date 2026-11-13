using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep the action's layout slot fixed throughout reversible opacity changes.
internal sealed class FadingAction : ContentControl
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly FrameworkElement action;
    private bool shown;
    internal bool Animating => motion.IsRunning;
    internal FadingAction(FrameworkElement action)
    {
        this.action = action; Content = action;
        Background = Color.Transparent; BorderThickness = 0; Padding = default;
        Opacity = 0; IsHitTestVisible = action.IsVisible = false;
    }
    internal void Show(bool value, bool animate = true)
    {
        if (shown == value && animate) return;
        shown = value; motion.Stop(); IsHitTestVisible = value;
        var target = value ? 1d : 0d;
        if (!animate || FindVisualRoot() is not Window || Math.Abs(Opacity - target) < .001)
        { Opacity = target; action.IsVisible = value; return; }
        action.IsVisible = true;
        var from = Opacity;
        motion.Duration = TimeSpan.FromMilliseconds(180 * Math.Abs(target - from));
        motion.TickCallback = t => Opacity = from + (target - from) * t;
        motion.CompletedCallback = () => { Opacity = target; action.IsVisible = shown; };
        motion.Start();
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
