using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using Pookie.App.Messages;

namespace Pookie.App;

// Use the same inline artwork/title as the composer, retaining the source URL for copying.
internal sealed class ChatMessageText : TextBlock
{
    private readonly List<(int Start, ChatMusicInline Inline)> links = [];
    private ITextLayout? layout;
    private double layoutWidth = -1;
    private uint layoutDpi;
    private TextRunStyle layoutStyle;
    internal Action<string>? OpenMusic;
    internal int MusicCount => links.Count;
    internal Rect[] MusicBounds => links.Select(link => link.Inline.Bounds).ToArray();

    internal ChatMessageText()
    {
        MouseMove += args => Cursor = Hit(args) != null ? CursorType.Hand : CursorType.Arrow;
        MouseLeave += () => Cursor = CursorType.Arrow;
        MouseDown += args =>
        {
            if (args.Button != MouseButton.Left || Hit(args) is not { } link) return;
            args.Handled = true; OpenMusic?.Invoke(link.Link.Url);
        };
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
        var dpi = (uint)Math.Round(96 * (FindVisualRoot() is Window window ? window.DpiScale : 1));
        var style = new TextRunStyle(FontFamily, FontSize, FontWeight);
        if (layout != null && layoutWidth.Equals(width) && layoutDpi == dpi && layoutStyle == style) return layout;
        layoutWidth = width; layoutDpi = dpi; layoutStyle = style;
        foreach (var link in links) { link.Inline.MaximumWidth = width; link.Inline.ClearBounds(); }
        return layout = Application.Current.GraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = Text.AsMemory(), Dpi = dpi, DefaultStyle = style,
            Inlines = links.Select(link => new InlineRun(link.Start, link.Inline.Link.Url.Length, link.Inline, true)).ToArray(),
            Paragraph = new() { MaxWidth = width, Wrapping = TextWrapping.Wrap, LineSpacing = LineSpacing }
        });
    }
    protected override Size MeasureContent(Size availableSize)
    {
        if (links.Count == 0) return base.MeasureContent(availableSize);
        var measured = Layout(availableSize.Width).MeasuredSize;
        return new(Math.Min(availableSize.Width, measured.Width + 1), measured.Height);
    }
    protected override void OnRender(IGraphicsContext context)
    {
        if (links.Count == 0) { base.OnRender(context); return; }
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.Text.Draw(Layout(Bounds.Width), new(Bounds.X, Bounds.Y), new TextDrawOptions(Foreground, Owner: this));
        }
        finally { context.Restore(); }
    }
    protected override void OnDispose() { ClearMusic(); base.OnDispose(); }
}
