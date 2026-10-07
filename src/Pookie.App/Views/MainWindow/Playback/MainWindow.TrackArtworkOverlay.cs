using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class TrackArtworkOverlay : ContentControl
    {
        private readonly Image play = Icons.View("play-solid", 23, true).CenterHorizontal().CenterVertical();
        private readonly Image pause = Icons.View("pause-solid", 22, true).CenterHorizontal().CenterVertical().IsVisible(false);
        private bool hovered, active;
        internal bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public TrackArtworkOverlay()
        {
            Background = Color.FromArgb(64, 0, 0, 0);
            Padding = new Thickness(0); BorderThickness = 0; CornerRadius = 0;
            Content = new Border().Width(46).Height(46).CornerRadius(23).Background(Color.White)
                .CenterHorizontal().CenterVertical().Child(new Grid().Columns("*").Rows("*").Children(play, pause));
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
