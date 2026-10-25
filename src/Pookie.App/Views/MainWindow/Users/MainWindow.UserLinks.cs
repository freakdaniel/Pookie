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
        {
            // Resolve the resting colour after attachment, when the theme has been applied.
            // Keep it across interrupted transitions so repeated hovering cannot change it.
            Color? normal = null;
            text.Transitions = [Transition.Create(Control.ForegroundProperty, 240, Easing.CubicBezier(.2, 0, 0, 1))];
            text.MouseEnter += () =>
            {
                if (user() is not { Id: > 0 }) return;
                normal ??= text.Foreground;
                text.Foreground = normal.Value.Lerp(Color.White, .4);
            };
            text.MouseLeave += () => { if (normal is { } resting) text.Foreground = resting; };
        }
        target.MouseDown += args =>
        {
            if (args.Button != MouseButton.Left || user() is not { Id: > 0 } profile) return;
            args.Handled = true; Run(() => OpenUserProfileAsync(profile));
        };
    }
}
