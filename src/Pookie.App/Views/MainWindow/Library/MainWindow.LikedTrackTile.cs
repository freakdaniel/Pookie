using Aprillz.MewUI;
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
        private readonly TrackArtworkOverlay overlay;
        internal Button PlaybackButton => overlay.PlaybackButton!;
        internal bool ShowsPause => overlay.ShowsPause;

        public LikedTrackTile(MainWindow owner, Action<SoundCloudTrack> select, Action<SoundCloudTrack> open, string marker = "heart-filled")
        {
            overlay = owner.CreateCoverPlayback(Cover, () => { if (Track is { } track) select(track); });
            var zoom = new ArtworkHoverZoom(owner.ArtworkLayer(Cover).Children(overlay));
            artworkFrame = new Border().CornerRadius(6).ClipToBounds()
                .Child(zoom);
            Root = new Button().Background(Color.Transparent).BorderBrush(Color.Transparent).BorderThickness(0).Padding(0).Top()
                .Content(new StackPanel().Vertical().Spacing(1).Children(
                    artworkFrame,
                    new Grid().Columns("14,*").Rows("20").Spacing(4).Margin(0, 7, 0, 0).Children(
                        Icons.View(marker, 13, MutedIcon).CenterVertical().Column(0), Title.Column(1)),
                    Author))
                .OnMouseEnter(() => overlay.SetHovered(Track != null))
                .OnMouseLeave(() => overlay.SetHovered(false))
                .OnClick(() => { if (Track is { } track) open(track); });
            zoom.Follow(Root);
            owner.AttachUserLink(Author, () => Track?.User);
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
            overlay.SetHovered(Track != null && Root.IsMouseOver);
            overlay.SetPlaying(selected, playing, animate);
        }
        internal double OverlayOpacity => overlay.Opacity;
        public void Reset() => overlay.Reset();
    }
}
