using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class ChatComposerSurface : Panel
{
    private readonly FrameworkElement content;
    private readonly Border hint;
    internal bool Dragging { get; private set; }
    internal ChatComposerSurface(FrameworkElement content)
    {
        this.content = content;
        hint = new Border().BorderThickness(0).CornerRadius(14).Background(Color.FromArgb(245, 48, 58, 70))
            .Child(new StackPanel().Vertical().Spacing(6).Center().Children(
                Icons.View("paperclip", 25, Color.FromRgb(170, 211, 246)).CenterHorizontal(),
                new TextBlock().Text("Отпусти файлы здесь").FontSize(14).CenterHorizontal()));
        hint.Opacity = 0; hint.IsHitTestVisible = false;
        hint.Transitions = [Transition.Create(UIElement.OpacityProperty, 170)];
        content.Transitions = [Transition.Create(UIElement.OpacityProperty, 170)];
        Add(content); Add(hint);
    }
    internal void SetDragging(bool value)
    {
        Dragging = value; hint.Opacity = value ? 1 : 0; content.Opacity = value ? .2 : 1;
    }
    protected override Size MeasureContent(Size availableSize)
    {
        content.Measure(availableSize); hint.Measure(content.DesiredSize); return content.DesiredSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
        content.Arrange(bounds); hint.Arrange(bounds); return finalSize;
    }
}
