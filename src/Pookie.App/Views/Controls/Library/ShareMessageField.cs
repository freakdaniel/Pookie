using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// The editor's native scrollbar sits in the dialog gutter, outside the glass.
internal sealed class ShareMessageField : Panel
{
    internal const double ScrollGutter = 18;
    internal GlassCard Surface { get; } = new GlassCard { HoverEnabled = false, ShadowEnabled = false }
        .CornerRadius(24).Background(GlassSheen.LightControlTint(TrackButtons.GlassSurface));
    private readonly MultiLineTextBox input;
    internal ShareMessageField(MultiLineTextBox input)
    {
        this.input = input; Height = 76;
        Add(Surface); Add(input);
        input.GotFocus += () => Surface.SetEmphasized(true);
        input.LostFocus += () => Surface.SetEmphasized(false);
    }
    protected override Size MeasureContent(Size availableSize)
    {
        Surface.Measure(new(Math.Max(0, availableSize.Width - ScrollGutter), Height));
        input.Measure(new(availableSize.Width, Height));
        return new(availableSize.Width, Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Surface.Arrange(new(Bounds.X, Bounds.Y, Math.Max(0, finalSize.Width - ScrollGutter), finalSize.Height));
        input.Arrange(Bounds);
        return finalSize;
    }
}
