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
    private string? emojiText;
    private (int Start, int Length, ChatEmojiInline Inline)[] emoji = [];
    internal int EmojiCount { get { EnsureEmoji(); return emoji.Length; } }
    private ITextLayout? layout;
    private double layoutWidth = -1;
    private uint layoutDpi;
    private TextRunStyle layoutStyle;
    internal Action<string>? OpenMusic;
    internal Action<string>? Copy;
    internal Func<bool>? SelectionClick;
    private int selectionAnchor,selectionEnd;
    private bool selecting;
    internal string? SelectedText=>selectionAnchor==selectionEnd?null:Text[Math.Min(selectionAnchor,selectionEnd)..Math.Max(selectionAnchor,selectionEnd)];
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
        MouseDown += args =>
        {
            if(args.Button==MouseButton.Left && SelectionClick?.Invoke()==true){args.Handled=true;return;}
            if (args.Button != MouseButton.Left || Hit(args) is not { } link) return;
            args.Handled = true; selecting=false; selectionAnchor=selectionEnd=0; OpenMusic?.Invoke(link.Link.Url);
        };
        MouseDown+=args=>
        {
            if(args.Handled||args.Button!=MouseButton.Left)return;
            Focus();selecting=true;
            var hit=Layout(Bounds.Width).HitTestPoint(args.GetPosition(this));
            var offset=Math.Clamp(hit.FirstCharacterIndex+hit.TrailingLength,0,Text.Length);
            if(!args.Modifiers.HasFlag(ModifierKeys.Shift))selectionAnchor=offset;
            selectionEnd=offset;
            if(FindVisualRoot() is Window window)window.CaptureMouse(this);
            args.Handled=true;InvalidateVisual();
        };
        MouseMove+=args=>
        {
            if(!selecting||!args.LeftButton)return;
            var hit=Layout(Bounds.Width).HitTestPoint(args.GetPosition(this));
            selectionEnd=Math.Clamp(hit.FirstCharacterIndex+hit.TrailingLength,0,Text.Length);args.Handled=true;InvalidateVisual();
        };
        MouseUp+=args=>{if(!selecting||args.Button!=MouseButton.Left)return;selecting=false;if(FindVisualRoot() is Window window)window.ReleaseMouseCapture();args.Handled=true;};
        KeyDown+=args=>
        {
            if(args.Modifiers.HasFlag(ModifierKeys.Control)&&args.Key==Key.A){selectionAnchor=0;selectionEnd=Text.Length;args.Handled=true;InvalidateVisual();}
            else if(args.Modifiers.HasFlag(ModifierKeys.Control)&&args.Key==Key.C&&SelectedText is {} text){Copy?.Invoke(text);args.Handled=true;}
            else if(args.Key is Key.Left or Key.Right)
            {
                var hit=Layout(Bounds.Width).GetNextLogicalCaret(new(selectionEnd,0),args.Key==Key.Left?LogicalDirection.Backward:LogicalDirection.Forward,CaretMode.TextElement);
                selectionEnd=hit.FirstCharacterIndex+hit.TrailingLength;
                if(!args.Modifiers.HasFlag(ModifierKeys.Shift))selectionAnchor=selectionEnd;
                args.Handled=true;InvalidateVisual();
            }
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
    protected override void OnTextChanged() { if(selecting&&IsMouseCaptured&&FindVisualRoot() is Window window)window.ReleaseMouseCapture(); selectionAnchor=selectionEnd=0;selecting=false;ClearMusic(); base.OnTextChanged(); }
    private ITextLayout Layout(double width)
    {
        EnsureEmoji();
        var dpi = (uint)Math.Round(96 * (FindVisualRoot() is Window window ? window.DpiScale : 1));
        var style = new TextRunStyle(FontFamily, FontSize, FontWeight,FontStyle == FontStyle.Italic);
        if (layout != null && layoutWidth.Equals(width) && layoutDpi == dpi && layoutStyle == style) return layout;
        layoutWidth = width; layoutDpi = dpi; layoutStyle = style;
        foreach (var link in links) { link.Inline.MaximumWidth = width; link.Inline.ClearBounds(); }
        return layout = Application.Current.GraphicsFactory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = Text.AsMemory(), Dpi = dpi, DefaultStyle = style,
            Inlines = links.Select(link => new InlineRun(link.Start, link.Inline.Link.Url.Length, link.Inline, true))
                .Concat(emoji.Where(span => !links.Any(link => span.Start < link.Start + link.Inline.Link.Url.Length && span.Start + span.Length > link.Start))
                    .Select(span => new InlineRun(span.Start, span.Length, span.Inline, true))).OrderBy(span => span.Position).ToArray(),
            Paragraph = new() { MaxWidth = width, Wrapping = TextWrapping.Wrap, LineSpacing = LineSpacing,
                Alignment = TextAlignment,LineBoxTrim = LineBoxTrim,Culture = System.Globalization.CultureInfo.CurrentUICulture }
        });
    }
    protected override Size MeasureContent(Size availableSize)
    {
        if (links.Count == 0 && EmojiCount == 0) return base.MeasureContent(availableSize);
        var measured = Layout(availableSize.Width).MeasuredSize;
        return new(Math.Min(availableSize.Width, measured.Width + 1), measured.Height);
    }
    protected override void OnRender(IGraphicsContext context)
    {
        if(selectionAnchor!=selectionEnd)
        {
            var rectangles=new List<Rect>();
            Layout(Bounds.Width).GetRangeBounds(Math.Min(selectionAnchor,selectionEnd),Math.Abs(selectionEnd-selectionAnchor),rectangles);
            foreach(var rect in rectangles)context.FillRectangle(new Rect(Bounds.X+rect.X,Bounds.Y+rect.Y,rect.Width,rect.Height),Color.FromArgb(100,100,175,230));
        }
        if (links.Count == 0 && EmojiCount == 0) { base.OnRender(context); return; }
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.Text.Draw(Layout(Bounds.Width), new(Bounds.X, Bounds.Y), new TextDrawOptions(Foreground, Owner: this));
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
