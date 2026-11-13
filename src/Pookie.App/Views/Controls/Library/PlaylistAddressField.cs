using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class PlaylistAddressField : GlassCard
{
    private static readonly Color Normal = GlassSheen.LightControlTint(TrackButtons.GlassSurface);
    private static readonly Color Error = Color.FromArgb(100, 195, 63, 84);
    private static readonly Color ErrorInk = Color.FromRgb(255, 159, 180);
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly TextBox input;
    private bool invalid;
    private double amount;
    internal TextBlock Label { get; } = new TextBlock().Text("Адрес").FontSize(14).CenterVertical();
    internal bool Animating => motion.IsRunning;
    internal double InvalidAmount => amount;

    internal PlaylistAddressField(TextBox input)
    {
        this.input = input;
        HoverEnabled = ShadowEnabled = false;
        CornerRadius = 22; Padding = default;
        // Own all three colours. Interpolating translucent white and red as
        // straight RGBA creates a bright pink flash halfway through the fade.
        Transitions = input.Transitions = Label.Transitions = [];
        Content = new Grid().Columns("124,*").Spacing(12).Margin(16, 8).Children(Label, input.Column(1).CenterVertical());
        input.GotFocus += () => SetEmphasized(true);
        input.LostFocus += () => SetEmphasized(false);
        Apply(0);
    }

    internal void SetInvalid(bool value, bool animate = true)
    {
        if (invalid == value) return;
        invalid = value; motion.Stop();
        var target = value ? 1d : 0d;
        if (!animate || FindVisualRoot() is not Window) { Apply(target); return; }
        var from = amount;
        motion.TickCallback = t => Apply(from + (target - from) * t);
        motion.CompletedCallback = () => Apply(target);
        motion.Start();
    }

    private void Apply(double value)
    {
        amount = value;
        var alpha = Normal.A * (1 - value) + Error.A * value;
        byte Channel(byte normal, byte error) => (byte)Math.Clamp(Math.Round(
            (normal * Normal.A * (1 - value) + error * Error.A * value) / alpha), 0, 255);
        Background = Color.FromArgb((byte)Math.Round(alpha), Channel(Normal.R, Error.R), Channel(Normal.G, Error.G), Channel(Normal.B, Error.B));
        input.Foreground = Label.Foreground = TrackButtons.OnSurface.Lerp(ErrorInk, value);
    }

    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
