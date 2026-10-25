using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Rebind virtualized results once, between two short fades. Never retain a
// screenshot, duplicate catalog tree, or a growing list of queued queries.
internal sealed class ChatEmojiResultsFade(FrameworkElement content) : ContentControl
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.4, 0, .2, 1));
    private Action? pending;
    internal bool Animating => motion.IsRunning;
    internal ChatEmojiResultsFade Initialize() { Content = content; Background = Color.Transparent; BorderThickness = 0; Padding = new Thickness(0); return this; }
    internal void Replace(Action apply, bool animate)
    {
        motion.Stop(); pending = apply;
        if (!animate || FindVisualRoot() is not Window) { pending = null; apply(); content.Opacity = 1; IsHitTestVisible = true; return; }
        var from = content.Opacity; IsHitTestVisible = false;
        motion.TickCallback = t =>
        {
            if (t < .35) content.Opacity = from * (1 - t / .35);
            else
            {
                var update = pending; pending = null; update?.Invoke();
                content.Opacity = (t - .35) / .65;
            }
        };
        motion.CompletedCallback = () => { var update = pending; pending = null; update?.Invoke(); content.Opacity = 1; IsHitTestVisible = true; };
        motion.Start();
    }
    internal void Stop() { motion.Stop(); pending = null; content.Opacity = 1; IsHitTestVisible = true; }
    protected override void OnDispose() { Stop(); base.OnDispose(); }
}
