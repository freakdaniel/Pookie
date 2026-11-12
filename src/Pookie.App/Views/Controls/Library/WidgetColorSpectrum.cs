using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// GPU gradients keep dragging independent of CPU bitmap generation. Hue and
// saturation/value have separate hit regions and retain their state at black.
internal sealed class WidgetColorSpectrum : Control
{
    private HsvColor hsv;
    private bool dragging, hueDrag;
    internal Action<HsvColor>? Changed;
    internal HsvColor Hsv => hsv;
    internal Rect SpectrumBounds => new(Bounds.X + 6, Bounds.Y + 6, Math.Max(0, Bounds.Width - 12), Math.Max(0, Bounds.Height - 46));
    internal Rect HueBounds => new(Bounds.X + 6, Bounds.Bottom - 26, Math.Max(0, Bounds.Width - 12), 16);
    internal WidgetColorSpectrum() { Height = 208; Focusable = true; IsTabStop = true; Cursor = CursorType.Cross; }
    internal void Set(HsvColor value) { hsv = value; InvalidateVisual(); }
    protected override Size MeasureContent(Size availableSize) => new(260, 208);
    protected override void OnRender(IGraphicsContext context)
    {
        var square = SpectrumBounds; var rail = HueBounds;
        context.Save(); context.SetClipRoundedRect(square, 10, 10);
        context.FillRectangle(square, new LinearGradientBrush(new(square.X, square.Y), new(square.Right, square.Y),
            [new(0, Color.White), new(1, HsvColor.HueToRgb(hsv.H))]));
        context.FillRectangle(square, new LinearGradientBrush(new(square.X, square.Y), new(square.X, square.Bottom),
            [new(0, Color.Transparent), new(1, Color.Black)]));
        context.Restore();
        context.Save(); context.SetClipRoundedRect(rail, 8, 8);
        context.FillRectangle(rail, new LinearGradientBrush(new(rail.X, rail.Y), new(rail.Right, rail.Y),
            [new(0, HsvColor.HueToRgb(0)), new(1d / 6, HsvColor.HueToRgb(60)), new(2d / 6, HsvColor.HueToRgb(120)),
             new(.5, HsvColor.HueToRgb(180)), new(4d / 6, HsvColor.HueToRgb(240)), new(5d / 6, HsvColor.HueToRgb(300)), new(1, HsvColor.HueToRgb(0))]));
        context.Restore();
        Marker(context, new(square.X + square.Width * hsv.S, square.Y + square.Height * (1 - hsv.V)), 5);
        Marker(context, new(rail.X + rail.Width * hsv.H / 360, rail.Center.Y), 7);
        if (IsFocused && KeyboardFocusFeedback.IsVisible(this))
            context.DrawRoundedRectangle(Bounds, 14, 14, Color.FromArgb(140, 255, 255, 255), 1);
    }
    private static void Marker(IGraphicsContext context, Point point, double radius)
    {
        context.DrawEllipse(new(point.X - radius - 1, point.Y - radius - 1, radius * 2 + 2, radius * 2 + 2), Color.FromArgb(180, 0, 0, 0), 2);
        context.DrawEllipse(new(point.X - radius, point.Y - radius, radius * 2, radius * 2), Color.White, 2);
    }
    private void Update(Point point)
    {
        var area = hueDrag ? HueBounds : SpectrumBounds;
        var x = (float)Math.Clamp((point.X - area.X) / Math.Max(1, area.Width), 0, 1);
        var y = (float)Math.Clamp((point.Y - area.Y) / Math.Max(1, area.Height), 0, 1);
        Set(hueDrag ? new(Math.Min(359.99f, x * 360), hsv.S, hsv.V) : new(hsv.H, x, 1 - y)); Changed?.Invoke(hsv);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        var point = e.GetPosition((Window)FindVisualRoot()!);
        hueDrag = HueBounds.Inflate(0, 6).Contains(point);
        if (!hueDrag && !SpectrumBounds.Contains(point)) return;
        dragging = true; Focus(); KeyboardFocusFeedback.Set(this, false);
        (FindVisualRoot() as Window)?.CaptureMouse(this); Update(point); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) { Update(e.GetPosition((Window)FindVisualRoot()!)); e.Handled = true; } }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging || e.Button != MouseButton.Left) return;
        dragging = false; (FindVisualRoot() as Window)?.ReleaseMouseCapture(); e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        var direction = e.Key is Key.Right or Key.Up ? 1 : -1;
        if (e.Modifiers.HasFlag(ModifierKeys.Shift)) Set(new((hsv.H + direction * 3 + 360) % 360, hsv.S, hsv.V));
        else if (e.Key is Key.Left or Key.Right) Set(new(hsv.H, Math.Clamp(hsv.S + direction * .01f, 0, 1), hsv.V));
        else Set(new(hsv.H, hsv.S, Math.Clamp(hsv.V + direction * .01f, 0, 1)));
        Changed?.Invoke(hsv); e.Handled = true;
    }
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    { if (dragging && oldRoot is Window window) { dragging = false; window.ReleaseMouseCapture(); } base.OnVisualRootChanged(oldRoot, newRoot); }
}
