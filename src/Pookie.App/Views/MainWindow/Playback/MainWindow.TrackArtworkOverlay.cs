using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class TrackArtworkOverlay : ContentControl
    {
        private const double DefaultButtonSize = 46;
        private readonly Image play, pause;
        private bool hovered, active;
        internal Button? PlaybackButton { get; }
        internal bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public TrackArtworkOverlay(Action? onPlay = null, double buttonSize = DefaultButtonSize)
        {
            var scale = buttonSize / DefaultButtonSize;
            play = Icons.View("play-solid", (int)Math.Round(23 * scale), true).CenterHorizontal().CenterVertical();
            pause = Icons.View("pause-solid", (int)Math.Round(22 * scale), true).CenterHorizontal().CenterVertical().IsVisible(false);
            Background = Color.FromArgb(64, 0, 0, 0);
            Padding = new Thickness(0); BorderThickness = 0; CornerRadius = 0;
            var glyphs = new Grid().Columns("*").Rows("*").Children(play, pause);
            if (onPlay == null)
                Content = new Border().Width(buttonSize).Height(buttonSize).CornerRadius(buttonSize / 2).Background(Color.White)
                    .CenterHorizontal().CenterVertical().Child(glyphs);
            else
            {
                PlaybackButton = TrackButtons.Icon(glyphs, onPlay, TrackButtons.Filled, buttonSize).Center();
                Content = PlaybackButton;
            }
            Opacity = 0;
            Transitions = [Transition.Create(UIElement.OpacityProperty, 180, Easing.CubicBezier(.2, 0, 0, 1))];
        }

        public void SetHovered(bool value) { hovered = value; Refresh(); }
        public void SetPlaying(bool selected, bool playing, bool animate = true)
        {
            active = selected;
            play.IsVisible = !selected || !playing;
            pause.IsVisible = selected && playing;
            if (animate) Refresh();
            else SetImmediately(hovered || active ? 1 : 0);
        }

        public void Reset()
        {
            hovered = active = false;
            SetImmediately(0);
            play.IsVisible = true; pause.IsVisible = false;
        }

        private void Refresh() => Opacity = hovered || active ? 1 : 0;
        private void SetImmediately(double opacity)
        {
            var transitions = Transitions;
            Transitions = null;
            Opacity = opacity;
            Transitions = transitions;
        }
    }
}
