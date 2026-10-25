using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class ChatLibraryPreview : Panel
{
    private readonly LibrarySkeleton skeleton = new() { ArtworkOnly = true };
    private readonly Image picture = new Image().StretchMode(Stretch.UniformToFill);
    private readonly TextBlock notice = new TextBlock().FontSize(11).Foreground(TrackButtons.OnSurface).TextWrapping(TextWrapping.Wrap).Center().IsVisible(false);
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    internal bool Loading { get; private set; }
    internal bool Ready => picture.Source != null;
    internal bool Waiting { get; private set; } = true;
    internal bool Manual { get; private set; }
    internal bool Animating => fade.IsRunning;
    internal bool Shimmering => skeleton.Running;
    internal string Notice => notice.Text;
    internal IImageSource? Source => picture.Source;
    internal ChatLibraryPreview()
    {
        ClipToBounds = true; Add(skeleton); Add(picture); Add(notice); picture.Opacity = 0;
        fade.TickCallback = t => { picture.Opacity = t; skeleton.Opacity = 1-t; };
        fade.CompletedCallback = () => { skeleton.IsVisible = false; skeleton.SetActive(false); };
    }
    internal void Start() { Waiting = false; Loading = true; notice.IsVisible = false; skeleton.IsVisible = true; skeleton.Opacity = 1; skeleton.SetActive(true); }
    internal void Defer() { Waiting = false; skeleton.IsVisible = false; skeleton.SetActive(false); notice.Text = "Загрузить"; notice.IsVisible = true; }
    internal void Retry() { Manual = Waiting = true; notice.IsVisible = false; skeleton.IsVisible = true; }
    internal void Fail() { Loading = Waiting = false; skeleton.IsVisible = false; skeleton.SetActive(false); notice.Text = "Не загрузилось\nПовторить"; notice.IsVisible = true; }
    internal void Show(ImageSource source)
    {
        if (ReferenceEquals(picture.Source, source)) return;
        var first = !Ready; picture.Source = source; Waiting = Loading = false; notice.IsVisible = false;
        if (first) { skeleton.SetActive(false); fade.Start(); }
    }
    internal void Stop() { fade.Stop(); skeleton.SetActive(false); }
    protected override Size MeasureContent(Size availableSize)
    { foreach (var child in Children) child.Measure(availableSize); return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 94,94); }
    protected override Size ArrangeOverride(Size finalSize)
    { foreach (var child in Children) child.Arrange(Bounds); return finalSize; }
    protected override void OnDispose() { Stop(); base.OnDispose(); }
}
