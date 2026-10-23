using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using System.Runtime.CompilerServices;

namespace Pookie.App;

// Material 3 button shapes and state layers, using Pookie's neutral dark palette.
internal static class TrackButtons
{
    internal const string Filled = "track-material-filled";
    internal const string Tonal = "track-material-tonal";
    internal const string Text = "track-material-text";
    internal const string Selected = "track-material-selected";
    internal const string Glass = "track-material-glass";
    internal const string Reposted = "track-material-reposted";
    internal const string Confirmed = "track-material-confirmed";
    internal const string CommentTime = "track-material-comment-time";
    internal const double ActionSpacing = 12;
    internal static readonly Color OnSurface = Color.FromRgb(232, 232, 235);
    internal static readonly Color OnPrimary = Color.FromRgb(32, 32, 35);
    internal static readonly Color TonalSurface = Color.FromRgb(49, 49, 53);
    internal static readonly Color SelectedSurface = Color.FromRgb(78, 40, 51);
    internal static readonly Color GlassSurface = GlassSheen.ControlTint();
    internal static readonly Color OnSelected = Color.FromRgb(255, 177, 193);
    internal static readonly Color RepostedSurface = Color.FromRgb(67, 49, 92);
    internal static readonly Color OnReposted = Color.FromRgb(211, 185, 255);
    internal static readonly Color ConfirmedSurface = Color.FromRgb(35, 65, 51);
    internal static readonly Color OnConfirmed = Color.FromRgb(149, 219, 177);
    internal static readonly Color CommentTimeSurface = Color.FromArgb(28, 110, 166, 255);
    internal static readonly Color OnCommentTime = Color.FromRgb(110, 166, 255);
    private static readonly StyleSheet styles = CreateStyles();

    internal static Button Action(FrameworkElement content, Action action, string variant = Tonal, double height = 40) =>
        Create(variant).Height(height).CornerRadius(height / 2).Padding(16, 0)
            .Content(content.CenterHorizontal().CenterVertical()).OnClick(action);

    private static Button Create(string variant)
    {
        var button = new MaterialTrackButton().StyleSheet(styles);
        if (variant == Glass) button.UseGlassFinish();
        button.Variant(variant);
        return button;
    }

    internal static Button Icon(FrameworkElement content, Action action, string variant = Tonal, double size = 40) =>
        Action(content, action, variant, size).Width(size).Padding(0);

    internal static void SetCounter(Button button, TextBlock label, string text)
    {
        label.Text = text;
        label.IsVisible = text.Length > 0;
        button.Width = label.IsVisible ? double.NaN : button.Height;
        button.Padding = new Thickness(label.IsVisible ? 16 : 0, 0);
    }

    internal static StackPanel Connected(Button first, Button second)
    {
        ((MaterialTrackButton)first).Connect(true);
        ((MaterialTrackButton)second).Connect(false);
        return new StackPanel().Horizontal().Spacing(2).Children(first, second);
    }

    internal static void SetLiked(Button button, bool liked, string inactive = Tonal)
    {
        var variant = liked ? Selected : inactive;
        SetVariant(button, variant);
    }

    internal static void SetReposted(Button button, bool reposted, string inactive = Tonal)
    {
        var variant = reposted ? Reposted : inactive;
        SetVariant(button, variant);
    }

    internal static void SetConfirmed(Button button, bool confirmed, string inactive = Tonal) =>
        SetVariant(button, confirmed ? Confirmed : inactive);

    internal static void SetVariant(Button button, string variant)
    {
        if (button is MaterialTrackButton material) material.Variant(variant);
        else if (button.StyleName != variant) button.StyleName = variant;
    }

    internal static void SetAvailability(Button button, bool available, bool pending)
    {
        if (button is MaterialTrackButton material) material.Availability(available, pending);
        else button.IsEnabled = available;
    }

    internal static void SnapNextChange(Button button)
    {
        if (button is MaterialTrackButton material) material.SnapNextChange();
    }

    internal static Color Surface(string variant) => variant switch {
        Selected => SelectedSurface, Reposted => RepostedSurface, Confirmed => ConfirmedSurface, Glass => GlassSurface,
        Filled => OnSurface, Text => Color.Transparent, CommentTime => CommentTimeSurface, _ => TonalSurface };
    internal static Color Foreground(string variant) => variant switch {
        Selected => OnSelected, Reposted => OnReposted, Confirmed => OnConfirmed, Filled => OnPrimary, CommentTime => OnCommentTime, _ => OnSurface };

    private static readonly ConditionalWeakTable<Image, GlyphColor> glyphColors = new();
    internal static void SetGlyph(Image image, string name, Color color, bool animate = true) =>
        glyphColors.GetValue(image, static icon => new GlyphColor(icon)).Set(name, color, animate);

    internal static void SetTextColor(TextBlock text, Color color, bool animate = true)
    {
        text.Transitions = animate ? [Transition.Create(Control.ForegroundProperty, 240, Easing.CubicBezier(.2, 0, 0, 1))] : [];
        text.Foreground = color;
    }

    private sealed class GlyphColor
    {
        private readonly Image image;
        private readonly AnimationClock clock = new(TimeSpan.FromMilliseconds(240), Easing.CubicBezier(.2, 0, 0, 1));
        private Color current, from, target;
        private string? name;
        internal GlyphColor(Image image)
        {
            this.image = image;
            clock.TickCallback = amount => { current = from.Lerp(target, amount); image.Source = Icons.Source(name!, current); };
        }
        internal void Set(string glyph, Color color, bool animate)
        {
            if (name == glyph && target == color && animate) return;
            var first = name == null; name = glyph; target = color; clock.Stop();
            if (first || !animate || image.FindVisualRoot() is not Window)
            { current = color; image.Source = Icons.Source(glyph, color); return; }
            from = current; image.Source = Icons.Source(glyph, current); clock.Start();
        }
    }

    internal static Color StateColor(Color surface, Color foreground, double opacity)
    {
        if (surface.A == 0) return Color.FromArgb((byte)Math.Round(255 * opacity), foreground.R, foreground.G, foreground.B);
        return Color.FromRgb((byte)Math.Round(surface.R + (foreground.R - surface.R) * opacity),
            (byte)Math.Round(surface.G + (foreground.G - surface.G) * opacity),
            (byte)Math.Round(surface.B + (foreground.B - surface.B) * opacity));
    }

    internal static Color HoverColor(Color surface, Color foreground) => surface == GlassSurface
        ? Color.FromArgb(152, 42, 42, 49) : surface == CommentTimeSurface ? Color.FromArgb(46, 110, 166, 255) : StateColor(surface, foreground, .08);
    internal static Color PressedColor(Color surface, Color foreground) => surface == GlassSurface
        ? Color.FromArgb(164, 48, 48, 55) : surface == CommentTimeSurface ? Color.FromArgb(60, 110, 166, 255) : StateColor(surface, foreground, .12);

    private static StyleSheet CreateStyles()
    {
        var sheet = new StyleSheet();
        Define(Filled, OnSurface, OnPrimary);
        Define(Tonal, TonalSurface, OnSurface);
        Define(Glass, GlassSurface, OnSurface);
        Define(Selected, SelectedSurface, OnSelected);
        Define(Reposted, RepostedSurface, OnReposted);
        Define(Confirmed, ConfirmedSurface, OnConfirmed);
        Define(Text, Color.Transparent, OnSurface);
        Define(CommentTime, CommentTimeSurface, OnCommentTime);
        return sheet;

        void Define(string name, Color surface, Color foreground) => sheet.Define(name, () => Style.DeriveFromDefault<Button>(
            setters: [
                Setter.Create(Control.BackgroundProperty, surface),
                Setter.Create(Control.BorderBrushProperty, Color.Transparent),
                Setter.Create(Control.BorderThicknessProperty, 0d),
                Setter.Create(UIElement.CursorProperty, (CursorType?)CursorType.Hand)
            ],
            triggers: [
                new StateTrigger { Match = VisualStateFlags.Enabled | VisualStateFlags.Hot, Setters = [
                    Setter.Create(Control.BackgroundProperty, HoverColor(surface, foreground))
                ] },
                new StateTrigger { Match = VisualStateFlags.Enabled | VisualStateFlags.Focused, Exclude = VisualStateFlags.Hot | VisualStateFlags.Pressed, Setters = [
                    Setter.Create(Control.BackgroundProperty, surface)
                ] },
                new StateTrigger { Match = VisualStateFlags.Enabled | VisualStateFlags.Pressed, Setters = [
                    Setter.Create(Control.BackgroundProperty, PressedColor(surface, foreground))
                ] },
                new StateTrigger { Exclude = VisualStateFlags.Enabled, Setters = [
                    Setter.Create(UIElement.OpacityProperty, 1d),
                    Setter.Create(UIElement.CursorProperty, (CursorType?)CursorType.Arrow)
                ] }
            ],
            transitions: [Transition.Create(Control.BackgroundProperty, 150), Transition.Create(Control.BorderBrushProperty, 150)]));
    }
}
