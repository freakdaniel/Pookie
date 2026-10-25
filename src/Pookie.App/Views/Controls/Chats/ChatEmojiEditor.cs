using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed class ChatEmojiEditor : ITextElementGenerator, IDisposable
{
    private readonly MultiLineTextBox editor;
    private (int Start, int Length, ChatEmojiInline Inline)[] spans = [];
    internal int Count => spans.Length;
    internal ChatEmojiEditor(MultiLineTextBox editor)
    {
        this.editor = editor;
        editor.Extensions.ElementGenerators.Add(this);
        editor.TextChanged += Update;
        Update(editor.Text);
    }
    private void Update(string text)
    {
        spans = ChatEmojiCatalog.Spans(text).Select(span => (span.Start, span.Length, new ChatEmojiInline(editor, span.Value))).ToArray();
        editor.InvalidateTextView();
    }
    public int GetFirstInterestedOffset(in TextElementScanContext context, int startOffset)
    {
        var index = LowerBound(startOffset);
        return index < spans.Length ? spans[index].Start : -1;
    }
    public GeneratedTextElement? ConstructElement(in TextElementScanContext context, int offset)
    {
        var index = LowerBound(offset);
        if (index >= spans.Length || spans[index].Start != offset) return null;
        var span = spans[index];
        return new(span.Length, span.Length, span.Inline, true);
    }
    private int LowerBound(int offset)
    {
        var start = 0; var end = spans.Length;
        while (start < end)
        {
            var middle = start + (end - start) / 2;
            if (spans[middle].Start < offset) start = middle + 1; else end = middle;
        }
        return start;
    }
    public void Dispose()
    {
        editor.TextChanged -= Update;
        editor.Extensions.ElementGenerators.Remove(this);
        spans = [];
    }
}
