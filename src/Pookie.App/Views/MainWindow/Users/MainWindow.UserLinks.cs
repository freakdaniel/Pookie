using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void AttachUserLink(UIElement target, Func<SoundCloudUser?> user)
    {
        target.Cursor = CursorType.Hand;
        if (target is TextBlock text)
            AttachTextLinkHover(text, colour => colour.Lerp(Color.White, .4), () => user() is { Id: > 0 });
        target.MouseDown += args =>
        {
            if (args.Button != MouseButton.Left || user() is not { Id: > 0 } profile) return;
            args.Handled = true; Run(() => OpenUserProfileAsync(profile));
        };
    }

    private static void AttachTextLinkHover(TextBlock text, Func<Color, Color> hoverColour, Func<bool>? available = null)
    {
        Color? normal = null;
        var motion = new AnimationClock(TimeSpan.FromMilliseconds(240), Easing.CubicBezier(.4, 0, .2, 1));
        // Resolve the themed resting colour on first hover, then interpolate from
        // the displayed colour so interrupted hover/leave motions stay continuous.
        text.Transitions = [];
        void Animate(Color target)
        {
            motion.Stop(); var from = text.Foreground;
            motion.TickCallback = t =>
            {
                if (text.FindVisualRoot() is not Aprillz.MewUI.Window) { motion.Stop(); text.Foreground = normal ?? target; return; }
                text.Foreground = from.Lerp(target, t);
            };
            motion.CompletedCallback = () => text.Foreground = target;
            motion.Start();
        }
        text.MouseEnter += () =>
        {
            if (available?.Invoke() == false) return;
            normal ??= text.Foreground; Animate(hoverColour(normal.Value));
        };
        text.MouseLeave += () => { if (normal is { } resting) Animate(resting); };
    }
}
