using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const double PlayerBarHeight = 72;
    private const int PlayerControlsWidth = 380;
    private StackPanel playerTrackInfo = null!;
    private Grid playerTimeline = null!;
    private static readonly Color LikedHeart = Color.FromRgb(184, 51, 78);
    private static readonly Color PlayerSecondaryText = Color.FromArgb(153, 255, 255, 255);

    private readonly List<HoverReveal> hoverReveals = [];
    private TextBlock positionLabel = null!, durationLabel = null!;
    private Slider volumeSlider = null!;

    private readonly LoadingTrack loadingTrack = new();
    private readonly BufferedTrack bufferedTrack = new();
    private readonly PlayerBackdrop playerBackdrop = new();
    private Border playerArtworkOverlay = null!;
    private Button playerArtworkButton = null!;

    private FrameworkElement PlayerBar()
    {
        playbackLoading.Changed += RefreshPlayerTimeline;
        loadingTrack.Height(12);
        bufferedTrack.Height(12);
        progressAnimation.TickCallback = AdvancePlaybackProgress;
        progress.Background = Color.Transparent;
        progress.Padding = new Thickness(0);
        RefreshPlayerTimeline();
        var idleHeart = Icons.View("heart", 19, Color.FromRgb(190, 190, 190));
        var likedHeart = Icons.View("heart-filled", 19, LikedHeart);
        var hoverHeart = Icons.View("heart-filled", 19, Color.FromRgb(255, 255, 255));
        likedHeart.Opacity = 0;
        hoverHeart.Opacity = 0;
        foreach (var heart in new[] { idleHeart, likedHeart, hoverHeart })
            heart.Transitions = [Transition.Create(UIElement.OpacityProperty, 190, Easing.CubicBezier(0.2, 0, 0, 1))];
        bool heartHovered = false;
        void RefreshHeart()
        {
            idleHeart.Opacity = !heartHovered && !isLiked.Value ? 1 : 0;
            likedHeart.Opacity = !heartHovered && isLiked.Value ? 1 : 0;
            hoverHeart.Opacity = heartHovered ? 1 : 0;
        }
        isLiked.Changed += RefreshHeart;
        var heartButton = new Button().Background(Color.Transparent).BorderBrush(Color.Transparent).BorderThickness(0)
            .Padding(0).Width(30).Height(30).CornerRadius(15).Content(new Grid().Columns("*").Rows("*").Children(
                idleHeart.CenterHorizontal().CenterVertical(),
                likedHeart.CenterHorizontal().CenterVertical(),
                hoverHeart.CenterHorizontal().CenterVertical()))
            .BindIsEnabled(likeAvailable)
            .OnMouseEnter(() => { heartHovered = true; RefreshHeart(); })
            .OnMouseLeave(() => { heartHovered = false; RefreshHeart(); })
            .OnClick(() => Run(ToggleLikeAsync));
        RefreshHeart();

        playerArtworkOverlay = new Border().Background(Color.FromArgb(140, 0, 0, 0))
            .Child(Icons.View("arrows-out", 23, Color.White).Center());
        playerArtworkOverlay.Opacity = 0;
        playerArtworkOverlay.Transitions = [Transition.Create(UIElement.OpacityProperty, 180, Easing.CubicBezier(.2, 0, 0, 1))];
        playerArtworkButton = new Button().Background(Color.FromRgb(59, 59, 59)).BorderThickness(0).Padding(0)
            .CornerRadius(6).Width(48).Height(48)
            .Content(new Border().CornerRadius(6).ClipToBounds()
                .Child(new Grid().Columns("*").Rows("*").Children(artwork, playerArtworkOverlay)))
            .OnMouseEnter(() => playerArtworkOverlay.Opacity = 1)
            .OnMouseLeave(() => playerArtworkOverlay.Opacity = 0)
            .OnGotFocus(() => playerArtworkOverlay.Opacity = 1)
            .OnLostFocus(() => playerArtworkOverlay.Opacity = 0)
            .OnClick(() => SetExpandedPlayer(true));

        playerTrackInfo = new StackPanel().Vertical().Spacing(4).MaxWidth(180).Margin(4, 0, 0, 0).CenterVertical().Children(
            new TextBlock().BindText(title).FontSize(13).Bold().TextTrimming(TextTrimming.CharacterEllipsis),
            new TextBlock().BindText(artist).FontSize(11).Foreground(PlayerSecondaryText).TextTrimming(TextTrimming.CharacterEllipsis));
        positionLabel = new TextBlock().BindText(currentTime).FontSize(11).Foreground(PlayerSecondaryText)
            .Margin(0, 0, 4, 2).CenterVertical().Right().Column(0);
        durationLabel = new TextBlock().BindText(totalTime).FontSize(11).Foreground(PlayerSecondaryText)
            .Margin(4, 0, 0, 2).CenterVertical().Left().Column(2);
        foreach (var label in new[] { positionLabel, durationLabel })
        {
            label.Opacity = 0;
            label.Transitions = [Transition.Create(UIElement.OpacityProperty, 200, Easing.CubicBezier(0.2, 0, 0, 1))];
        }
        // Fade the labels without removing their fixed columns from the layout.
        playerTimeline = new Grid().Columns("52,*,52").Rows("*").Spacing(0).Children(
            positionLabel,
            new Grid().Columns("*").Rows("*").Height(12).CenterVertical().Column(1).Children(bufferedTrack, progress, loadingTrack),
            durationLabel);
        hoverReveals.Add(new HoverReveal(playerTimeline, visible =>
            { positionLabel.Opacity = durationLabel.Opacity = visible ? 1 : 0; }, () => progress.IsMouseCaptured));
        volumeSlider = ThinSlider().Minimum(0).Maximum(100).BindValue(volume).Width(72).CenterVertical()
            .OnValueChanged(value => { muted.Value = value == 0; RunSync(() => player?.Volume(value)); });
        playerContentFrame = new Border().Width(DefaultWindowWidth).MaxWidth(1440).Height(PlayerBarHeight)
            .Padding(36, 8).CenterHorizontal().Bottom().Child(new Grid().Columns($"*,{PlayerControlsWidth},*").Rows("*").Spacing(12).Children(
            new Grid().Columns("48,Auto,Auto").Rows("*").Spacing(6).Left().CenterVertical().Column(0).Children(
                playerArtworkButton.Column(0),
                playerTrackInfo.Column(1),
                heartButton.CenterVertical().Column(2)),
            new Grid().Columns("*").Rows("34,18").Spacing(4).CenterVertical().Column(1).Children(
                new StackPanel().Horizontal().Spacing(10).CenterHorizontal().Row(0).Children(
                    PlayerButton(Icons.View("skip-back-solid", 18), () => Run(() => SkipAsync(-1))).CenterVertical(),
                    new Button().Content(new Grid().Columns("*").Rows("*").Children(
                        Icons.PlaybackDisc(false, 34, 19, Color.FromRgb(232, 232, 232)).CenterHorizontal().CenterVertical().BindIsVisible(isPlaying, value => !value),
                        Icons.PlaybackDisc(true, 34, 19, Color.FromRgb(232, 232, 232)).CenterHorizontal().CenterVertical().BindIsVisible(isPlaying)))
                        .Padding(0).Width(34).Height(34).CornerRadius(17).Background(Color.Transparent).BorderBrush(Color.Transparent).BorderThickness(0)
                        .OnClick(() => Run(ToggleAsync)),
                    PlayerButton(Icons.View("skip-forward-solid", 18), () => Run(() => SkipAsync(1))).CenterVertical()),
                playerTimeline.Row(1)),
            new StackPanel().Horizontal().Spacing(12).CenterVertical().Right().Column(2).Children(
                PlayerButton(Icons.View("queue", 19), () =>
                    { queueOpen.Value = !queueOpen.Value; profileOpen.Value = false; }, queueOpen),
                PlayerButton(Icons.View("shuffle", 19), () => shuffle.Value = !shuffle.Value, shuffle),
                new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                    PlayerButton(new Grid().Columns("*").Rows("*").Children(
                        Icons.View("speaker-high", 19).CenterHorizontal().CenterVertical().BindIsVisible(muted, value => !value),
                        Icons.View("speaker-slash", 19).CenterHorizontal().CenterVertical().BindIsVisible(muted)), ToggleMute),
                    volumeSlider))));
        // The backdrop is shared with the rounded content corners above the player.
        playerBackdrop.Height(0).Bottom().BindIsVisible(playerVisible);
        playerBackdrop.Opacity = 0;
        playerBackdrop.Transitions = [Transition.Create(UIElement.OpacityProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1))];
        playerBackdrop.Bind(UIElement.OpacityProperty, playerVisible, visible => visible ? 1d : 0d);
        playerChrome = new Border().Background(Color.Transparent).BorderThickness(0).ClipToBounds().Child(playerContentFrame);
        playerChrome.Opacity = 0;
        playerChrome.Height = 0;
        playerChrome.Transitions = [
            Transition.Create(UIElement.OpacityProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1)),
            Transition.Create(FrameworkElement.HeightProperty, 420, Easing.CubicBezier(0.16, 1, 0.3, 1))];
        playerChrome.BindIsVisible(playerVisible);
        playerChrome.Bind(UIElement.OpacityProperty, playerVisible, visible => visible ? 1d : 0d);
        playerChrome.Bind(FrameworkElement.HeightProperty, playerVisible, visible => visible ? PlayerBarHeight : 0d);
        return playerChrome;
    }

    private static Button PlayerButton(FrameworkElement content, Action action, ObservableValue<bool>? active = null)
    {
        bool hovered = false;
        void Refresh() => content.Opacity = hovered || active?.Value == true ? 1 : .72;
        Refresh();
        if (active != null) active.Changed += Refresh;
        content.Transitions = [Transition.Create(UIElement.OpacityProperty, 190, Easing.CubicBezier(0.2, 0, 0, 1))];
        return new Button().Background(Color.Transparent).BorderThickness(0).Content(new Grid().Columns("*").Rows("*")
            .Children(content.CenterHorizontal().CenterVertical())).Padding(0).Width(28).Height(28).CenterVertical().CornerRadius(6)
            .OnMouseEnter(() => { hovered = true; Refresh(); }).OnMouseLeave(() => { hovered = false; Refresh(); }).OnClick(action);
    }

    private Slider ThinSlider()
    {
        var slider = new Slider().Height(12).BorderThickness(0).BorderBrush(Color.Transparent)
            .Background(LoadingTrack.RailColor).ThumbBrush(Color.FromArgb(0, 208, 208, 208)).ThumbBorderBrush(Color.Transparent);
        slider.Transitions = [Transition.Create(Slider.ThumbBrushProperty, 180, Easing.CubicBezier(.2, 0, 0, 1))];
        hoverReveals.Add(new HoverReveal(slider, visible =>
            slider.ThumbBrush = Color.FromArgb((byte)(visible ? 255 : 0), 208, 208, 208)));
        return slider;
    }
}
