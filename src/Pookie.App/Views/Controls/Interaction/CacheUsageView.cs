using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed class CacheUsageView : Panel
{
    private static readonly string[] Names = ["Обложки", "Звуковые волны", "Тексты"];
    internal static readonly Color[] Colors = [Color.FromRgb(170, 199, 255), Color.FromRgb(206, 182, 255), Color.FromRgb(255, 192, 160)];
    private readonly Border card;
    private readonly TextBlock total = new TextBlock().Text("—").FontSize(24).SemiBold().CenterHorizontal();
    private readonly TextBlock caption = new TextBlock().Text("считаем объём").FontSize(12).Foreground(TrackButtons.OnSurface).CenterHorizontal();
    private readonly TextBlock[] amounts = new TextBlock[3], shares = new TextBlock[3];
    private readonly CacheUsageLegend[] legends = new CacheUsageLegend[3];
    internal TextBlock Title { get; } = new TextBlock().Text("Кэш на устройстве").FontSize(20).SemiBold();
    internal CacheUsageChart Chart { get; } = new();
    internal CacheUsage? Usage { get; private set; }
    internal string TotalText => total.Text;
    internal IReadOnlyList<TextBlock> Amounts => amounts;
    internal IReadOnlyList<CacheUsageLegend> Legends => legends;

    internal CacheUsageView()
    {
        var ring = new Grid().Columns("*").Rows("*").Width(168).Height(168).Children(Chart,
            new StackPanel().Vertical().Spacing(2).CenterVertical().IsHitTestVisible(false).Children(total, caption));
        var categories = new Grid().Columns("*,*,*").Rows("*").Spacing(32);
        for (var index = 0; index < 3; index++)
        {
            amounts[index] = new TextBlock().Text("—").FontSize(19).SemiBold();
            shares[index] = new TextBlock().Text("—").FontSize(12).Foreground(Color.FromRgb(170, 170, 170));
            var item = index;
            legends[index] = new CacheUsageLegend(Names[index], Colors[index], amounts[index], shares[index],
                hovered => Chart.Hover(hovered ? item : -1));
            categories.Add(legends[index].Column(index));
        }
        card = new Border().Background(Color.FromRgb(34, 34, 34)).BorderThickness(0).CornerRadius(20).Padding(24)
            .Child(new Grid().Columns("168,*").Rows("*").Spacing(28).Children(ring.Column(0),
                new StackPanel().Vertical().Spacing(24).Column(1).Children(Title, categories)));
        Add(card);
        Chart.HoveredChanged = ShowCategory;
        Chart.HoverFrame = () =>
        {
            for (var index = 0; index < legends.Length; index++) legends[index].Highlight = Chart.Highlights[index];
        };
    }

    internal void Update(CacheUsage usage)
    {
        Usage = usage;
        var sizes = new[] { usage.ImagesBytes, usage.WaveformsBytes, usage.LyricsBytes };
        for (var index = 0; index < sizes.Length; index++)
        {
            amounts[index].Text = FormatBytes(sizes[index]);
            shares[index].Text = usage.TotalBytes == 0 ? "0 %" : (sizes[index] / (double)usage.TotalBytes).ToString("0.# %");
        }
        Chart.Update(sizes);
        ShowCategory(-1);
    }

    private void ShowCategory(int index)
    {
        if (Usage == null) return;
        var sizes = new[] { Usage.ImagesBytes, Usage.WaveformsBytes, Usage.LyricsBytes };
        total.Text = FormatBytes(index >= 0 ? sizes[index] : Usage.TotalBytes);
        caption.Text = index >= 0 ? Names[index].ToLowerInvariant() : "всего в кэше";
    }

    internal static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.#} ГиБ" :
        bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.#} МиБ" : bytes >= 1024 ? $"{bytes / 1024d:0.#} КиБ" : $"{bytes} Б";

    protected override Size MeasureContent(Size availableSize) { card.Measure(availableSize); return card.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize)
    { card.Arrange(new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height)); return finalSize; }
}

// The dot starts at the heading's edge; labels and values share the inset text column.
internal sealed class CacheUsageLegend : Panel
{
    private readonly StackPanel content;
    private readonly Color color;
    private double highlight;
    internal TextBlock Label { get; }
    internal double Highlight
    {
        get => highlight;
        set { if (highlight == value) return; highlight = value; InvalidateVisual(); }
    }

    internal CacheUsageLegend(string name, Color color, TextBlock amount, TextBlock share, Action<bool> hovered)
    {
        this.color = color;
        Label = new TextBlock().Text(name).FontSize(13).Foreground(color);
        content = new StackPanel().Vertical().Spacing(7).Children(Label, amount, share);
        Add(content);
        MouseEnter += () => hovered(true);
        MouseLeave += () => hovered(false);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        content.Measure(new Size(Math.Max(0, availableSize.Width - 14), availableSize.Height));
        return new Size(content.DesiredSize.Width + 14, content.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    { content.Arrange(new Rect(Bounds.X + 14, Bounds.Y, Math.Max(0, finalSize.Width - 14), finalSize.Height)); return finalSize; }
    protected override void OnRender(IGraphicsContext context)
    {
        if (highlight > 0)
            context.FillRoundedRectangle(new Rect(Bounds.X - 12, Bounds.Y - 12, Bounds.Width + 24, Bounds.Height + 24), 12, 12,
                Color.FromArgb((byte)Math.Round(255 * .08 * highlight), color.R, color.G, color.B));
        context.FillEllipse(new Rect(Bounds.X, Label.Bounds.Y + (Label.ActualHeight - 7) / 2, 7, 7), color);
    }
}

internal sealed class CacheUsageChart : Control
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(420), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly double[] fractions = new double[3], from = new double[3], target = new double[3];
    private readonly PathGeometry arc = new();
    private readonly AnimationClock hoverMotion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly double[] highlights = new double[3], highlightFrom = new double[3];
    internal Action<int>? HoveredChanged;
    internal Action? HoverFrame;
    internal int HoveredCategory { get; private set; } = -1;
    internal IReadOnlyList<double> Fractions => fractions;
    internal IReadOnlyList<double> Highlights => highlights;
    internal bool Animating => motion.IsRunning || hoverMotion.IsRunning;

    internal CacheUsageChart()
    {
        Width = Height = 168;
        motion.TickCallback = amount =>
        {
            for (var index = 0; index < 3; index++) fractions[index] = from[index] + (target[index] - from[index]) * amount;
            InvalidateVisual();
        };
        motion.CompletedCallback = () => { Array.Copy(target, fractions, 3); InvalidateVisual(); };
        hoverMotion.TickCallback = amount =>
        {
            for (var index = 0; index < 3; index++)
                highlights[index] = highlightFrom[index] + ((index == HoveredCategory ? 1 : 0) - highlightFrom[index]) * amount;
            HoverFrame?.Invoke(); InvalidateVisual();
        };
        hoverMotion.CompletedCallback = () =>
        {
            for (var index = 0; index < 3; index++) highlights[index] = index == HoveredCategory ? 1 : 0;
            HoverFrame?.Invoke(); InvalidateVisual();
        };
    }

    internal void Update(long[] sizes)
    {
        motion.Stop();
        var total = sizes.Sum();
        Array.Copy(fractions, from, 3);
        for (var index = 0; index < 3; index++) target[index] = total > 0 ? sizes[index] / (double)total : 0;
        Hover(-1); motion.Start();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var cx = Bounds.X + Bounds.Width / 2; var cy = Bounds.Y + Bounds.Height / 2;
        const double radius = 70, thickness = 14;
        context.DrawEllipse(new Rect(cx - radius, cy - radius, radius * 2, radius * 2), Color.FromRgb(57, 57, 60), thickness);
        var start = -Math.PI / 2;
        for (var index = 0; index < 3; index++)
        {
            var sweep = fractions[index] * Math.Tau;
            if (sweep > .00001)
            {
                var gap = Math.Min(.045, sweep * .2);
                arc.Reset();
                arc.Arc(cx, cy, radius, radius, start + gap, start + sweep - gap, false);
                context.DrawPath(arc, CacheUsageView.Colors[index], thickness + 5 * highlights[index]);
            }
            start += sweep;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var dx = point.X - Bounds.Width / 2; var dy = point.Y - Bounds.Height / 2;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        var angle = (Math.Atan2(dy, dx) + Math.PI / 2 + Math.Tau) % Math.Tau / Math.Tau;
        var offset = 0d; var hovered = -1;
        if (radius is >= 59 and <= 81)
            for (var index = 0; index < 3; index++)
            {
                if (fractions[index] > 0 && angle >= offset && angle < offset + fractions[index]) { hovered = index; break; }
                offset += fractions[index];
            }
        Hover(hovered);
    }
    protected override void OnMouseLeave() { base.OnMouseLeave(); Hover(-1); }
    internal void Hover(int index)
    {
        if (HoveredCategory == index) return;
        hoverMotion.Stop(); Array.Copy(highlights, highlightFrom, 3);
        HoveredCategory = index; HoveredChanged?.Invoke(index); hoverMotion.Start(); InvalidateVisual();
    }
    protected override void OnDispose() { motion.Stop(); hoverMotion.Stop(); base.OnDispose(); }
}
