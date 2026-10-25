using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class LikedTrackRow
    {
        public SoundCloudTrack? Track { get; private set; }
        public Grid Root { get; }
        public Image Cover { get; } = new Image().Width(TrackRowLayout.CoverSize).Height(TrackRowLayout.CoverSize).StretchMode(Stretch.UniformToFill);
        public TextBlock Title { get; } = new TextBlock().FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        public TextBlock Author { get; } = new TextBlock().FontSize(11.5).SemiBold().Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        public TrackWaveform Waveform { get; } = new TrackWaveform().Height(TrackRowLayout.WaveformHeight);
        public Image TrackArt { get; } = new Image().StretchMode(Stretch.UniformToFill).IsHitTestVisible(false);
        private readonly Border trackArtFrame;
        public CoverGlassBackdrop TrackArtGlass { get; }
        public Button PlayButton { get; }
        private readonly Image play = Icons.View("play-solid", 20, TrackButtons.OnPrimary).CenterHorizontal().CenterVertical();
        private readonly Image pause = Icons.View("pause-solid", 20, TrackButtons.OnPrimary).CenterHorizontal().CenterVertical().IsVisible(false);
        private readonly Image heart = Icons.View("heart", 17, TrackButtons.OnSurface);
        private readonly TextBlock likeCount = new TextBlock().FontSize(13).Bold().CenterVertical();
        private readonly TextBlock playsCount = new TextBlock().FontSize(11).Foreground(Muted).CenterVertical();
        private readonly TextBlock commentsCount = new TextBlock().FontSize(11).Foreground(Muted).CenterVertical();
        private readonly StackPanel playsStat;
        private readonly StackPanel commentsStat;
        private readonly Border genreBadge;
        private readonly TextBlock posted = new TextBlock().FontSize(10.5).Foreground(Muted).Right();
        private readonly TextBlock genre = new TextBlock().FontSize(10.5).Foreground(Muted).Right().TextTrimming(TextTrimming.CharacterEllipsis).MaxWidth(160);
        private readonly TextBlock position = new TextBlock().FontSize(10);
        private readonly TextBlock duration = new TextBlock().FontSize(10);
        private readonly Border positionLabel;
        private readonly Border durationLabel;
        private readonly Button likeButton;
        private readonly Button copyButton;
        private readonly Button repostButton;
        private readonly Border artworkFrame;
        private readonly StackPanel metadata;
        private readonly StackPanel actionButtons;
        private readonly Grid actions;
        private readonly Grid details;
        private readonly Grid heading, wave;
        private readonly string actionVariant;
        private readonly bool publication;
        private double footerReserve;
        private readonly TextBlock collectionTrack = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical().IsVisible(false);
        private readonly Image repostIcon = Icons.View("repeat", 18, TrackButtons.OnSurface);
        private readonly TextBlock repostCount = new TextBlock().FontSize(13).Bold().CenterVertical();
        private bool snapActionColors = true, embedded;
        public bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public LikedTrackRow(Action<SoundCloudTrack> select, Action<SoundCloudTrack> open, Action<SoundCloudTrack> like,
            Action<SoundCloudTrack> copy, Action<SoundCloudTrack> repost, Action<SoundCloudTrack> playlist, Action<SoundCloudTrack, double> seek, Func<Image, Grid> artworkLayer, bool glass = false, bool publication = false, Func<TextBlock, FrameworkElement>? byline = null)
        {
            this.publication = publication;
            actionVariant = glass ? TrackButtons.Glass : TrackButtons.Tonal;
            PlayButton = TrackButtons.Icon(new Grid().Columns("*").Rows("*").Children(play, pause),
                () => { if (Track is { } track) select(track); }, TrackButtons.Filled, TrackRowLayout.ActionSize);
            likeButton = TrackButtons.Action(new StackPanel().Horizontal().Spacing(6).Children(heart.CenterVertical(), likeCount),
                () => { if (Track is { } track) like(track); }, actionVariant, TrackRowLayout.ActionSize);
            copyButton = TrackButtons.Icon(Icons.View("copy", 18, TrackButtons.OnSurface),
                () => { if (Track is { } track) copy(track); }, actionVariant, TrackRowLayout.ActionSize).ToolTip("Скопировать ссылку");
            repostButton = TrackButtons.Action(new StackPanel().Horizontal().Spacing(6).Children(repostIcon.CenterVertical(), repostCount),
                () => { if (Track is { } track) repost(track); }, actionVariant, TrackRowLayout.ActionSize).ToolTip("Сделать репост");
            var playlistButton = TrackButtons.Icon(Icons.View("playlist-plus", 18, TrackButtons.OnSurface),
                () => { if (Track is { } track) playlist(track); }, actionVariant, TrackRowLayout.ActionSize).ToolTip("Добавить в плейлист");
            playsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("play-solid", 12, Muted).CenterVertical(), playsCount);
            commentsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("chat-circle", 14, Muted).CenterVertical(), commentsCount);
            genreBadge = new Border().Background(Raised).CornerRadius(9).Padding(8, 2).Right().Child(genre);
            positionLabel = new Border().Background(Surface).Padding(3, 1).Left().Bottom().Child(position).IsVisible(false);
            durationLabel = new Border().Background(Surface).Padding(3, 1).Right().Bottom().Child(duration);
            Waveform.SeekRequested += fraction => { if (Track is { } track) seek(track, fraction); };
            artworkFrame = new Border().CornerRadius(TrackRowLayout.CornerRadius).ClipToBounds().Width(TrackRowLayout.CoverSize).Height(TrackRowLayout.CoverSize).Child(artworkLayer(Cover));
            metadata = new StackPanel().Vertical().Spacing(3).Right().CenterVertical().Children(posted, genreBadge);
            actionButtons = new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Children(TrackButtons.Connected(likeButton, repostButton), playlistButton, copyButton);
            actions = new Grid().Columns("Auto,*").Rows("*").Children(
                actionButtons.Column(0),
                new StackPanel().Horizontal().Spacing(12).Right().CenterVertical().Column(1).Children(playsStat, commentsStat));
            details = new Grid().Columns("*").Rows("36,56,36").Spacing(16).Padding(TrackRowLayout.ContentInset, TrackRowLayout.VerticalInset);
            trackArtFrame = new Border().CornerRadius(TrackRowLayout.CornerRadius).ClipToBounds()
                .IsHitTestVisible(false).IsVisible(false).Child(
                    new Grid().Columns("*").Rows("*").Children(TrackArt,
                        new Border().Background(ArtworkBackdrop.TrackArtScrim).IsHitTestVisible(false)));
            var artworkButton = new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Column(0).CenterVertical()
                .Content(artworkFrame).OnClick(() => { if (Track is { } track) open(track); });
            heading = new Grid().Columns("36,*,Auto").Rows("*").Spacing(10).Children(
                PlayButton.CenterVertical().Column(0),
                new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(byline?.Invoke(Author) ?? Author, Title), metadata.Column(2));
            wave = new Grid().Columns("*").Rows("*")
                .Children(Waveform, positionLabel, durationLabel);
            if (publication) metadata.IsVisible = false;
            Root = new ViewportGrid().Columns($"{TrackRowLayout.CoverSize},*").Rows("*").Spacing(TrackRowLayout.ColumnGap).Height(TrackRowLayout.Height).Children(
                artworkButton, new Grid().Columns("*").Rows("*").Column(1).Children(trackArtFrame,
                    details.Children(heading.Row(0), wave.Row(1), new Grid().Columns("*").Rows("*").Row(2).Children(actions, collectionTrack))));
            if (publication) Root.SizeChanged += _ =>
            {
                FitPublicationActions();
                // Visibility/width changes during arrange need a fresh measure
                // after that pass, otherwise counters retain their compact bounds.
                Application.Current.Dispatcher?.BeginInvoke(() => Root.InvalidateMeasure());
            };
            TrackArtGlass = new(TrackArt, background: trackArtFrame);
            ReserveFooter(0);
        }

        // Embedded rows keep the library's waveform and controls, while allowing
        // their artwork and secondary metadata to fit a narrower conversation.
        public void FitEmbedded(double width)
        {
            embedded = true; positionLabel.Background = durationLabel.Background = Color.Transparent;
            Root.Height = TrackRowLayout.EmbeddedHeight; Waveform.Height = 56;
            if (!details.RowDefinitions[0].Height.Equals(new GridLength(40))) details.RowDefinitions[0].Height = new GridLength(40);
            if (!details.RowDefinitions[1].Height.Equals(new GridLength(56))) details.RowDefinitions[1].Height = new GridLength(56);
            if (!details.RowDefinitions[2].Height.Equals(new GridLength(40))) details.RowDefinitions[2].Height = new GridLength(40);
            details.Spacing = 12; details.Padding = new Thickness(0);
            heading.ColumnDefinitions[0].Width = new GridLength(40);
            Title.FontSize = 15; Author.FontSize = 12;
            PlayButton.Width = PlayButton.Height = 40; PlayButton.CornerRadius = 20;
            VisualTree.Visit(actionButtons, element =>
            {
                if (element is not Button button) return;
                if (button.Width == button.Height) button.Width = 40;
                button.Height = 40; button.CornerRadius = 20;
            });
            ReserveFooter(footerReserve);
            var size = Math.Clamp(width - 360, 72, 160);
            if (Cover.Width != size)
            {
                Cover.Width = Cover.Height = artworkFrame.Width = artworkFrame.Height = size;
                Root.ColumnDefinitions[0].Width = new GridLength(size);
            }
            Root.Spacing = width < 540 ? 12 : 20;
            metadata.IsVisible = false;
            playsStat.IsVisible = commentsStat.IsVisible = false;
            var compact = width - footerReserve < 480;
            likeCount.IsVisible = !compact;
            likeButton.Width = compact ? 40 : double.NaN;
            likeButton.Padding = new Thickness(compact ? 0 : 16, 0);
            TrackButtons.SetCounter(repostButton, repostCount, compact ? "" : Track?.RepostsCount is > 0 ? ShortCount(Track.RepostsCount.Value) : "");
            actionButtons.Spacing = compact ? 6 : TrackButtons.ActionSpacing;
        }

        private void FitPublicationActions()
        {
            if (!publication) return;
            var width = Math.Max(0, Root.ActualWidth - TrackRowLayout.CoverSize - TrackRowLayout.ColumnGap - 2 * TrackRowLayout.ContentInset);
            var compact = width < 360;
            likeCount.IsVisible = !compact;
            likeButton.Width = compact ? TrackRowLayout.ActionSize : double.NaN;
            likeButton.Padding = new Thickness(compact ? 0 : 16, 0);
            TrackButtons.SetCounter(repostButton, repostCount, compact ? "" : Track?.RepostsCount is > 0 ? ShortCount(Track.RepostsCount.Value) : "");
            playsStat.IsVisible = width >= 480 && Track?.PlaybackCount != null;
            commentsStat.IsVisible = width >= 480 && Track?.CommentCount != null;
        }

        public void AddQueueAction(Action<SoundCloudTrack> enqueue) => actionButtons.Add(
            TrackButtons.Icon(Icons.View("queue", 18, TrackButtons.OnSurface), () => { if (Track is { } track) enqueue(track); }, actionVariant,
                embedded ? 40 : TrackRowLayout.ActionSize).ToolTip("Добавить в очередь"));

        public void ReserveFooter(double width)
        {
            footerReserve = width;
            actions.Margin = collectionTrack.Margin = new Thickness(0, 0, width, 0);
        }

        public void SetCollectionContext(string? text)
        { actions.IsVisible = text == null; collectionTrack.IsVisible = text != null; collectionTrack.Text = text ?? ""; }

        public void Bind(SoundCloudTrack track)
        {
            SetTrackArt(null);
            if (Track?.Id != track.Id)
            {
                ClearActionFocus(); snapActionColors = true;
                TrackButtons.SnapNextChange(likeButton); TrackButtons.SnapNextChange(repostButton);
            }
            Track = track;
            Title.Text = track.Title; Author.Text = track.Author;
            posted.Text = PostedAt(track.CreatedAt);
            genre.Text = track.Genre ?? "";
            likeCount.Text = track.LikesCount is { } likes ? ShortCount(likes) : "Лайк";
            TrackButtons.SetCounter(repostButton, repostCount, track.RepostsCount is { } reposts && reposts > 0 ? ShortCount(reposts) : "");
            genreBadge.IsVisible = !string.IsNullOrWhiteSpace(track.Genre);
            playsStat.IsVisible = !embedded && track.PlaybackCount != null;
            commentsStat.IsVisible = !embedded && track.CommentCount != null;
            playsCount.Text = ShortCount(track.PlaybackCount ?? 0);
            commentsCount.Text = ShortCount(track.CommentCount ?? 0);
            copyButton.IsEnabled = TrackLink(track.PermalinkUrl) != null || track.Id > 0;
            duration.Text = FormatTime(track.DurationSeconds);
            Cover.Source = Icons.Source("music-notes");
            Waveform.SetSamples([]);
            Waveform.IsPlaying = false;
            Waveform.Progress = 0;
            FitPublicationActions();
        }

        public void Refresh(bool selected, bool playing, bool liked, bool canLike, double seconds, bool pending = false)
        {
            play.IsVisible = !selected || !playing;
            pause.IsVisible = selected && playing;
            TrackButtons.SetGlyph(heart, liked ? "heart-filled" : "heart", liked ? TrackButtons.OnSelected : TrackButtons.OnSurface, !snapActionColors);
            TrackButtons.SetTextColor(likeCount, liked ? TrackButtons.OnSelected : TrackButtons.OnSurface, !snapActionColors);
            TrackButtons.SetLiked(likeButton, liked, actionVariant);
            PlayButton.ToolTip(selected && playing ? "Приостановить" : "Воспроизвести");
            likeButton.ToolTip(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
            TrackButtons.SetAvailability(likeButton, canLike, pending);
            snapActionColors = false;
            Waveform.IsPlaying = selected && playing;
            RefreshPosition(selected, seconds);
        }

        public void RefreshPosition(bool selected, double seconds)
        {
            positionLabel.IsVisible = selected;
            position.Text = FormatTime(selected ? seconds : 0);
            Waveform.Progress = selected && Track is { DurationSeconds: > 0 } track ? seconds / track.DurationSeconds : 0;
        }

        private void ClearActionFocus()
        {
            if (Root.IsFocusWithin && Root.FindVisualRoot() is Window window) window.FocusManager.ClearFocus();
        }

        public void RefreshRepost(bool reposted, bool available, bool pending = false)
        {
            TrackButtons.SetReposted(repostButton, reposted, actionVariant);
            TrackButtons.SetGlyph(repostIcon, "repeat", reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface, !snapActionColors);
            TrackButtons.SetTextColor(repostCount, reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface, !snapActionColors);
            TrackButtons.SetAvailability(repostButton, available, pending);
            repostButton.ToolTip(reposted ? "Убрать репост" : "Сделать репост");
        }

        public void SetTrackArt(ImageSource? source)
        {
            TrackArt.Source = source; trackArtFrame.IsVisible = source != null;
            Waveform.OnArtwork = source != null;
            if (source == null) TrackArtGlass.Reset();
            else TrackArtGlass.RefreshArtwork();
            VisualTree.Visit(actionButtons, element =>
            {
                if (element is MaterialTrackButton button) button.AttachGlassBackdrop(source == null ? null : TrackArtGlass);
            });
        }

        public void PrepareTrackArtGlass(Rect viewport)
        {
            if (TrackArt.Source == null || !Root.Bounds.IntersectsWith(viewport) || Root.FindVisualRoot() is not Aprillz.MewUI.Window) return;
            for (Element? element = Root; element != null; element = element.Parent)
                if (element is UIElement { IsVisible: false }) return;
            TrackArtGlass.RefreshArtwork(); TrackArtGlass.Prepare();
        }

        public void Reset() { ClearActionFocus(); Track = null; SetTrackArt(null); Waveform.SetSamples([]); Waveform.IsPlaying = false; Waveform.Progress = 0; positionLabel.IsVisible = false; }

        private static string ShortCount(long value) => value switch {
            >= 1000000 => (value / 1000000d).ToString("0.#", CultureInfo.InvariantCulture) + "M",
            >= 1000 => (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "K",
            _ => value.ToString(CultureInfo.InvariantCulture) };

        private static string PostedAt(string? value)
        {
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return "";
            var days = Math.Max(0, (DateTimeOffset.UtcNow - date).TotalDays);
            return days switch { < 1 => "Сегодня", < 30 => $"{(int)days} дн. назад", < 365 => $"{(int)(days / 30)} мес. назад", _ => $"{(int)(days / 365)} г. назад" };
        }
    }
}
