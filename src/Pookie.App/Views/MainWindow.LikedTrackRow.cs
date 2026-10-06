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
        private readonly Image play = Icons.View("play-solid", 21, true).CenterHorizontal().CenterVertical();
        private readonly Image pause = Icons.View("pause-solid", 20, true).CenterHorizontal().CenterVertical().IsVisible(false);
        private readonly Image heart = Icons.View("heart-filled", 15, Muted);
        private readonly TextBlock likeCount = new TextBlock().FontSize(12).Bold().CenterVertical();
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
        public bool ShowsPause => pause.IsVisible && !play.IsVisible;

        public LikedTrackRow(Action<SoundCloudTrack> select, Action<SoundCloudTrack> like,
            Action<SoundCloudTrack> copy, Action<SoundCloudTrack, double> seek)
        {
            PlayButton = new Button().Width(38).Height(38).CornerRadius(19).Background(Color.White).BorderThickness(0).Padding(0)
                .Content(new Grid().Columns("*").Rows("*").Children(play, pause))
                .OnClick(() => { if (Track is { } track) select(track); });
            likeButton = new Button().StyleName("track-action").CornerRadius(4).Height(28).Padding(8, 3)
                .Content(new StackPanel().Horizontal().Spacing(5).Children(heart.CenterVertical(), likeCount))
                .OnClick(() => { if (Track is { } track) like(track); });
            copyButton = new Button().StyleName("track-action").CornerRadius(4).Width(30).Height(28).Padding(5)
                .ToolTip("Скопировать ссылку")
                .Content(Icons.View("copy", 16).CenterHorizontal().CenterVertical())
                .OnClick(() => { if (Track is { } track) copy(track); });
            playsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("play-solid", 12, Muted).CenterVertical(), playsCount);
            commentsStat = new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                Icons.View("chat-circle", 14, Muted).CenterVertical(), commentsCount);
            genreBadge = new Border().Background(Raised).CornerRadius(9).Padding(8, 2).Right().Child(genre);
            positionLabel = new Border().Background(Surface).Padding(3, 1).Left().Bottom().Child(position).IsVisible(false);
            Waveform.SeekRequested += fraction => { if (Track is { } track) seek(track, fraction); };
            Root = new Grid().StyleSheet(ActionStyles()).Columns("160,*").Rows("*").Spacing(20).Height(160).Children(
                new Button().Background(Color.Transparent).BorderThickness(0).Padding(0).Column(0)
                    .Content(new Border().CornerRadius(6).ClipToBounds().Width(160).Height(160).Child(Cover))
                    .OnClick(() => { if (Track is { } track) select(track); }),
                new Grid().Columns("*").Rows("40,68,28").Spacing(12).Column(1).Children(
                    new Grid().Columns("38,*,Auto").Rows("*").Spacing(10).Row(0).Children(
                        PlayButton.CenterVertical().Column(0),
                        new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(Author, Title),
                        new StackPanel().Vertical().Spacing(3).Right().CenterVertical().Column(2).Children(posted, genreBadge)),
                    new Grid().Columns("*").Rows("*").Row(1).Children(
                        Waveform,
                        positionLabel,
                        new Border().Background(Surface).Padding(3, 1).Right().Bottom().Child(duration)),
                    new Grid().Columns("Auto,*").Rows("*").Row(2).Children(
                        new StackPanel().Horizontal().Spacing(6).Children(likeButton, copyButton).Column(0),
                        new StackPanel().Horizontal().Spacing(12).Right().CenterVertical().Column(1).Children(playsStat, commentsStat))));
        }

        public void Bind(SoundCloudTrack track)
        {
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
            copyButton.IsEnabled = track.PermalinkUrl != null;
            duration.Text = FormatTime(track.DurationSeconds);
            Cover.Source = Icons.Source("music-notes");
            Waveform.SetSamples([]);
            Waveform.Progress = 0;
        }

        public void Refresh(bool selected, bool playing, bool liked, bool canLike, double seconds)
        {
            play.IsVisible = !selected || !playing;
            pause.IsVisible = selected && playing;
            heart.Source = Icons.Source("heart-filled", liked ? LikedHeart : Muted);
            likeCount.Foreground = liked ? LikedHeart : Color.FromRgb(230, 230, 230);
            likeButton.ToolTip(liked ? "Убрать из понравившегося" : "Добавить в понравившееся");
            likeButton.IsEnabled = canLike;
            positionLabel.IsVisible = selected;
            position.Text = FormatTime(selected ? seconds : 0);
            Waveform.Progress = selected && Track is { DurationSeconds: > 0 } track ? seconds / track.DurationSeconds : 0;
        }

        public void Reset() { Track = null; Waveform.SetSamples([]); Waveform.Progress = 0; positionLabel.IsVisible = false; }

        private static StyleSheet ActionStyles()
        {
            var sheet = new StyleSheet();
            sheet.Define("track-action", () => Style.DeriveFromDefault<Button>(
                setters: [
                    Setter.Create(Control.BackgroundProperty, Raised),
                    Setter.Create(Control.BorderBrushProperty, Color.FromRgb(58, 58, 58)),
                    Setter.Create(Control.BorderThicknessProperty, 1d),
                    Setter.Create(UIElement.CursorProperty, (CursorType?)CursorType.Hand)
                ],
                triggers: [
                    new StateTrigger { Match = VisualStateFlags.Enabled | VisualStateFlags.Hot, Setters = [
                        Setter.Create(Control.BackgroundProperty, Color.FromRgb(64, 64, 64)),
                        Setter.Create(Control.BorderBrushProperty, Color.FromRgb(112, 112, 112))
                    ] },
                    new StateTrigger { Match = VisualStateFlags.Enabled | VisualStateFlags.Focused, Setters = [
                        Setter.Create(Control.BorderBrushProperty, Color.FromRgb(164, 164, 164))
                    ] },
                    new StateTrigger { Match = VisualStateFlags.Enabled | VisualStateFlags.Pressed, Setters = [
                        Setter.Create(Control.BackgroundProperty, Color.FromRgb(86, 86, 86))
                    ] },
                    new StateTrigger { Exclude = VisualStateFlags.Enabled, Setters = [
                        Setter.Create(UIElement.OpacityProperty, .45),
                        Setter.Create(UIElement.CursorProperty, (CursorType?)CursorType.Arrow)
                    ] }
                ],
                transitions: [Transition.Create(Control.BackgroundProperty, 120, value => value),
                    Transition.Create(Control.BorderBrushProperty, 120, value => value)]));
            return sheet;
        }

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
