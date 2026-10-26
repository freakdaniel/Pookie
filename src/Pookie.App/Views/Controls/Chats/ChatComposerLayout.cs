using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
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
    private readonly FrameworkElement voice, reply;
    private readonly Func<double> maximumHeight;
    private readonly EditorFade fade;
    private readonly List<ScrollBar> scrollbars = [];
    private double attachmentHeight, editorHeight;
    private readonly AnimationClock mode = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.25,.1,.25,1));
    private bool voiceMode;
    private double voiceAmount;
    private bool replyOpen, replyTransition;
    internal double ReplyHeight { get; private set; }
    internal bool IsChangingMode => mode.IsRunning;
    internal double VoiceAmount => voiceAmount;
    internal double HeightLimit { get; private set; }
    internal double FadeOpacity => fade.Opacity;

    internal ChatComposerLayout(MultiLineTextBox editor, FrameworkElement attachments,
        FrameworkElement attach, FrameworkElement gif, FrameworkElement send, FrameworkElement voice, FrameworkElement reply, Func<double> maximumHeight, Func<Color> background)
    {
        this.editor = editor; this.attachments = attachments; this.attach = attach; this.gif = gif; this.send = send;
        this.maximumHeight = maximumHeight; this.voice = voice; this.reply = reply;
        editor.SizeToDocument = true;
        // The native editor keeps caret navigation, selection and wheel handling;
        // its overlay bars do not occupy space and need neither paint nor hit testing.
        VisualTree.Visit(editor, child =>
        { if (child is ScrollBar bar) { scrollbars.Add(bar); bar.Opacity = 0; bar.IsHitTestVisible = false; } });
        fade = new(background);
        Add(reply); Add(attachments); Add(editor); Add(fade); Add(attach); Add(gif); Add(voice); Add(send);
        editor.ScrollOffsetChanged += OnEditorScroll;
        editor.DocumentChanged += OnEditorDocumentChanged;
    }

    internal bool SetReplyOpen(bool value)
    {
        if (replyOpen == value) return false;
        replyOpen = value; replyTransition = true;
        // Keep the old quote alive until its closing mask reaches zero.
        reply.IsVisible = true; reply.IsHitTestVisible = value;
        InvalidateMeasure(); return true;
    }

    internal void CompleteReplyTransition()
    {
        if (!replyTransition) return;
        replyTransition = false;
        reply.IsVisible = replyOpen;
        InvalidateArrange();
    }

    internal void SetVoiceMode(bool value, bool snap = false)
    {
        if (voiceMode == value && !snap) return;
        mode.Stop(); voiceMode = value;
        var from = voiceAmount;
        // Retain both native subtrees throughout the fade, including on reversal.
        // Hit testing follows the requested mode, not the fading old surface.
        editor.IsVisible = attach.IsVisible = true; voice.IsVisible = true;
        editor.IsHitTestVisible = attach.IsHitTestVisible = gif.IsHitTestVisible = !value;
        voice.IsHitTestVisible = value;
        mode.TickCallback = t => { voiceAmount = from + ((value ? 1 : 0) - from) * t; ApplyMode(); };
        mode.CompletedCallback = () =>
        {
            voiceAmount = value ? 1 : 0;
            editor.IsVisible = attach.IsVisible = !value; voice.IsVisible = value;
            ApplyMode(); InvalidateMeasure();
        };
        if (snap || FindVisualRoot() is not Window) mode.CompletedCallback?.Invoke();
        else mode.Start();
        ApplyMode(); InvalidateMeasure();
    }

    private void ApplyMode()
    {
        editor.Opacity = attach.Opacity = gif.Opacity = 1 - voiceAmount;
        voice.Opacity = voiceAmount; UpdateFade(); InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 640;
        HeightLimit = Math.Clamp(maximumHeight(), 80, 244);
        attachments.Measure(new Size(width, 44));
        attachmentHeight = attachments.IsVisible && !voiceMode ? attachments.DesiredSize.Height + 8 : 0;
        reply.Measure(new Size(width,48));
        var replyHeight = replyOpen ? 48 : 0;
        voice.Measure(new Size(Math.Max(40,width-44),40));
        foreach (var button in new[] { attach, gif, send }) button.Measure(new Size(40, 40));
        editor.Measure(new Size(Math.Max(40, width - (gif.IsVisible ? 132 : 88)), Math.Max(28, HeightLimit - replyHeight - attachmentHeight - 2 * TextInset)));
        editorHeight = voiceMode ? 28 : Math.Max(28, editor.DesiredSize.Height);
        fade.Measure(new Size(Math.Max(40, width - (gif.IsVisible ? 132 : 88)), 18));
        return new(width, replyHeight + attachmentHeight + Math.Max(40, editorHeight + 2 * TextInset));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        voice.Arrange(voice.IsVisible ? new Rect(Bounds.X,Bounds.Bottom-40,Math.Max(0,finalSize.Width-44),40) : Rect.Empty);
        var x = Bounds.X; var bottom = Bounds.Bottom;
        // Keep the same text origin throughout resize, including the final
        // compact frame. Switching to centering at 40 px caused a visible jump.
        var rowHeight = replyTransition ? Math.Max(40,editorHeight+2*TextInset) : Math.Max(40, finalSize.Height-attachmentHeight-(replyOpen ? 48 : 0));
        ReplyHeight = replyTransition ? Math.Clamp(finalSize.Height-attachmentHeight-rowHeight,0,48) : replyOpen ? 48 : 0;
        reply.Arrange(new Rect(x,bottom-rowHeight-attachmentHeight-48,finalSize.Width,48));
        attachments.Arrange(new Rect(x,bottom-rowHeight-attachmentHeight,finalSize.Width,attachments.IsVisible && !voiceMode ? attachments.DesiredSize.Height : 0));
        var textHeight = Math.Max(28, rowHeight - 2 * TextInset);
        var textY = bottom - rowHeight + TextInset;
        editor.Arrange(new Rect(x + 44, textY, Math.Max(40, finalSize.Width - (gif.IsVisible ? 132 : 88)), textHeight));
        // MultiLineTextBox hit-tests its overlay bars directly. Empty their
        // geometry as well, so the hidden strip cannot intercept editor clicks.
        foreach (var bar in scrollbars) bar.Arrange(Rect.Empty);
        var viewport = editor.TextViewportBounds;
        fade.Arrange(new Rect(viewport.X, viewport.Y, viewport.Width, Math.Min(18, viewport.Height)));
        attach.Arrange(new Rect(x, bottom - 40, 40, 40));
        gif.Arrange(gif.IsVisible ? new Rect(Bounds.Right - 84, bottom - 40, 40, 40) : Rect.Empty);
        send.Arrange(new Rect(Bounds.Right - 40, bottom - 40, 40, 40));
        UpdateFade();
        return finalSize;
    }

    private void OnEditorScroll(Aprillz.MewUI.Text.ITextViewHost _) => UpdateFade();
    private void OnEditorDocumentChanged(Aprillz.MewUI.Text.ITextViewHost _) => editor.InvalidateMeasure();
    private void UpdateFade() => fade.Opacity = editor.VerticalOffset > 1 ? 1 - voiceAmount : 0;
    protected override void OnDispose()
    { mode.Stop(); editor.ScrollOffsetChanged -= OnEditorScroll; editor.DocumentChanged -= OnEditorDocumentChanged; base.OnDispose(); }

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
