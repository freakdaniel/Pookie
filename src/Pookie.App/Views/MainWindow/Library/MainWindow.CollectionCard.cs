using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record CollectionCardView(Button Root, Image Cover, Border Frame, TextBlock Title, TextBlock Author,
        StackPanel Subtitle, Image Followers, TrackArtworkOverlay Playback)
    {
        internal bool Artist { get; private set; }

        internal void SetArtist(bool artist)
        {
            Artist = artist;
            SetCollectionArtist(Title, Subtitle, Followers, artist);
        }

        internal void SetSize(double size)
        {
            Frame.Width = Frame.Height = Cover.Width = Cover.Height = Title.Width = size;
            Author.MaxWidth = Math.Max(0, size - (Artist ? 17 : 0));
            Frame.CornerRadius = Artist ? size / 2 : 6;
        }
    }

    private static void SetCollectionArtist(TextBlock title, StackPanel subtitle, Image followers, bool artist)
    {
        title.TextAlignment = artist ? TextAlignment.Center : TextAlignment.Left;
        subtitle.HorizontalAlignment = artist ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        followers.IsVisible = artist;
        title.Margin = new Thickness(0, artist ? 12 : 7, 0, 0);
    }

    // Library collections and track subsections share typography, artwork and
    // profile treatment; only their grid measurement differs.
    private CollectionCardView CreateCollectionCardView(bool responsive = false)
    {
        var image = new Image().StretchMode(Stretch.UniformToFill);
        var playback = new TrackArtworkOverlay();
        var frame = new Border().CornerRadius(6).ClipToBounds().Child(ArtworkLayer(image).Children(playback));
        var title = new TextBlock().FontSize(13).SemiBold().Height(20).TextTrimming(TextTrimming.CharacterEllipsis);
        var author = new TextBlock().FontSize(12).SemiBold().Height(19).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        var followers = Icons.View("user", 13, Muted).CenterVertical();
        var subtitle = new StackPanel().Horizontal().Spacing(4).Children(followers, author.CenterVertical());
        title.Margin(0, 7, 0, 0);
        var root = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Top();
        var card = new CollectionCardView(root, image, frame, title, author, subtitle, followers, playback);
        root.Content = responsive
            ? new TrackCollectionTile(frame, new StackPanel().Vertical().Spacing(1).Children(title, subtitle), resize: card.SetSize).Initialize()
            : new StackPanel().Vertical().Spacing(1).Children(frame, title, subtitle);
        ((FrameworkElement)root.Content).HorizontalAlignment = HorizontalAlignment.Stretch;
        return card;
    }
}
