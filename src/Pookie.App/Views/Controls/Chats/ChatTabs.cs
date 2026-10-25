using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// One horizontal pill, with a sliding selection and no scrollbar chrome.
internal sealed class ChatTabs : Panel
{
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.4, 0, .2, 1));
    private readonly List<Button> tabs = [];
    private readonly List<TextBlock> labels = [];
    private readonly List<Action> actions = [];
    internal bool Animating => motion.IsRunning;
    internal Rect SelectionBounds => new(Bounds.X + pillX - offset, Bounds.Y + 4, pillWidth, 40);
    private int selected;
    private double pillX, pillWidth, offset, total, fromX, fromWidth, fromOffset, targetX, targetWidth, targetOffset;
    private bool initialized;
    private double arrangedWidth;
    internal ChatTabs()
    {
        ClipToBounds = true;
        motion.TickCallback = t => { pillX = fromX + (targetX - fromX) * t; pillWidth = fromWidth + (targetWidth - fromWidth) * t; offset = fromOffset + (targetOffset - fromOffset) * t; InvalidateArrange(); InvalidateVisual(); };
        KeyDown += e =>
        {
            if (e.Key is not (Key.Left or Key.Right) || tabs.Count == 0) return;
            e.Handled = true; var step = e.Key == Key.Left ? -1 : 1; var index = selected + step;
            while (index >= 0 && index < tabs.Count && !tabs[index].IsVisible) index += step;
            if (index < 0 || index >= tabs.Count) return;
            Select(index); tabs[index].Focus(); actions[index]();
        };
        MouseWheel += e => { if (total > ActualWidth) { ScrollBy(-(e.Delta.X + e.Delta.Y)*48); e.Handled = true; } };
    }
    internal void ScrollBy(double amount)
    { motion.Stop(); if (initialized) { pillX = targetX; pillWidth = targetWidth; } offset = Math.Clamp(offset+amount,0,Math.Max(0,total-ActualWidth)); InvalidateArrange(); InvalidateVisual(); }
    internal void AddTab(string title, Action activate)
    {
        var index = tabs.Count;
        var label = new TextBlock().Text(title).FontSize(12).SemiBold().Foreground(index == selected ? Color.FromRgb(128,196,249) : TrackButtons.OnSurface);
        labels.Add(label); actions.Add(activate);
        var button = TrackButtons.Action(label, () => { if (index == selected) return; Select(index); activate(); }, TrackButtons.Text, 40).Padding(14, 0).MinWidth(58);
        ((MaterialTrackButton)button).SetPassive(index == selected);
        tabs.Add(button); Add(button);
    }
    internal void Select(int index)
    {
        if (index < 0 || index >= tabs.Count || !tabs[index].IsVisible) return;
        selected = index;
        for (var i = 0; i < labels.Count; i++)
        {
            TrackButtons.SetTextColor(labels[i], i == selected ? Color.FromRgb(128,196,249) : TrackButtons.OnSurface);
            ((MaterialTrackButton)tabs[i]).SetPassive(i == selected);
        }
        Retarget(animate: true);
    }
    internal void SetTabVisible(int index, bool visible)
    { if (index < 0 || index >= tabs.Count || tabs[index].IsVisible == visible) return; tabs[index].IsVisible = visible; initialized = false; InvalidateMeasure(); }
    private void Retarget(bool animate)
    {
        if (tabs.Count == 0 || ActualWidth <= 0) return;
        targetX = 4 + tabs.Take(selected).Where(t => t.IsVisible).Sum(t => t.DesiredSize.Width);
        targetWidth = tabs[selected].DesiredSize.Width;
        targetOffset = Math.Clamp(offset, Math.Max(0, targetX + targetWidth + 4 - ActualWidth), Math.Max(0, Math.Min(targetX - 4, total - ActualWidth)));
        motion.Stop();
        if (!initialized || !animate || FindVisualRoot() is not Window)
        { pillX = targetX; pillWidth = targetWidth; offset = targetOffset; initialized = true; InvalidateArrange(); InvalidateVisual(); return; }
        fromX = pillX; fromWidth = pillWidth; fromOffset = offset; motion.Start();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        total = 8;
        foreach (var tab in tabs.Where(t => t.IsVisible)) { tab.Measure(new Size(double.PositiveInfinity, 40)); total += tab.DesiredSize.Width; }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : total, 48);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!initialized || Math.Abs(arrangedWidth - finalSize.Width) > .1 || Math.Abs(targetWidth - (tabs.ElementAtOrDefault(selected)?.DesiredSize.Width ?? 0)) > .1) Retarget(false);
        arrangedWidth = finalSize.Width;
        var x = Bounds.X + 4 - offset;
        foreach (var tab in tabs.Where(t => t.IsVisible)) { tab.Arrange(new Rect(x, Bounds.Y + 4, tab.DesiredSize.Width, 40)); x += tab.DesiredSize.Width; }
        return finalSize;
    }
    protected override void RenderSubtree(IGraphicsContext context)
    {
        context.Save();
        try
        {
            context.SetClipRoundedRect(Bounds, 24, 24);
            context.FillRoundedRectangle(Bounds, 24, 24, Color.FromRgb(36, 36, 40));
            if (pillWidth > 0) context.FillRoundedRectangle(SelectionBounds, 20, 20, Color.FromRgb(43, 63, 82));
            base.RenderSubtree(context);
            var opaque = Color.FromRgb(36,36,40); var clear = Color.FromArgb(0,36,36,40);
            if (offset > 1) context.FillRectangle(new Rect(Bounds.X, Bounds.Y, 20, Bounds.Height), new LinearGradientBrush(new Point(Bounds.X,Bounds.Y), new Point(Bounds.X+20,Bounds.Y), [new(0,opaque),new(1,clear)]));
            if (total - offset > ActualWidth + 1) context.FillRectangle(new Rect(Bounds.Right-20, Bounds.Y, 20, Bounds.Height), new LinearGradientBrush(new Point(Bounds.Right-20,Bounds.Y), new Point(Bounds.Right,Bounds.Y), [new(0,clear),new(1,opaque)]));
        }
        finally { context.Restore(); }
    }
    protected override void OnDispose() { motion.Stop(); base.OnDispose(); }
}
