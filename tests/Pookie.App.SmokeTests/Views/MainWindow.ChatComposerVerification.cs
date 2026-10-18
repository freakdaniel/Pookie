using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatComposerLayoutAsync()
    {
        var draft = chatInput.Text;
        chatInput.Text = "";
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatComposer.ActualHeight <= 57);
        var compact = chatComposer.ActualHeight;
        var textInset = chatInput.TextViewportBounds.Y - chatComposerLayout.Bounds.Y;
        var bottom = chatComposer.Bounds.Bottom;
        var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
        RequestChatBottom(); await WaitForLikedLayoutAsync(() => !chatScrollBottom);
        CaptureUiPreview("chats-composer-compact");
        var samples = new List<double>();
        var insets = new List<double>();
        var textInsets = new List<double>();
        var textOrigins = new List<double>();
        void Paint()
        {
            samples.Add(chatComposer.ActualHeight);
            textInsets.Add(chatInput.TextViewportBounds.Y - chatComposerLayout.Bounds.Y);
            textOrigins.Add(chatInput.TextViewportBounds.Y - chatInput.VerticalOffset - chatComposerLayout.Bounds.Y);
            var last = ChatRows.FirstOrDefault(row => row.Bound && row.Message?.Id == chatMessageItems.Last().Id);
            if (last != null) insets.Add(viewer.Bounds.Bottom - last.Root.Bounds.Bottom);
        }
        chatViewport.BeforePaint += Paint;
        var wrapText = new string('1', (int)(chatInput.ActualWidth / 7) * 3);
        chatInput.Text = wrapText; chatInput.CaretPosition = wrapText.Length;
        try { await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatComposer.ActualHeight > compact + 10); }
        finally { chatViewport.BeforePaint -= Paint; }
        var expanded = chatComposer.ActualHeight;
        if (!samples.Any(height => height > compact + 1 && height < expanded - 1) ||
            Math.Abs(chatComposer.Bounds.Bottom - bottom) > 1 || insets.Count == 0 || insets.Max() - insets.Min() > 1 ||
            textInsets.Any(inset => Math.Abs(inset - textInset) > .5))
            throw new InvalidOperationException("Wrapped input did not grow smoothly upwards while keeping the thread anchored.");
        if (Math.Abs(chatAttach.Bounds.Bottom - chatSend.Bounds.Bottom) > 1 || Math.Abs(chatGif.Bounds.Bottom - chatSend.Bounds.Bottom) > 1)
            throw new InvalidOperationException("Composer action buttons left the bottom row.");
        CaptureUiPreview("chats-composer-wrapped");
        chatInput.Text = string.Join('\n', Enumerable.Repeat(new string('1', 40), 40));
        chatInput.CaretPosition = chatInput.Text.Length;
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatInput.VerticalOffset > 1 && chatComposerLayout.FadeOpacity > .95);
        if (chatComposer.ActualHeight > 261 || chatComposer.ActualHeight < expanded || chatInput.GetCharRectInWindow(chatInput.CaretPosition).Bottom > chatInput.TextViewportBounds.Bottom + 1)
            throw new InvalidOperationException("Maximum composer height or caret visibility is incorrect.");
        VisualTree.Visit(chatInput, child =>
        {
            if (child is ScrollBar bar && (bar.Opacity != 0 || bar.IsHitTestVisible))
                throw new InvalidOperationException("Composer exposed an internal scrollbar.");
        });
        if (chatInput.HitTest(new Point(chatInput.Bounds.Right - 2, chatInput.Bounds.Y + chatInput.ActualHeight / 2)) != chatInput)
            throw new InvalidOperationException("An invisible scrollbar intercepted the editor's right edge.");
        CaptureUiPreview("chats-composer-maximum");
        chatInput.CaretPosition = 0;
        await WaitForLikedLayoutAsync(() => chatComposerLayout.FadeOpacity < .05);
        samples.Clear(); textInsets.Clear(); chatViewport.BeforePaint += Paint;
        chatInput.Text = "";
        try { await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && Math.Abs(chatComposer.ActualHeight - compact) < 1); }
        finally { chatViewport.BeforePaint -= Paint; }
        if (!samples.Any(height => height > compact + 1 && height < expanded - 1) || textInsets.Any(inset => Math.Abs(inset - textInset) > .5))
            throw new InvalidOperationException("Clearing the draft snapped the composer height.");
        foreach (var singleLine in new[] { "", "Одна строка" })
        {
            chatInput.Text = singleLine; chatInput.CaretPosition = singleLine.Length;
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing);
            chatInput.ReplaceSelection("\n");
            await WaitForLikedLayoutAsync(() => chatComposerSurface.IsResizing && chatComposer.ActualHeight > compact + 2);
            samples.Clear(); textInsets.Clear(); textOrigins.Clear(); chatViewport.BeforePaint += Paint;
            chatInput.Undo();
            if (singleLine.Length == 0) Window.FocusManager.ClearFocus();
            try
            {
                await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && Math.Abs(chatComposer.ActualHeight - compact) < 1);
                await WaitForLoginFrameAsync();
            }
            finally { chatViewport.BeforePaint -= Paint; }
            if (chatInput.Text != singleLine || textInsets.Count < 2 || textInsets.Any(inset => Math.Abs(inset - textInset) > .5) ||
                textOrigins.Any(origin => Math.Abs(origin - textInset) > .5))
                throw new InvalidOperationException("Quickly undoing a newline made the single-line text/placeholder recenter abruptly.");
        }
        chatInput.Text = draft;
        await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing);
    }
}
