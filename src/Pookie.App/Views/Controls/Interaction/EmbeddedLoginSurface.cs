using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Browser;

namespace Pookie.App;

// The same opaque rectangle backs loading, browser snapshots and the live native view.
internal sealed class EmbeddedLoginSurface : Panel
{
    internal static readonly Color SurfaceColor = Color.FromRgb(17, 17, 19);
    private readonly EmbeddedLoginView browser;
    private readonly Image logo = Icons.SoundCloudMark(104).Height(100);
    private readonly LoadingSpinner spinner = new LoadingSpinner().Width(28).Height(28).Foreground(Color.FromRgb(239, 239, 239));
    private readonly AnimationClock expand = new(TimeSpan.FromMilliseconds(320), Easing.CubicBezier(.4, 0, .2, 1));
    private readonly AnimationClock reveal = new(TimeSpan.FromMilliseconds(420), Easing.CubicBezier(.4, 0, .2, 1));
    private readonly TaskCompletionSource revealed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool opened, closing, pageReady;
    internal double IntroProgress { get; private set; }
    internal double PageReveal { get; private set; }
    internal bool Loading => spinner.IsActive;
    internal bool NativeVisible => browser.NativePresented;

    internal EmbeddedLoginSurface(EmbeddedLoginView browser)
    {
        this.browser = browser; browser.SnapshotOpacity = 0;
        spinner.Opacity = 0;
        Add(browser); Add(logo); Add(spinner);
        ClipToBounds = true;
        expand.TickCallback = value =>
        {
            IntroProgress = value; spinner.Opacity = value; InvalidateArrange();
        };
        expand.CompletedCallback = () =>
        {
            IntroProgress = 1; spinner.Opacity = 1; InvalidateArrange();
            if (pageReady && !closing) reveal.Start();
        };
        reveal.TickCallback = value =>
        {
            PageReveal = value; browser.SnapshotOpacity = value; logo.Opacity = spinner.Opacity = 1 - value; InvalidateVisual();
        };
        reveal.CompletedCallback = () =>
        {
            PageReveal = browser.SnapshotOpacity = 1; logo.Opacity = spinner.Opacity = 0; spinner.IsActive = false;
            _ = FinishRevealAsync();
        };
    }

    internal Task RevealAsync(CancellationToken token)
    {
        pageReady = true;
        if (opened && IntroProgress == 1 && !closing) reveal.Start();
        return revealed.Task.WaitAsync(token);
    }

    internal void Opened()
    {
        if (opened || closing) return;
        opened = true;
        spinner.IsActive = true;
        expand.Start();
    }

    private async Task FinishRevealAsync()
    {
        try
        {
            if (opened && !closing) await browser.ActivateAsync();
            revealed.TrySetResult();
        }
        catch (OperationCanceledException) when (closing) { revealed.TrySetCanceled(); }
        catch (Exception error) { revealed.TrySetException(error); }
    }

    internal async Task FreezeAsync()
    {
        closing = true; expand.Stop(); reveal.Stop();
        await browser.FreezeAsync();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        browser.Measure(availableSize); logo.Measure(new(104, 100)); spinner.Measure(new(28, 28)); return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        browser.Arrange(new(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height));
        var center = Bounds.Center;
        logo.Arrange(new(center.X - 52, center.Y - 50 - 24 * IntroProgress, 104, 100));
        spinner.Arrange(new(center.X - 14, center.Y + 16 + 12 * (1 - IntroProgress), 28, 28));
        return finalSize;
    }

    protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, SurfaceColor);

    protected override void OnDispose()
    {
        closing = true; expand.Stop(); reveal.Stop(); revealed.TrySetCanceled(); spinner.IsActive = false;
        base.OnDispose();
    }
}
