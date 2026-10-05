using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private static readonly Color LikedHeart = Color.FromRgb(184, 51, 78);

    private readonly LoadingTrack loadingTrack = new();

    private FrameworkElement PlayerBar()
    {
        playbackLoading.Changed += () => loadingTrack.SetLoading(playbackLoading.Value);
        loadingTrack.Height(12).BindIsVisible(playbackLoading);
        progress.BindIsVisible(playbackLoading, loading => !loading);
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

        playerIsland = new Border()
            .Background(Color.FromRgb(42, 42, 42)).CornerRadius(12).BorderThickness(0)
            .Padding(10, 8).Width(1160).Height(72).CenterHorizontal().Bottom().Margin(28, 0, 28, 24).ClipToBounds()
            .Child(new Grid().Columns("*,380,*").Rows("*").Spacing(12).Children(
            new Grid().Columns("48,*,Auto").Rows("*").Spacing(6).Left().CenterVertical().Column(0).Children(
                new Border().Background(Color.FromRgb(59, 59, 59)).CornerRadius(6).ClipToBounds()
                    .Width(48).Height(48).Child(artwork).Column(0),
                new StackPanel().Vertical().Spacing(4).MaxWidth(180).Margin(4, 0, 0, 0).CenterVertical().Column(1).Children(
                    new TextBlock().BindText(title).FontSize(12).Bold().TextTrimming(TextTrimming.CharacterEllipsis),
                    new TextBlock().BindText(artist).FontSize(11).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis)),
                heartButton.CenterVertical().Column(2)),
            new Grid().Columns("*").Rows("34,18").Spacing(4).CenterVertical().Column(1).Children(
                new StackPanel().Horizontal().Spacing(10).CenterHorizontal().Row(0).Children(
                    PlayerButton(Icons.View("skip-back-solid", 18), () => Run(() => SkipAsync(-1))).CenterVertical(),
                    new Button().Content(new Grid().Columns("*").Rows("*").Children(
                        Icons.View("play-solid", 19, true).CenterHorizontal().CenterVertical().BindIsVisible(isPlaying, value => !value),
                        Icons.View("pause-solid", 19, true).CenterHorizontal().CenterVertical().BindIsVisible(isPlaying)))
                        .Padding(0).Width(34).Height(34).CornerRadius(17).Background(Color.FromRgb(232, 232, 232)).BorderThickness(0)
                        .OnClick(() => Run(ToggleAsync)),
                    PlayerButton(Icons.View("skip-forward-solid", 18), () => Run(() => SkipAsync(1))).CenterVertical()),
            new Grid().Columns("Auto,*,Auto").Rows("*").Spacing(0).Children(
                    new TextBlock().BindText(currentTime).FontSize(11).Foreground(Muted).Margin(0, 0, 0, 2).CenterVertical().Right().Column(0),
                    new Grid().Columns("*").Rows("*").Height(12).CenterVertical().Column(1)
                        .Children(progress, loadingTrack),
                    new TextBlock().BindText(totalTime).FontSize(11).Foreground(Muted).Margin(0, 0, 0, 2).CenterVertical().Left().Column(2)).Row(1)),
            new StackPanel().Horizontal().Spacing(12).CenterVertical().Right().Column(2).Children(
                PlayerButton(Icons.View("queue", 19), () =>
                    { queueOpen.Value = !queueOpen.Value; profileOpen.Value = false; })
                    .Bind(Control.BackgroundProperty, queueOpen, value => value ? Color.FromRgb(66, 66, 66) : Color.Transparent),
                PlayerButton(Icons.View("shuffle", 19), () => shuffle.Value = !shuffle.Value)
                    .Bind(Control.BackgroundProperty, shuffle, value => value ? Color.FromRgb(66, 66, 66) : Color.Transparent),
                new StackPanel().Horizontal().Spacing(4).CenterVertical().Children(
                    PlayerButton(new Grid().Columns("*").Rows("*").Children(
                        Icons.View("speaker-high", 19).CenterHorizontal().CenterVertical().BindIsVisible(muted, value => !value),
                        Icons.View("speaker-slash", 19).CenterHorizontal().CenterVertical().BindIsVisible(muted)), ToggleMute),
                    ThinSlider().Minimum(0).Maximum(100).BindValue(volume).Width(72).CenterVertical()
                        .OnValueChanged(value => { muted.Value = value == 0; RunSync(() => player?.Volume(value)); })))));
        playerIsland.Opacity = 0;
        playerIsland.Height = 0;
        playerIsland.Transitions = [
            Transition.Create(UIElement.OpacityProperty, 250, Easing.CubicBezier(0.2, 0, 0, 1)),
            Transition.Create(FrameworkElement.HeightProperty, 420, Easing.CubicBezier(0.16, 1, 0.3, 1))];
        playerIsland.BindIsVisible(playerVisible);
        playerIsland.Bind(UIElement.OpacityProperty, playerVisible, visible => visible ? 1d : 0d);
        playerIsland.Bind(FrameworkElement.HeightProperty, playerVisible, visible => visible ? 72d : 0d);
        return playerIsland;
    }

    private static Button PlayerButton(FrameworkElement content, Action action) => new Button()
        .StyleName("flat-button").Content(new Grid().Columns("*").Rows("*")
            .Children(content.CenterHorizontal().CenterVertical())).Padding(0).Width(28).Height(28).CenterVertical().CornerRadius(6)
        .OnClick(action);

    private static Slider ThinSlider()
    {
        var slider = new Slider().Height(12).BorderThickness(0).BorderBrush(Color.Transparent)
            .Background(Color.FromRgb(64, 64, 64)).ThumbBrush(Color.Transparent).ThumbBorderBrush(Color.Transparent);
        slider.OnMouseEnter(() => slider.ThumbBrush = Color.FromRgb(208, 208, 208));
        slider.OnMouseLeave(() => slider.ThumbBrush = Color.Transparent);
        return slider;
    }
}
