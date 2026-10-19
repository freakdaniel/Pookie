using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using Pookie.App.Messages;

namespace Pookie.App;

// Presentation only: the document, clipboard, undo stack and sent body keep the URL.
internal sealed class ChatMusicEditor : ITextElementGenerator, IDisposable
{
    internal sealed record Link(string Url, string Label, ImageSource? Cover);
    private sealed record Span(int Start, int Length, ChatMusicInline Chip) { internal int End => Start + Length; }
    private readonly MultiLineTextBox editor;
    private readonly Action<string> open;
    private readonly List<Span> spans = [];
    private double width;
    internal int Count => spans.Count;
    internal Rect[] ChipBounds => spans.Select(span => span.Chip.Bounds).ToArray();

    internal ChatMusicEditor(MultiLineTextBox editor, Action<string> open)
    {
        this.editor = editor; this.open = open;
        editor.Extensions.ElementGenerators.Add(this);
        editor.MouseMove += Move; editor.MouseLeave += Leave; editor.MouseDown += Click; editor.KeyDown += KeyDown;
        editor.TextInput += TextInput; editor.TextCompositionStart += CompositionStart;
        editor.SizeChanged += Resize;
    }

    internal void Update(IReadOnlyList<Link> links)
    {
        var next = new List<Span>(); var text = editor.Text;
        var byUrl = links.ToDictionary(link => link.Url, StringComparer.Ordinal);
        foreach (var (offset, url) in ChatMusicLink.Occurrences(text, byUrl.Keys).Take(32))
        {
            var link = byUrl[url];
            var old = spans.FirstOrDefault(span => span.Start == offset && span.Chip.Link == link);
            next.Add(new(offset, link.Url.Length, old?.Chip ?? new ChatMusicInline(editor, link)));
        }
        if (next.Count == spans.Count && next.Zip(spans).All(pair => pair.First == pair.Second)) return;
        foreach (var span in spans) if (!next.Any(value => ReferenceEquals(value.Chip, span.Chip))) span.Chip.Dispose();
        spans.Clear(); spans.AddRange(next.OrderBy(span => span.Start));
        editor.InvalidateTextView(); editor.InvalidateMeasure();
    }

    private static bool Matches(Span span, IReadOnlyTextDocument document) => span.End <= document.TextLength &&
        document.GetText(span.Start, span.Length) == span.Chip.Link.Url;
    public int GetFirstInterestedOffset(in TextElementScanContext context, int startOffset)
    {
        foreach (var span in spans) if (span.Start >= startOffset && Matches(span, context.Document)) return span.Start;
        return -1;
    }
    public GeneratedTextElement? ConstructElement(in TextElementScanContext context, int offset)
    {
        foreach (var span in spans) if (span.Start == offset && Matches(span, context.Document)) return new(span.Length, span.Length, span.Chip, true);
        return null;
    }

    private Span? Hit(Point point) => editor.TextViewportBounds.Contains(point)
        ? spans.FirstOrDefault(span => span.Chip.Bounds.Contains(point) && Matches(span, editor.Document)) : null;
    private void Move(MouseEventArgs args) => editor.Cursor = Hit(WindowPoint(args)) != null ? CursorType.Hand : CursorType.IBeam;
    private Point WindowPoint(MouseEventArgs args)
    { var point = args.GetPosition(editor); return new(editor.Bounds.X + point.X, editor.Bounds.Y + point.Y); }
    private void Leave() => editor.Cursor = CursorType.IBeam;
    private void Click(MouseEventArgs args)
    {
        if (args.Button != MouseButton.Left || args.Modifiers.HasFlag(ModifierKeys.Shift) || Hit(WindowPoint(args)) is not { } span) return;
        args.Handled = true; open(span.Chip.Link.Url);
    }
    private void KeyDown(KeyEventArgs args)
    {
        if (args.Handled || editor.IsReadOnly) return;
        if (editor.SelectionLength != 0)
        {
            if (args.Key is Key.Backspace or Key.Delete or Key.Enter) ExpandSelection();
            return;
        }
        if (args.Modifiers != ModifierKeys.None &&
            !(args.Modifiers == ModifierKeys.Control && args.Key is Key.Backspace or Key.Delete)) return;
        var caret = editor.CaretPosition;
        var span = spans.FirstOrDefault(value => Matches(value, editor.Document) && (args.Key is Key.Backspace or Key.Left
            ? value.Start < caret && caret <= value.End : value.Start <= caret && caret < value.End));
        if (span == null) return;
        if (args.Key is Key.Backspace or Key.Delete)
            editor.Select(span.Start, span.Length); // The editor deletes the selection in its undo transaction.
        else if (args.Key is Key.Left or Key.Right)
        { args.Handled = true; editor.MoveCaret(args.Key == Key.Left ? span.Start : span.End, false); }
    }
    private void ExpandSelection()
    {
        var start = editor.SelectionStart; var end = start + editor.SelectionLength;
        if (start == end) return;
        foreach (var span in spans)
            if (Matches(span, editor.Document) && start < span.End && end > span.Start)
            { start = Math.Min(start, span.Start); end = Math.Max(end, span.End); }
        editor.Select(start, end - start);
    }
    private void PrepareInsertion()
    {
        ExpandSelection();
        if (editor.SelectionLength != 0) return;
        var caret = editor.CaretPosition;
        var span = spans.FirstOrDefault(value => Matches(value, editor.Document) && value.Start < caret && caret < value.End);
        if (span != null) editor.MoveCaret(span.End, false);
    }
    internal void InsertText(string text)
    {
        if (editor.IsReadOnly || text.Length == 0) return;
        PrepareInsertion();
        var start = editor.SelectionStart; var end = start + editor.SelectionLength;
        // Keep the raw URL delimited, including when typing immediately after pasting it.
        var prefix = !char.IsWhiteSpace(text[0]) && spans.Any(span => span.End == start && Matches(span, editor.Document));
        var suffix = !char.IsWhiteSpace(text[^1]) && spans.Any(span => span.Start == end && Matches(span, editor.Document));
        editor.ReplaceSelection((prefix ? " " : "") + text + (suffix ? " " : ""));
        if (suffix) editor.MoveCaret(editor.CaretPosition - 1, false);
    }
    internal void Paste()
    {
        var clipboard = editor.ClipboardService ?? Application.Current.PlatformServices.Clipboard;
        if (clipboard?.TryGetText(out var text) != true) return;
        // Metadata arrives asynchronously. Leave the caret outside a freshly pasted URL
        // even when the user starts typing before its title has been resolved.
        if (ChatMusicLink.Find(text).Any(url => text.EndsWith(url, StringComparison.Ordinal))) text += " ";
        InsertText(text);
    }
    private void TextInput(TextInputEventArgs args)
    {
        if (args.Handled || editor.IsReadOnly) return;
        args.Handled = true; InsertText(args.Text);
    }
    private void CompositionStart(TextCompositionEventArgs args)
    {
        if (args.Handled || editor.IsReadOnly) return;
        PrepareInsertion();
        var start = editor.SelectionStart; var end = start + editor.SelectionLength;
        if (spans.Any(span => span.End == start && Matches(span, editor.Document))) editor.ReplaceSelection(" ");
        else if (spans.Any(span => span.Start == end && Matches(span, editor.Document)))
        { editor.ReplaceSelection(" "); editor.MoveCaret(editor.CaretPosition - 1, false); }
    }
    private void Resize(SizeChangedEventArgs args)
    {
        if (Math.Abs(width - editor.ActualWidth) < 1) return;
        width = editor.ActualWidth;
        if (spans.Count > 0) { foreach (var span in spans) span.Chip.ClearBounds(); editor.InvalidateTextView(); editor.InvalidateMeasure(); }
    }
    public void Dispose()
    {
        editor.Extensions.ElementGenerators.Remove(this);
        editor.MouseMove -= Move; editor.MouseLeave -= Leave; editor.MouseDown -= Click; editor.KeyDown -= KeyDown; editor.SizeChanged -= Resize;
        editor.TextInput -= TextInput; editor.TextCompositionStart -= CompositionStart;
        foreach (var span in spans) span.Chip.Dispose(); spans.Clear();
    }

}
