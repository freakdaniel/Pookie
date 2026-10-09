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
        private readonly Button likeButton;
        private readonly Button copyButton;
        private readonly Button repostButton;
        private readonly Image repostIcon = Icons.View("repeat", 20, TrackButtons.OnSurface);
        private bool snapActionColors = true;
        public bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public LikedTrackRow(Action<SoundCloudTrack> select, Action<SoundCloudTrack> like,
            Action<SoundCloudTrack> copy, Action<SoundCloudTrack> repost, Action<SoundCloudTrack> playlist, Action<SoundCloudTrack, double> seek, Func<Image, Grid> artworkLayer)
        {
            PlayButton = TrackButtons.Icon(new Grid().Columns("*").Rows("*").Children(play, pause),
                () => { if (Track is { } track) select(track); }, TrackButtons.Filled);
            likeButton = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(heart.CenterVertical(), likeCount),
                () => { if (Track is { } track) like(track); });
            copyButton = TrackButtons.Icon(Icons.View("copy", 20, TrackButtons.OnSurface),
                () => { if (Track is { } track) copy(track); }).ToolTip("Скопировать ссылку");
            repostButton = TrackButtons.Icon(repostIcon, () => { if (Track is { } track) repost(track); }).ToolTip("Сделать репост");
            var playlistButton = TrackButtons.Icon(Icons.View("playlist-plus", 20, TrackButtons.OnSurface),
                () => { if (Track is { } track) playlist(track); }).ToolTip("Добавить в плейлист");
            playsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("play-solid", 12, Muted).CenterVertical(), playsCount);
            commentsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("chat-circle", 14, Muted).CenterVertical(), commentsCount);
            genreBadge = new Border().Background(Raised).CornerRadius(9).Padding(8, 2).Right().Child(genre);
            positionLabel = new Border().Background(Surface).Padding(3, 1).Left().Bottom().Child(position).IsVisible(false);
            Waveform.SeekRequested += fraction => { if (Track is { } track) seek(track, fraction); };
            Root = new Grid().Columns("160,*").Rows("*").Spacing(20).Height(TrackRowLayout.Height).Children(
                new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Column(0).CenterVertical()
                    .Content(new Border().CornerRadius(6).ClipToBounds().Width(160).Height(160).Child(artworkLayer(Cover)))
                    .OnClick(() => { if (Track is { } track) select(track); }),
                new Grid().Columns("*").Rows("40,68,40").Spacing(12).Column(1).Children(
                    new Grid().Columns("40,*,Auto").Rows("*").Spacing(10).Row(0).Children(
                        PlayButton.CenterVertical().Column(0),
                        new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(Author, Title),
                        new StackPanel().Vertical().Spacing(3).Right().CenterVertical().Column(2).Children(posted, genreBadge)),
                    new Grid().Columns("*").Rows("*").Row(1).Children(
                        Waveform,
                        positionLabel,
                        new Border().Background(Surface).Padding(3, 1).Right().Bottom().Child(duration)),
                    new Grid().Columns("Auto,*").Rows("*").Row(2).Children(
                        new StackPanel().Horizontal().Spacing(8).Children(likeButton, repostButton, playlistButton, copyButton).Column(0),
                        new StackPanel().Horizontal().Spacing(12).Right().CenterVertical().Column(1).Children(playsStat, commentsStat))));
        }

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
            genreBadge.IsVisible = !string.IsNullOrWhiteSpace(track.Genre);
            playsStat.IsVisible = track.PlaybackCount != null;
            commentsStat.IsVisible = track.CommentCount != null;
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
            TrackButtons.SetLiked(likeButton, liked);
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
            TrackButtons.SetReposted(repostButton, reposted);
            TrackButtons.SetGlyph(repostIcon, "repeat", reposted ? TrackButtons.OnReposted : TrackButtons.OnSurface, !snapActionColors);
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
