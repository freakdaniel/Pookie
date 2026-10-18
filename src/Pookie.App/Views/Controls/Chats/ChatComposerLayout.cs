using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// Measure the real editor at its wrap width. Its viewport follows the caret
// after reaching the cap, while the action buttons stay on the bottom row.
internal sealed class ChatComposerLayout : Panel
{
    private const double TextInset = 6;
    private readonly MultiLineTextBox editor;
    private readonly FrameworkElement attachments, attach, gif, send;
    private readonly Func<double> maximumHeight;
    private readonly EditorFade fade;
    private readonly List<ScrollBar> scrollbars = [];
    private double attachmentHeight, editorHeight;
    internal double HeightLimit { get; private set; }
    internal double FadeOpacity => fade.Opacity;

    internal ChatComposerLayout(MultiLineTextBox editor, FrameworkElement attachments,
        FrameworkElement attach, FrameworkElement gif, FrameworkElement send, Func<double> maximumHeight, Func<Color> background)
    {
        this.editor = editor; this.attachments = attachments; this.attach = attach; this.gif = gif; this.send = send;
        this.maximumHeight = maximumHeight;
        editor.SizeToDocument = true;
        // The native editor keeps caret navigation, selection and wheel handling;
        // its overlay bars do not occupy space and need neither paint nor hit testing.
        VisualTree.Visit(editor, child =>
        { if (child is ScrollBar bar) { scrollbars.Add(bar); bar.Opacity = 0; bar.IsHitTestVisible = false; } });
        fade = new(background);
        Add(attachments); Add(editor); Add(fade); Add(attach); Add(gif); Add(send);
        editor.ScrollOffsetChanged += OnEditorScroll;
        editor.DocumentChanged += OnEditorDocumentChanged;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 640;
        HeightLimit = Math.Clamp(maximumHeight(), 80, 244);
        attachments.Measure(new Size(width, 44));
        attachmentHeight = attachments.IsVisible ? attachments.DesiredSize.Height + 8 : 0;
        foreach (var button in new[] { attach, gif, send }) button.Measure(new Size(40, 40));
        editor.Measure(new Size(Math.Max(40, width - 132), Math.Max(28, HeightLimit - attachmentHeight - 2 * TextInset)));
        editorHeight = Math.Max(28, editor.DesiredSize.Height);
        fade.Measure(new Size(Math.Max(40, width - 132), 18));
        return new(width, attachmentHeight + Math.Max(40, editorHeight + 2 * TextInset));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = Bounds.X; var bottom = Bounds.Bottom;
        attachments.Arrange(new Rect(x, Bounds.Y, finalSize.Width, attachments.IsVisible ? attachments.DesiredSize.Height : 0));
        // Keep the same text origin throughout resize, including the final
        // compact frame. Switching to centering at 40 px caused a visible jump.
        var rowHeight = Math.Max(40, finalSize.Height - attachmentHeight);
        var textHeight = Math.Max(28, rowHeight - 2 * TextInset);
        var textY = bottom - rowHeight + TextInset;
        editor.Arrange(new Rect(x + 44, textY, Math.Max(40, finalSize.Width - 132), textHeight));
        // MultiLineTextBox hit-tests its overlay bars directly. Empty their
        // geometry as well, so the hidden strip cannot intercept editor clicks.
        foreach (var bar in scrollbars) bar.Arrange(Rect.Empty);
        var viewport = editor.TextViewportBounds;
        fade.Arrange(new Rect(viewport.X, viewport.Y, viewport.Width, Math.Min(18, viewport.Height)));
        attach.Arrange(new Rect(x, bottom - 40, 40, 40));
        gif.Arrange(new Rect(Bounds.Right - 84, bottom - 40, 40, 40));
        send.Arrange(new Rect(Bounds.Right - 40, bottom - 40, 40, 40));
        UpdateFade();
        return finalSize;
    }

    private void OnEditorScroll(Aprillz.MewUI.Text.ITextViewHost _) => UpdateFade();
    private void OnEditorDocumentChanged(Aprillz.MewUI.Text.ITextViewHost _) => editor.InvalidateMeasure();
    private void UpdateFade() => fade.Opacity = editor.VerticalOffset > 1 ? 1 : 0;
    protected override void OnDispose()
    { editor.ScrollOffsetChanged -= OnEditorScroll; editor.DocumentChanged -= OnEditorDocumentChanged; base.OnDispose(); }

    private sealed class EditorFade : Control
    {
        private readonly Func<Color> background;
        internal EditorFade(Func<Color> background)
        {
            this.background = background; IsHitTestVisible = false; Opacity = 0;
            Transitions = [Transition.Create(UIElement.OpacityProperty, 170)];
        }
        protected override void OnRender(IGraphicsContext context)
        {
            var color = background();
            context.FillRectangle(Bounds, new LinearGradientBrush(new Point(Bounds.X, Bounds.Y), new Point(Bounds.X, Bounds.Bottom),
                [new GradientStop(0, color), new GradientStop(1, Color.FromArgb(0, color.R, color.G, color.B))]));
        }
    }
}
