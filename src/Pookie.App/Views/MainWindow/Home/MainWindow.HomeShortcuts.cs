using Aprillz.MewUI;
using Pookie.App.Home;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBlock homeLikesSubtitle = null!, homeHistorySubtitle = null!;
    private Button homeLikesCard = null!, homeHistoryCard = null!;
    private readonly List<(Image Image, Border Frame)> homeLikesCovers = [];
    private readonly string?[] homeLikesCoverKeys = new string?[2];
    private HomeShortcutCoverStack homeHistoryCoverStack = null!;
    private string? homeHistoryFrontKey, homeHistoryRearKey;
    private int homeHistoryCoverGeneration;
    private int? homeLikesCount;
    private string? homeHistoryTitle;
    private int homeHistoryTitleGeneration;

    private FrameworkElement HomeShortcutsView()
    {
        Button Card(string title, string illustration, bool history, Action open, out TextBlock subtitle)
        {
            subtitle = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
            subtitle.Transitions = [Transition.Create(UIElement.OpacityProperty, 170, Easing.CubicBezier(.2, 0, 0, 1))];
            FrameworkElement previews;
            if (history)
            {
                homeHistoryCoverStack = new HomeShortcutCoverStack().CenterVertical();
                previews = homeHistoryCoverStack;
            }
            else
            {
                var stack = new Grid().Columns("*").Rows("*").Width(48).Height(48).CenterVertical();
                var frames = new Border[2];
                for (var index = 0; index < frames.Length; index++)
                {
                    var image = new Image().Width(40).Height(40).StretchMode(Stretch.UniformToFill);
                    var frame = new Border().Width(40).Height(40).CornerRadius(5).ClipToBounds().Background(Raised).Child(image)
                        .Margin(index == 0 ? new Thickness(0, 8, 8, 0) : new Thickness(8, 0, 0, 8));
                    frame.Opacity = 0;
                    frame.Transitions = [Transition.Create(UIElement.OpacityProperty, 190, Easing.CubicBezier(.2, 0, 0, 1))];
                    homeLikesCovers.Add((image, frame)); frames[index] = frame;
                }
                // The latest cover is in front, offset to the southwest of the faded back cover.
                stack.Children(frames[1], frames[0]);
                previews = stack;
            }
            var content = new Grid().Columns("56,*,Auto").Rows("*").Spacing(14).Children(
                new Image().Source(ImageSource.FromResource<MainWindow>($"Pookie.App.Assets.Stickle.{illustration}.png"))
                    .Width(56).Height(56).StretchMode(Stretch.Uniform).CenterVertical().Column(0),
                new StackPanel().Vertical().Spacing(5).CenterVertical().Column(1).Children(
                    new TextBlock().Text(title).FontSize(18).Bold(), subtitle), previews.Column(2));
            var card = TrackButtons.Action(content, open, TrackButtons.Tonal, 88).CornerRadius(16).Padding(12);
            ((MaterialTrackButton)card).Palette(TrackButtons.TonalSurface, TrackButtons.OnSurface);
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            return card;
        }
        homeLikesCard = Card("Мне нравится", "pixel-heart", history: false,
            () => Run(() => LikesAsync(showAll: true)), out homeLikesSubtitle);
        homeHistoryCard = Card("История", "pixel-clock", history: true,
            () => Run(() => ShowLibrarySectionAsync(Page.LibraryHistory)), out homeHistorySubtitle);
        return new Grid().Columns("*,*").Rows("Auto").Spacing(16).Margin(0, 0, 18, 0)
            .Children(homeLikesCard.Column(0), homeHistoryCard.Column(1));
    }
    private void RefreshHomeShortcuts()
    {
        if (homeLikesSubtitle == null) return;
        var count = likedIdsReady ? likedIds.Count : homeLikesCount;
        homeLikesSubtitle.Text = count is { } total ? DetailCount(total) + " " + RussianPlural(total, "трек", "трека", "треков") : "Сохранённые треки";
        var likes = (libraryLikes?.Tracks ?? waveTaste.Snapshot().Tracks.Where(item => item.Liked).Select(item => item.Track.ToTrack()).ToArray())
            .Take(2).Select(LibraryItem.FromTrack).ToArray();
        var history = localRecent.Concat(HomeItems("recent")).Where(item => item.Track != null || item.Playlist != null)
            .DistinctBy(item => item.Key).Take(2).ToArray();
        var latestHistoryTitle = history.FirstOrDefault()?.Title ?? "Недавние прослушивания";
        if (homeHistoryTitle == null)
        {
            homeHistoryTitle = latestHistoryTitle;
            homeHistorySubtitle.Text = latestHistoryTitle;
            homeHistorySubtitle.Opacity = 1;
        }
        else if (homeHistoryTitle != latestHistoryTitle)
        {
            homeHistoryTitle = latestHistoryTitle;
            var generation = ++homeHistoryTitleGeneration;
            Run(async () =>
            {
                homeHistorySubtitle.Opacity = 0;
                await Task.Delay(170, lifetime.Token);
                if (disposed || generation != homeHistoryTitleGeneration) return;
                homeHistorySubtitle.Text = latestHistoryTitle;
                homeHistorySubtitle.Opacity = 1;
            });
        }
        for (var index = 0; index < homeLikesCovers.Count; index++)
        {
            var item = likes.ElementAtOrDefault(index);
            var (image, frame) = homeLikesCovers[index];
            frame.IsVisible = item != null;
            if (homeLikesCoverKeys[index] == item?.Key) continue;
            homeLikesCoverKeys[index] = item?.Key;
            if (item == null)
            {
                frame.Opacity = 0;
                image.Source = null;
                continue;
            }
            var slot = index; var generation = waveGeneration;
            var oldCoverVisible = image.Source != null;
            if (oldCoverVisible) frame.Opacity = 0;
            Run(async () => {
                if (oldCoverVisible) await Task.Delay(190, lifetime.Token);
                var source = await GetCollectionArtworkAsync(item);
                if (disposed || generation != waveGeneration || homeLikesCoverKeys[slot] != item.Key) return;
                image.Source = source;
                frame.IsVisible = source != null;
                frame.Opacity = source == null ? 0 : slot == 0 ? 1 : .45;
            });
        }
        RefreshHomeHistoryCovers(history);
    }

    private void RefreshHomeHistoryCovers(LibraryItem[] history)
    {
        var front = history.ElementAtOrDefault(0);
        var rear = history.ElementAtOrDefault(1);
        if (homeHistoryFrontKey == front?.Key && homeHistoryRearKey == rear?.Key) return;

        var oldFrontKey = homeHistoryFrontKey;
        var animateFront = oldFrontKey != null && front != null && oldFrontKey != front.Key &&
            homeHistoryCoverStack.FrontSource != null;
        var retainedFront = homeHistoryCoverStack.FrontSource;
        var generation = ++homeHistoryCoverGeneration;
        homeHistoryFrontKey = front?.Key;
        homeHistoryRearKey = rear?.Key;

        Run(async () =>
        {
            if (!animateFront && oldFrontKey == front?.Key)
            {
                var backSource = rear == null ? null : await GetCollectionArtworkAsync(rear);
                if (!disposed && generation == homeHistoryCoverGeneration) homeHistoryCoverStack.SetRear(backSource);
                return;
            }

            var frontTask = front == null ? Task.FromResult<ImageSource?>(null) : GetCollectionArtworkAsync(front);
            var rearTask = rear == null || animateFront ? Task.FromResult<ImageSource?>(null) : GetCollectionArtworkAsync(rear);
            await Task.WhenAll(frontTask, rearTask);
            if (disposed || generation != homeHistoryCoverGeneration) return;
            IImageSource? frontSource = oldFrontKey == front?.Key ? retainedFront : await frontTask;
            homeHistoryCoverStack.SetCovers(frontSource, await rearTask, animateFront);
        });
    }
}
