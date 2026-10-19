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
        public Image Cover { get; } = new Image().Width(160).Height(160).StretchMode(Stretch.UniformToFill);
        public TextBlock Title { get; } = new TextBlock().FontSize(15).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        public TextBlock Author { get; } = new TextBlock().FontSize(12).SemiBold().Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        public TrackWaveform Waveform { get; } = new TrackWaveform().Height(68);
        public Button PlayButton { get; }
        private readonly Image play = Icons.View("play-solid", 22, TrackButtons.OnPrimary).CenterHorizontal().CenterVertical();
        private readonly Image pause = Icons.View("pause-solid", 22, TrackButtons.OnPrimary).CenterHorizontal().CenterVertical().IsVisible(false);
        private readonly Image heart = Icons.View("heart", 18, TrackButtons.OnSurface);
        private readonly TextBlock likeCount = new TextBlock().FontSize(14).Bold().CenterVertical();
        private readonly TextBlock playsCount = new TextBlock().FontSize(11).Foreground(Muted).CenterVertical();
        private readonly TextBlock commentsCount = new TextBlock().FontSize(11).Foreground(Muted).CenterVertical();
        private readonly StackPanel playsStat;
        private readonly StackPanel commentsStat;
        private readonly Border genreBadge;
        private readonly TextBlock posted = new TextBlock().FontSize(11).Foreground(Muted).Right();
        private readonly TextBlock genre = new TextBlock().FontSize(11).Foreground(Muted).Right().TextTrimming(TextTrimming.CharacterEllipsis).MaxWidth(160);
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
        private readonly string actionVariant;
        private double footerReserve;
        private readonly TextBlock collectionTrack = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical().IsVisible(false);
        private readonly Image repostIcon = Icons.View("repeat", 20, TrackButtons.OnSurface);
        private readonly TextBlock repostCount = new TextBlock().FontSize(14).Bold().CenterVertical();
        private bool snapActionColors = true, embedded;
        public bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public LikedTrackRow(Action<SoundCloudTrack> select, Action<SoundCloudTrack> open, Action<SoundCloudTrack> like,
            Action<SoundCloudTrack> copy, Action<SoundCloudTrack> repost, Action<SoundCloudTrack> playlist, Action<SoundCloudTrack, double> seek, Func<Image, Grid> artworkLayer, bool glass = false)
        {
            actionVariant = glass ? TrackButtons.Glass : TrackButtons.Tonal;
            PlayButton = TrackButtons.Icon(new Grid().Columns("*").Rows("*").Children(play, pause),
                () => { if (Track is { } track) select(track); }, TrackButtons.Filled);
            likeButton = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(heart.CenterVertical(), likeCount),
                () => { if (Track is { } track) like(track); }, actionVariant);
            copyButton = TrackButtons.Icon(Icons.View("copy", 20, TrackButtons.OnSurface),
                () => { if (Track is { } track) copy(track); }, actionVariant).ToolTip("Скопировать ссылку");
            repostButton = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(repostIcon.CenterVertical(), repostCount),
                () => { if (Track is { } track) repost(track); }, actionVariant).ToolTip("Сделать репост");
            var playlistButton = TrackButtons.Icon(Icons.View("playlist-plus", 20, TrackButtons.OnSurface),
                () => { if (Track is { } track) playlist(track); }, actionVariant).ToolTip("Добавить в плейлист");
            playsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("play-solid", 12, Muted).CenterVertical(), playsCount);
            commentsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("chat-circle", 14, Muted).CenterVertical(), commentsCount);
            genreBadge = new Border().Background(Raised).CornerRadius(9).Padding(8, 2).Right().Child(genre);
            positionLabel = new Border().Background(Surface).Padding(3, 1).Left().Bottom().Child(position).IsVisible(false);
            durationLabel = new Border().Background(Surface).Padding(3, 1).Right().Bottom().Child(duration);
            Waveform.SeekRequested += fraction => { if (Track is { } track) seek(track, fraction); };
            artworkFrame = new Border().CornerRadius(6).ClipToBounds().Width(160).Height(160).Child(artworkLayer(Cover));
            metadata = new StackPanel().Vertical().Spacing(3).Right().CenterVertical().Children(posted, genreBadge);
            actionButtons = new StackPanel().Horizontal().Spacing(TrackButtons.ActionSpacing).Children(TrackButtons.Connected(likeButton, repostButton), playlistButton, copyButton);
            actions = new Grid().Columns("Auto,*").Rows("*").Children(
                actionButtons.Column(0),
                new StackPanel().Horizontal().Spacing(12).Right().CenterVertical().Column(1).Children(playsStat, commentsStat));
            details = new Grid().Columns("*").Rows("40,68,40").Spacing(12);
            Root = new Grid().Columns("160,*").Rows("*").Spacing(20).Height(TrackRowLayout.Height).Children(
                new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Column(0).CenterVertical()
                    .Content(artworkFrame)
                    .OnClick(() => { if (Track is { } track) open(track); }),
                details.Column(1).Children(
                    new Grid().Columns("40,*,Auto").Rows("*").Spacing(10).Row(0).Children(
                        PlayButton.CenterVertical().Column(0),
                        new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(Author, Title),
                        metadata.Column(2)),
                    new Grid().Columns("*").Rows("*").Row(1).Children(
                        Waveform,
                        positionLabel,
                        durationLabel),
                    new Grid().Columns("*").Rows("*").Row(2).Children(actions, collectionTrack)));
        }

        // Embedded rows keep the library's waveform and controls, while allowing
        // their artwork and secondary metadata to fit a narrower conversation.
        public void FitEmbedded(double width)
        {
            embedded = true; positionLabel.Background = durationLabel.Background = Color.Transparent;
            Root.Height = TrackRowLayout.EmbeddedHeight; Waveform.Height = 56;
            if (!details.RowDefinitions[1].Height.Equals(new GridLength(56))) details.RowDefinitions[1].Height = new GridLength(56);
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

        public void ReserveFooter(double width)
        {
            footerReserve = width;
            actions.Margin = collectionTrack.Margin = new Thickness(0, 0, width, 0);
        }

        public void SetCollectionContext(string? text)
        { actions.IsVisible = text == null; collectionTrack.IsVisible = text != null; collectionTrack.Text = text ?? ""; }

        public void Bind(SoundCloudTrack track)
        {
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
            positionLabel.IsVisible = selected;
            position.Text = FormatTime(selected ? seconds : 0);
            Waveform.IsPlaying = selected && playing;
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

        public void Reset() { ClearActionFocus(); Track = null; Waveform.SetSamples([]); Waveform.IsPlaying = false; Waveform.Progress = 0; positionLabel.IsVisible = false; }

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
