using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using Pookie.App.Messages;

namespace Pookie.App;

// Use the same inline artwork/title as the composer, retaining the source URL for copying.
internal sealed class ChatMessageText : SelectableTextBlock
{
    private const int MaximumSharedTextLength = 4096;
    private readonly List<(int Start, ChatMusicInline Inline)> links = [];
    private string? emojiText;
    private (int Start, int Length, ChatEmojiInline Inline)[] emoji = [];
    internal int EmojiCount { get { EnsureEmoji(); return emoji.Length; } }
    private ITextLayout? layout;
    private double layoutWidth = -1;
    private uint layoutDpi;
    private TextRunStyle layoutStyle;
    private IGraphicsFactory? layoutFactory;
    private double layoutSpacing;
    private TextAlignment layoutAlignment;
    private LineBoxTrim layoutTrim;
    private string? layoutCulture;
    internal Action<string>? OpenMusic;
    internal Func<bool>? SelectionClick;
    internal int MusicCount => links.Count;
    internal Rect[] MusicBounds => links.Select(link => link.Inline.Bounds).ToArray();
    internal TextLayoutLineMetrics? LastLineAt(double width)
    {
        var lines = Layout(Math.Max(1,width)).Lines;
        return lines.Count == 0 ? null : lines[^1];
    }

    internal ChatMessageText()
    {
        Focusable=true;
        MouseMove += args => Cursor = Hit(args) != null ? CursorType.Hand : CursorType.Arrow;
        MouseLeave += () => Cursor = CursorType.Arrow;
    }
    protected override ITextLayout SelectionLayout(double width) => Layout(width);
    protected override bool SelectionPressed(MouseEventArgs args)
    {
        if (SelectionClick?.Invoke() == true) return true;
        if (Hit(args) is not { } link) return false;
        ClearSelection(); OpenMusic?.Invoke(link.Link.Url); return true;
    }
    private ChatMusicInline? Hit(MouseEventArgs args)
    {
        var local = args.GetPosition(this); var point = new Point(Bounds.X + local.X, Bounds.Y + local.Y);
        return Bounds.Contains(point) ? links.FirstOrDefault(link => link.Inline.Bounds.Contains(point)).Inline : null;
    }
    internal bool UpdateMusic(IReadOnlyList<ChatMusicEditor.Link> values)
    {
        var byUrl = values.ToDictionary(value => value.Url, StringComparer.Ordinal);
        var next = ChatMusicLink.Occurrences(Text, byUrl.Keys).Take(32).ToArray();
        if (next.Length == links.Count && next.Zip(links).All(pair => pair.First.Start == pair.Second.Start && byUrl[pair.First.Url] == pair.Second.Inline.Link)) return false;
        ClearMusic();
        foreach (var (offset, url) in next) links.Add((offset, new(this, byUrl[url])));
        links.Sort((a, b) => a.Start.CompareTo(b.Start)); InvalidateMeasure(); InvalidateVisual();
        return true;
    }
    internal void ClearMusic()
    {
        if (links.Count == 0 && layout == null) return;
        foreach (var link in links) link.Inline.Dispose();
        links.Clear(); layout = null; layoutWidth = -1; Cursor = CursorType.Arrow;
        InvalidateMeasure(); InvalidateVisual();
    }
    protected override void OnTextChanged() { ClearMusic(); base.OnTextChanged(); }
    private ITextLayout Layout(double width)
    {
        EnsureEmoji();
        width = double.IsFinite(width) && width > 0 ? width : 1_000_000;
        var dpi = GetDpi();
        var style = new TextRunStyle(FontFamily, FontSize, FontWeight,FontStyle == FontStyle.Italic);
        var graphics = GetGraphicsFactory();
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        if (layout != null && layoutWidth.Equals(width) && layoutDpi == dpi && layoutStyle == style &&
            layoutFactory == graphics && layoutSpacing.Equals(LineSpacing) && layoutAlignment == TextAlignment &&
            layoutTrim == LineBoxTrim && layoutCulture == culture.Name) return layout;
        layoutWidth = width; layoutDpi = dpi; layoutStyle = style;
        layoutFactory = graphics; layoutSpacing = LineSpacing; layoutAlignment = TextAlignment;
        layoutTrim = LineBoxTrim; layoutCulture = culture.Name;
        foreach (var link in links) { link.Inline.MaximumWidth = width; link.Inline.ClearBounds(); }
        var request = new TextLayoutRequest
        {
            Text = Text.AsMemory(), Dpi = dpi, DefaultStyle = style,
            Inlines = links.Count == 0 && emoji.Length == 0 ? [] : links.Select(link => new InlineRun(link.Start, link.Inline.Link.Url.Length, link.Inline, true))
                .Concat(emoji.Where(span => !links.Any(link => span.Start < link.Start + link.Inline.Link.Url.Length && span.Start + span.Length > link.Start))
                    .Select(span => new InlineRun(span.Start, span.Length, span.Inline, true))).OrderBy(span => span.Position).ToArray(),
            Paragraph = new() { MaxWidth = width, Wrapping = TextWrapping.Wrap, LineSpacing = LineSpacing,
                Alignment = TextAlignment,LineBoxTrim = LineBoxTrim,Culture = culture }
        };
        // A recycled row alternates natural, wrapped and arranged widths. The
        // framework's owner cache keeps only one of them, reshaping the same
        // message on each return to the viewport. Its bounded content cache can
        // share plain paragraphs; inline objects must stay with their control.
        return layout = request.Inlines.Count == 0 && Text.Length <= MaximumSharedTextLength
            ? graphics.TextEngine.GetOrCreateLayout(request, TextLayoutCachePolicy.Content)
            : graphics.TextEngine.CreateLayout(request);
    }
    protected override Size MeasureContent(Size availableSize)
    {
        if (Text.Length == 0) return Size.Empty;
        var measured = Layout(availableSize.Width).MeasuredSize;
        if (links.Count == 0 && emoji.Length == 0)
        {
            var scale = GetDpi() / 96d;
            return new(Math.Ceiling(measured.Width * scale) / scale + 1 / scale, measured.Height);
        }
        return new(Math.Min(availableSize.Width, measured.Width + 1), measured.Height);
    }
    protected override void OnRender(IGraphicsContext context)
    {
        DrawSelection(context);
        if (Text.Length == 0) return;
        var paragraph = Layout(Bounds.Width);
        var y = Bounds.Y + (VerticalTextAlignment switch
        {
            TextAlignment.Center => Math.Max(0, (Bounds.Height - paragraph.ContentHeight) / 2),
            TextAlignment.Right => Math.Max(0, Bounds.Height - paragraph.ContentHeight),
            _ => 0
        });
        var options = new TextDrawOptions(Foreground, Owner: this);
        if (paragraph.MeasuredSize.Width <= Bounds.Width + .5 && paragraph.ContentHeight <= Bounds.Height + .5)
        { context.Text.Draw(paragraph, new(Bounds.X, y), in options); return; }
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.Text.Draw(paragraph, new(Bounds.X, y), in options);
        }
        finally { context.Restore(); }
    }
    protected override void OnDispose() { ClearMusic(); base.OnDispose(); }
    private void EnsureEmoji()
    {
        if (emojiText == Text) return;
        emojiText = Text;
        emoji = ChatEmojiCatalog.Spans(Text).Select(span => (span.Start, span.Length, new ChatEmojiInline(this, span.Value))).ToArray();
        layout = null; layoutWidth = -1;
    }
}
