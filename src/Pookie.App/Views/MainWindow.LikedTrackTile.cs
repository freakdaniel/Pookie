using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class LikedTrackTile
    {
        public SoundCloudTrack? Track { get; set; }
        public Button Root { get; }
        public Image Cover { get; } = new Image().StretchMode(Stretch.UniformToFill);
        public TextBlock Title { get; } = new TextBlock().FontSize(13).SemiBold().Height(20)
            .TextTrimming(TextTrimming.CharacterEllipsis);
        public TextBlock Author { get; } = new TextBlock().FontSize(12).SemiBold().Foreground(Muted).Height(19)
            .TextTrimming(TextTrimming.CharacterEllipsis);
        private readonly Border artworkFrame;
        private readonly Border overlay;
        private readonly Image play;
        private readonly Image pause;
        private bool hovered, active;
        internal bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public LikedTrackTile(Action<SoundCloudTrack> select)
        {
            play = Icons.View("play-solid", 23, true).CenterHorizontal().CenterVertical();
            pause = Icons.View("pause-solid", 22, true).CenterHorizontal().CenterVertical().IsVisible(false);
            overlay = new Border().Background(Color.FromArgb(64, 0, 0, 0))
                .Child(new Border().Width(46).Height(46).CornerRadius(23).Background(Color.White)
                    .CenterHorizontal().CenterVertical().Child(new Grid().Columns("*").Rows("*").Children(play, pause)));
            overlay.Opacity = 0;
            overlay.Transitions = [Transition.Create(UIElement.OpacityProperty, 180, Easing.CubicBezier(0.2, 0, 0, 1))];
            artworkFrame = new Border().CornerRadius(6).ClipToBounds()
                .Child(new Grid().Columns("*").Rows("*").Children(Cover, overlay));
            Root = new Button().Background(Color.Transparent).BorderBrush(Color.Transparent).BorderThickness(0).Padding(0).Top()
                .Content(new StackPanel().Vertical().Spacing(1).Children(
                    artworkFrame,
                    new Grid().Columns("14,*").Rows("20").Spacing(4).Margin(0, 7, 0, 0).Children(
                        Icons.View("heart-filled", 13, Muted).CenterVertical().Column(0), Title.Column(1)),
                    Author))
                .OnMouseEnter(() => { hovered = true; RefreshOverlay(); })
                .OnMouseLeave(() => { hovered = false; RefreshOverlay(); })
                .OnClick(() => { if (Track is { } track) select(track); });
        }

        public void SetSize(double size)
        {
            Root.Width = size;
            artworkFrame.Width = size; artworkFrame.Height = size;
            Cover.Width = size; Cover.Height = size;
            Author.Width = size;
        }

        public void SetPlaying(bool selected, bool playing, bool animate = true)
        {
            active = selected;
            play.IsVisible = !selected || !playing;
            pause.IsVisible = selected && playing;
            if (animate) RefreshOverlay();
            else SetOverlayImmediately(hovered || active ? 1 : 0);
        }

        internal double OverlayOpacity => overlay.Opacity;

        private void SetOverlayImmediately(double opacity)
        {
            var transitions = overlay.Transitions;
            overlay.Transitions = null;
            overlay.Opacity = opacity;
            overlay.Transitions = transitions;
        }

        public void Reset()
        {
            hovered = active = false;
            SetOverlayImmediately(0);
            play.IsVisible = true; pause.IsVisible = false;
        }

        private void RefreshOverlay() => overlay.Opacity = hovered || active ? 1 : 0;
    }
}
