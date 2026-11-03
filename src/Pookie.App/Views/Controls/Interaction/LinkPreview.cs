using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

internal sealed record LinkPreviewData(string Url, string Label, ImageSource? Cover, bool Loading = false, bool Failed = false)
{
    internal static LinkPreviewData Pending(string url) => new(url, "Ищу...", null, Loading: true);
    internal static LinkPreviewData Missing(string url) => new(url, url, null, Failed: true);
}

// One presentation for native search fields and generated inline elements. The document keeps the URL.
internal sealed class LinkPreviewVisual : IDisposable
{
    private static readonly Color Accent = Color.FromRgb(128, 196, 249);
    private readonly TextElement owner;
    private readonly Action layoutChanged;
    private readonly AnimationClock fade = new(TimeSpan.FromMilliseconds(200), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly AnimationClock spin = new(TimeSpan.FromMilliseconds(1600), Easing.Linear) { RepeatCount = -1 };
    private readonly PathGeometry arc = new();
    private Layer current;
    private Layer? previous;
    private Window? window;
    private double amount = 1, phase, maximum = 400;
    private bool disposed;
    internal Action? FallbackCompleted;
    internal LinkPreviewData Data => current.Data;
    internal bool IsFallback => Data.Failed && !fade.IsRunning;
    internal bool Transitioning => fade.IsRunning;

    internal LinkPreviewVisual(TextElement owner, LinkPreviewData data, Action layoutChanged)
    {
        this.owner = owner; this.layoutChanged = layoutChanged; current = new(data);
        fade.TickCallback = value => { amount = value; owner.InvalidateVisual(); };
        fade.CompletedCallback = () =>
        {
            amount = 1; previous?.Dispose(); previous = null; Refresh();
            layoutChanged(); if (Data.Failed) FallbackCompleted?.Invoke();
        };
        spin.TickCallback = value => { phase = value; owner.InvalidateVisual(); };
        Refresh();
    }

    internal void Update(LinkPreviewData data)
    {
        if (Data == data) return;
        fade.Stop(); previous?.Dispose(); previous = current; current = new(data); amount = 0;
        layoutChanged();
        if (owner.FindVisualRoot() is Window) fade.Start();
        else { amount = 1; previous.Dispose(); previous = null; }
        Refresh();
    }

    internal double Measure(double available)
    {
        maximum = Math.Clamp(available, 32, 460);
        return current.Measure(owner, maximum);
    }

    internal void Draw(IGraphicsContext graphics, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Refresh();
        if (previous != null && amount < 1) Paint(previous, 1 - amount);
        Paint(current, amount);
        void Paint(Layer layer, double alpha)
        {
            if (alpha <= 0) return;
            var layerWidth = layer.Measure(owner, Math.Min(maximum, bounds.Width));
            graphics.Save();
            try
            {
                graphics.GlobalAlpha *= (float)alpha;
                graphics.SetClip(bounds);
                var data = layer.Data;
                var inset = data.Failed ? 0 : 28;
                if (!data.Failed)
                {
                    graphics.FillRoundedRectangle(new Rect(bounds.X, bounds.Y, layerWidth, bounds.Height), 9, 9, Color.FromArgb(20, Accent.R, Accent.G, Accent.B));
                    var picture = new Rect(bounds.X + 5, bounds.Y + (bounds.Height - 16) / 2, 16, 16);
                    if (data.Loading) LoadingSpinner.Draw(graphics, picture, Accent, phase, arc);
                    else if (data.Cover != null)
                    {
                        layer.Image ??= data.Cover.CreateImage(Application.Current.GraphicsFactory, 40, 40);
                        graphics.Save();
                        try { graphics.SetClipRoundedRect(picture, 8, 8); graphics.DrawImage(layer.Image, picture); }
                        finally { graphics.Restore(); }
                    }
                    else
                    {
                        graphics.DrawRoundedRectangle(new(picture.X + 1, picture.Y + 5, 9, 6), 3, 3, Accent, 1.2);
                        graphics.DrawRoundedRectangle(new(picture.X + 6, picture.Y + 5, 9, 6), 3, 3, Accent, 1.2);
                    }
                }
                graphics.Text.Draw(layer.Text!, new(bounds.X + inset, bounds.Y + (bounds.Height - layer.Text!.MeasuredSize.Height) / 2),
                    new TextDrawOptions(data.Failed ? owner.Foreground : Accent));
            }
            finally { graphics.Restore(); }
        }
    }

    private void Refresh()
    {
        var next = disposed || (!Data.Loading && !fade.IsRunning && previous?.Data.Loading != true) ? null : owner.FindVisualRoot() as Window;
        if (window != next)
        {
            if (window != null) window.FrameRendered -= Refresh;
            window = next;
            if (window != null) window.FrameRendered += Refresh;
        }
        var visible = window != null && window.WindowState != WindowState.Minimized;
        for (Element? element = owner; visible && element != null; element = element.Parent)
            if (element is UIElement { IsVisible: false } or UIElement { Opacity: <= 0 }) visible = false;
        var loading = Data.Loading || previous?.Data.Loading == true;
        if (visible && loading && !spin.IsRunning) spin.Start();
        else if (!visible || !loading) spin.Stop();
        if (next == null) fade.Stop();
    }

    public void Dispose()
    { disposed = true; fade.Stop(); spin.Stop(); Refresh(); previous?.Dispose(); current.Dispose(); }

    private sealed class Layer(LinkPreviewData data) : IDisposable
    {
        internal readonly LinkPreviewData Data = data;
        internal ITextLayout? Text;
        internal IImage? Image;
        private double width = -1, dpi;
        internal double Measure(TextElement owner, double maximum)
        {
            var scale = owner.FindVisualRoot() is Window root ? root.DpiScale : 1;
            if (Text == null || width != maximum || dpi != scale)
            {
                width = maximum; dpi = scale;
                Text = Application.Current.GraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
                {
                    Text = Data.Label.AsMemory(), Dpi = (uint)Math.Round(96 * scale),
                    DefaultStyle = new(owner.FontFamily, Data.Failed ? owner.FontSize : 13, Data.Failed ? FontWeight.Normal : FontWeight.SemiBold),
                    Paragraph = new() { MaxWidth = Math.Max(1, maximum - (Data.Failed ? 0 : 33)), Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.CharacterEllipsis }
                });
            }
            return Math.Min(maximum, Text.MeasuredSize.Width + (Data.Failed ? 0 : 33));
        }
        public void Dispose() { Image?.Dispose(); Image = null; }
    }
}

internal sealed class LinkPreviewControl : Control
{
    private LinkPreviewVisual? visual;
    internal Action? FallbackCompleted;
    internal string? Label => visual?.Data.Label;
    internal bool Transitioning => visual?.Transitioning == true;
    internal bool Loading => visual?.Data.Loading == true;
    internal void Update(LinkPreviewData data)
    {
        if (visual == null)
        {
            visual = new(this, data, () => { InvalidateMeasure(); InvalidateVisual(); });
            visual.FallbackCompleted = () => FallbackCompleted?.Invoke();
        }
        else visual.Update(data);
    }
    internal void Clear() { visual?.Dispose(); visual = null; InvalidateMeasure(); InvalidateVisual(); }
    protected override Size MeasureContent(Size availableSize) => new(visual?.Measure(double.IsFinite(availableSize.Width) ? availableSize.Width : 400) ?? 0, 20);
    protected override void OnRender(IGraphicsContext context) => visual?.Draw(context, Bounds);
    protected override void OnDispose() { Clear(); base.OnDispose(); }
}
