using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed class LibraryLoadingView : IDisposable
{
    public Grid Root { get; }
    public LibrarySkeleton Skeleton { get; } = new();
    public bool Loading { get; private set; }
    private readonly FrameworkElement content;
    private readonly AnimationClock fade;
    private double previewHeight;

    public LibraryLoadingView(FrameworkElement content, bool preview = false)
    {
        this.content = content;
        Skeleton.Preview = preview;
        Skeleton.IsVisible = false;
        Root = new Grid().Columns("*").Rows(preview ? "Auto" : "*").Children(content, Skeleton);
        fade = new AnimationClock(TimeSpan.FromMilliseconds(300), Easing.CubicBezier(.2, 0, 0, 1))
        {
            TickCallback = value =>
            {
                content.Opacity = value; Skeleton.Opacity = 1 - value;
                if (preview && Root.ActualWidth > 0)
                {
                    // Empty/short responses shrink their reserved slot gradually, preserving neighboring sections.
                    content.Measure(new Size(Root.ActualWidth, double.PositiveInfinity));
                    Skeleton.Height = previewHeight + (content.DesiredSize.Height - previewHeight) * value;
                }
            },
            CompletedCallback = () => { Skeleton.IsVisible = false; Skeleton.Height = double.NaN; Skeleton.SetActive(false); }
        };
    }

    public void SetLoading(bool loading)
    {
        if (Loading == loading) return;
        Loading = loading;
        fade.Stop();
        content.IsHitTestVisible = !loading;
        if (loading)
        {
            Skeleton.Height = double.NaN;
            content.Opacity = 0; Skeleton.Opacity = 1;
            Skeleton.IsVisible = true; Skeleton.SetActive(true);
        }
        else { previewHeight = Skeleton.ActualHeight; fade.Start(); }
    }

    public void Dispose() { fade.Stop(); Skeleton.SetActive(false); }
}

internal sealed class LibrarySkeleton : Control
{
    public bool Preview { get; set; }
    public bool ListView { get; set; }
    public bool CompactList { get; set; }
    public bool ArtworkOnly { get; set; }
    private LoadingRowStyle? loadingRowStyle;
    private double artworkSize = 176;
    private int columns = 6;
    private double phase;
    private readonly AnimationClock shimmer;

    public LibrarySkeleton()
    {
        IsHitTestVisible = false;
        shimmer = new AnimationClock(TimeSpan.FromMilliseconds(1600), Easing.Linear)
        {
            RepeatCount = -1,
            TickCallback = value => { phase = value; InvalidateVisual(); }
        };
    }

    public void SetGeometry(double size, int count, bool listView)
    {
        if (artworkSize == size && columns == count && ListView == listView) return;
        artworkSize = size; columns = count; ListView = listView;
        InvalidateMeasure(); InvalidateVisual();
    }

    public void ConfigureLoadingRow(double size, int count, LoadingRowStyle style)
    {
        var rowColumns = style == LoadingRowStyle.Card ? 1 : count;
        if (loadingRowStyle == style && artworkSize == size && columns == rowColumns && !double.IsNaN(Height)) return;
        loadingRowStyle = style;
        SetGeometry(size, rowColumns, style == LoadingRowStyle.Waveform);
        Height = style switch { LoadingRowStyle.Compact => 64, LoadingRowStyle.Waveform => TrackRowLayout.Stride, _ => size + 90 };
    }

    public void SetActive(bool active) { if (active) shimmer.Start(); else shimmer.Stop(); }
    protected override Size MeasureContent(Size availableSize) => new(0,
        Preview ? Math.Ceiling(6d / columns) * (artworkSize + 90) : 0);

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var band = Math.Max(180, bounds.Width * .35);
        var x = bounds.X - band + phase * (bounds.Width + 2 * band);
        var brush = new LinearGradientBrush(new Point(x, bounds.Y), new Point(x + band, bounds.Y + 80), [
            new(0, Color.FromRgb(37, 37, 37)), new(.5, Color.FromRgb(53, 53, 53)), new(1, Color.FromRgb(37, 37, 37))]);
        if (ArtworkOnly) { context.FillRectangle(bounds, brush); return; }
        void Block(double left, double top, double width, double height, double radius = 4)
        {
            var rect = new Rect(bounds.X + left, bounds.Y + top, Math.Max(0, width), Math.Min(height, bounds.Height - top));
            if (rect.Width > 0 && rect.Height > 0) context.FillRoundedRectangle(rect, radius, radius, brush);
        }
        if (loadingRowStyle == LoadingRowStyle.Compact || CompactList && ListView)
        {
            for (var y = 0d; y < bounds.Height; y += 64)
            {
                Block(8, y + 10, 44, 44, 5); Block(64, y + 17, Math.Min(240, bounds.Width * .45), 12);
                Block(64, y + 38, Math.Min(130, bounds.Width * .3), 10);
                Block(bounds.Width - 50, y + 26, 38, 10);
            }
            return;
        }
        var rowHeight = ListView ? TrackRowLayout.Stride : artworkSize + 90;
        var rows = Preview ? (int)Math.Ceiling(6d / columns) : (int)Math.Ceiling(bounds.Height / rowHeight);
        for (var row = 0; row < rows; row++)
        {
            var y = row * rowHeight;
            if (ListView)
            {
                Block(0, y + 6, 160, 160, 6); Block(180, y, 40, 40, 20);
                Block(230, y + 3, 130, 10); Block(230, y + 24, Math.Min(280, bounds.Width - 248), 14);
                Block(180, y + 56, bounds.Width - 198, 52);
                Block(180, y + 132, 88, 40, 20); Block(276, y + 132, 40, 40, 20);
            }
            else for (var column = 0; column < columns; column++)
            {
                if (Preview && row * columns + column >= 6) break;
                var left = column * (artworkSize + 24);
                Block(left, y, artworkSize, artworkSize, 6);
                Block(left, y + artworkSize + 11, artworkSize * .76, 12);
                Block(left, y + artworkSize + 34, artworkSize * .5, 10);
            }
        }
    }

    protected override void OnDispose() { shimmer.Stop(); base.OnDispose(); }
}
