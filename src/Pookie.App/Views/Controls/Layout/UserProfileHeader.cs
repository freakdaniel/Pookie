using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Neither image dimensions nor a late banner may change the header's geometry.
internal sealed class UserProfileHeader(FrameworkElement content, Image banner) : Panel
{
    internal PlayerBackdrop Backdrop { get; } = new();
    internal Grid BackgroundLayer { get; } = new Grid().Columns("*").Rows("*").IsHitTestVisible(false);
    internal event Action? BackgroundChanged;
    private Grid bannerLayer = null!;
    private readonly AnimationClock reveal = new(TimeSpan.FromMilliseconds(500), Easing.CubicBezier(.2, 0, 0, 1));
    internal double BannerOpacity => bannerLayer.Opacity;
    internal bool Revealing => reveal.IsRunning;

    internal UserProfileHeader Initialize()
    {
        bannerLayer = new Grid { Opacity = 0 }.Columns("*").Rows("*").IsHitTestVisible(false).Children(
            banner, new Border().Background(Color.FromArgb(160, 18, 18, 22)).IsHitTestVisible(false));
        reveal.TickCallback = value => { bannerLayer.Opacity = value; BackgroundChanged?.Invoke(); };
        Backdrop.PaletteChanged += OnBackgroundChanged;
        BackgroundLayer.Children(Backdrop, bannerLayer, new ArtworkGlassSheen());
        Add(BackgroundLayer); Add(content);
        return this;
    }

    internal void SetPalette(PlayerPalette palette) => Backdrop.SetPalette(palette);
    internal void SetBanner(ImageSource? source)
    {
        if (ReferenceEquals(banner.Source, source)) return;
        reveal.Stop(); bannerLayer.Opacity = 0; banner.Source = source;
        BackgroundChanged?.Invoke();
        if (source != null) reveal.Start();
    }
    internal void Reset() { reveal.Stop(); bannerLayer.Opacity = 0; banner.Source = null; Backdrop.Reset(); }

    private void OnBackgroundChanged() => BackgroundChanged?.Invoke();
    protected override Size MeasureContent(Size availableSize)
    {
        content.Measure(availableSize);
        BackgroundLayer.Measure(content.DesiredSize);
        return content.DesiredSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    { foreach (var child in Children) child.Arrange(Bounds); return finalSize; }
    protected override void OnDispose()
    {
        reveal.Stop(); Backdrop.PaletteChanged -= OnBackgroundChanged;
        base.OnDispose();
    }
}
