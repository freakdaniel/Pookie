using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private StackPanel homeMixSection = null!;
    private MixCarousel homeMixCarousel = null!;
    private TextBlock homeMixStatus = null!;
    private TransitionContentControl homeMixCaption = null!;

    private FrameworkElement HomeMixCarouselView()
    {
        homeMixCarousel = new MixCarousel(this);
        homeMixCaption = new TransitionContentControl
        {
            Transition = ContentTransition.CreateSlide(SlideDirection.Up, durationMs: 220, easing: Easing.CubicBezier(.2, 0, 0, 1))
        };
        var captionViewport = new Border().Height(42).BorderThickness(0).ClipToBounds().Child(homeMixCaption);
        homeMixStatus = new TextBlock().FontSize(13).Foreground(Muted).CenterHorizontal().IsVisible(false);
        return homeMixSection = new StackPanel().Vertical().Spacing(5).Children(
            new TextBlock().Text("Миксы для тебя").FontSize(26).Bold().CenterHorizontal(),
            homeMixCarousel, captionViewport, homeMixStatus).IsVisible(false);
    }

    private void RenderHomeMixCarousel(LibraryItem[] mixes)
    {
        var hasMixes = mixes.Length > 0;
        var status = hasMixes ? "" : homePending.Contains("mixes") ? "Загружаем миксы…" :
            homeErrors.Contains("mixes") ? "Не удалось загрузить миксы." : "";
        homeMixStatus.Text = status;
        homeMixStatus.IsVisible = status.Length > 0;
        homeMixSection.IsVisible = hasMixes || homeMixStatus.IsVisible;
        homeMixCarousel.IsVisible = hasMixes;
        if (homeMixCaption.Parent is Border captionViewport) captionViewport.IsVisible = hasMixes;
        if (!homeMixCarousel.SetItems(mixes)) return;
        RefreshHomeMixPlayback();
        RefreshHomeMixMotion();
    }

    private void SetHomeMixCaption(LibraryItem? mix)
    {
        homeMixCaption.Content = mix == null ? null : new StackPanel().Vertical().Spacing(3).CenterVertical().Children(
            new TextBlock().Text(mix.Title).FontSize(16).SemiBold().CenterHorizontal().TextTrimming(TextTrimming.CharacterEllipsis),
            new TextBlock().Text(mix.Playlist?.ShortDescription ?? "").FontSize(13).Foreground(Muted)
                .CenterHorizontal().TextTrimming(TextTrimming.CharacterEllipsis).IsVisible(!string.IsNullOrWhiteSpace(mix.Playlist?.ShortDescription)));
    }

    private void RefreshHomeMixPlayback() =>
        homeMixCarousel?.UpdatePlayback(current != null ? playbackQueue.Context?.Key : null, isPlaying.Value);

    private void RefreshHomeMixMotion()
    {
        if (homeMixCarousel == null) return;
        if (Window?.ClientSize.Width is > 0 and var width) homeMixCarousel.SetViewportWidth(width);
        homeMixCarousel.SetMotionActive(!disposed && page.Value == Page.Home && Window is { IsActive: true, WindowState: not WindowState.Minimized } &&
            workspace.IsVisible && !expandedOpen && homeMixSection.IsVisible && homeMixCarousel.ActualWidth > 0 &&
            homeMixCarousel.Bounds.Bottom > startupHeader.Bounds.Bottom && homeMixCarousel.Bounds.Y < homeScroll.Bounds.Bottom);
    }
}
