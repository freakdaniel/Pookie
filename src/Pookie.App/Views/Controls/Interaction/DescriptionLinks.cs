using System.Text.RegularExpressions;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Text;

namespace Pookie.App;

// Native text selection/copy remains owned by the read-only editor. Links only
// decorate ranges and activate on release, after ruling out a selection drag.
internal sealed class DescriptionLinks : ITextClassifier, ITextLineTransformer, IDisposable
{
    internal sealed record Link(int Start, int Length, Uri Uri);
    private static readonly Regex urls = new(
        """(?<![\w@])(?:https?://|www\.|(?:[\p{L}\p{N}][\p{L}\p{N}-]*\.)+[\p{L}]{2,63}/)[^\s<>"'«»“”]+""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex telegram = new(
        @"(?<![\w@])(?:tgk?|тгк?)(?:[^\S\r\n]*:[^\S\r\n]*|[^\S\r\n]+)@?(?<username>[a-z][a-z0-9_]{4,31})(?![\w@/-]|\.[\p{L}\p{N}_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private readonly MultiLineTextBox editor;
    internal Action<Uri> OpenLink { get; set; }
    private Link[] links = [];
    private readonly Dictionary<Link, DescriptionLinkIcon> icons = [];
    private readonly Dictionary<Link, double> emphasis = [], from = [];
    private readonly AnimationClock motion = new(TimeSpan.FromMilliseconds(180), Easing.CubicBezier(.2, 0, 0, 1));
    private CancellationTokenSource? loading;
    private long documentVersion = -1;
    private bool IsCurrent => editor.Document.Version == documentVersion;
    internal Func<Uri, CancellationToken, Task<ImageSource?>>? LoadIcon { get; set; }
    internal IReadOnlyCollection<DescriptionLinkIcon> Icons => icons.Values;
    internal Color ColorFor(Link link)
    {
        var amount = emphasis.GetValueOrDefault(link);
        return Color.FromRgb((byte)Math.Round(166 + 52 * amount), (byte)Math.Round(195 + 36 * amount), (byte)Math.Round(246 + 9 * amount));
    }
    private Link? hovered, pressed;
    private Point down;
    private bool dragged;
    internal IReadOnlyList<Link> Links => links;
    internal Uri? HoveredUri => hovered?.Uri;

    internal DescriptionLinks(MultiLineTextBox editor, Action<Uri> open)
    {
        this.editor = editor; OpenLink = open;
        editor.Extensions.Classifiers.Add(this); editor.Extensions.Transformers.Add(this);
        editor.DocumentChanged += Changed;
        editor.MouseMove += Move; editor.MouseLeave += Leave;
        editor.MouseDown += Down; editor.MouseUp += Up;
        motion.TickCallback = progress =>
        {
            if (!IsCurrent) return;
            foreach (var link in from.Keys)
            {
                var target = link == hovered ? 1d : 0;
                emphasis[link] = from[link] + (target - from[link]) * progress;
                editor.InvalidateTextRange(link.Start, link.Length);
            }
        };
        Changed(editor);
    }

    private void Changed(ITextViewHost host)
    {
        loading?.Cancel(); loading?.Dispose(); loading = new();
        motion.Stop(); emphasis.Clear(); from.Clear();
        foreach (var icon in icons.Values) icon.Dispose();
        icons.Clear();
        var text = editor.Text;
        var result = new List<Link>();
        foreach (Match match in urls.Matches(text))
        {
            var value = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            foreach (var (opening, closing) in new[] { ('(', ')'), ('[', ']'), ('{', '}') })
                while (value.EndsWith(closing) && value.Count(c => c == closing) > value.Count(c => c == opening)) value = value[..^1];
            var absolute = value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            if (Uri.TryCreate(absolute ? value : "https://" + value,
                UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0)
                result.Add(new(match.Index, value.Length, uri));
        }
        foreach (Match match in telegram.Matches(text))
        {
            // An explicit URL owns its range, even if its path contains a TG label.
            if (result.Any(link => match.Index < link.Start + link.Length && match.Index + match.Length > link.Start)) continue;
            result.Add(new(match.Index, match.Length, new Uri("https://t.me/" + match.Groups["username"].Value)));
        }
        links = result.OrderBy(link => link.Start).ToArray(); hovered = pressed = null; dragged = false;
        foreach (var link in links) icons[link] = new(editor, System.Globalization.StringInfo.GetNextTextElement(text, link.Start), () => ColorFor(link));
        documentVersion = editor.Document.Version;
        editor.Cursor = CursorType.IBeam; editor.InvalidateTextView(); editor.InvalidateMeasure();
        if (LoadIcon != null) _ = LoadIconsAsync(loading.Token);
    }

    private async Task LoadIconsAsync(CancellationToken token)
    {
        var groups = links.GroupBy(link => link.Uri.GetLeftPart(UriPartial.Authority)).Take(32).ToArray();
        // Limit concurrent site requests; unavailable favicons retain the inline fallback.
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(groups.Select(async group =>
        {
            try
            {
                await gate.WaitAsync(token);
                try
                {
                    var source = await LoadIcon!(group.First().Uri, token);
                    if (token.IsCancellationRequested || source == null) return;
                    foreach (var link in group)
                        if (icons.TryGetValue(link, out var icon)) icon.SetSource(source);
                    editor.InvalidateVisual();
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or NotSupportedException) { }
        }));
    }

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        if (!IsCurrent) return;
        var start = context.LogicalLine.Offset; var end = start + context.LogicalLine.Length;
        foreach (var link in links)
        {
            var first = Math.Max(start, link.Start); var last = Math.Min(end, link.Start + link.Length);
            if (last <= first) continue;
            output.Add(new(new TextRange(first - start, last - first),
                Foreground: ColorFor(link)));
        }
    }

    public void Transform(in TextLineTransformContext context, IList<GeometryStyleRun> geometryRuns, IList<InlineRun> inlines)
    {
        // The editor can lay out a changed document before DocumentChanged is
        // delivered. Old inline ranges may split its new Unicode text elements.
        if (!IsCurrent) return;
        var start = context.LogicalLine.Offset; var end = start + context.LogicalLine.Length;
        foreach (var link in links)
        {
            if (link.Start >= start && link.Start < end && icons.TryGetValue(link, out var icon))
                inlines.Add(new(link.Start - start, icon.CharacterLength, icon));
        }
    }

    internal Rect[] BoundsFor(Link link)
    {
        if (!IsCurrent) return [];
        var result = new List<Rect>();
        foreach (var line in editor.VisibleTextLines)
        {
            var first = Math.Max(line.LogicalLine.Offset, link.Start);
            var last = Math.Min(line.LogicalLine.Offset + line.LogicalLine.Length, link.Start + link.Length);
            if (last <= first) continue;
            var bounds = new List<Rect>(); line.GetRangeBounds(new(first - line.LogicalLine.Offset, last - first), bounds);
            foreach (var rect in bounds) result.Add(new(
                editor.TextViewportBounds.X + line.DocumentX + rect.X - editor.ScrollOffset.X,
                editor.TextViewportBounds.Y + line.DocumentY + rect.Y - editor.ScrollOffset.Y, rect.Width, rect.Height));
        }
        return result.ToArray();
    }

    private Link? Hit(Point point) => editor.TextViewportBounds.Contains(point)
        ? links.FirstOrDefault(link => BoundsFor(link).Any(rect => rect.Contains(point))) : null;
    private Point WindowPoint(MouseEventArgs args)
    { var local = args.GetPosition(editor); return new(editor.Bounds.X + local.X, editor.Bounds.Y + local.Y); }
    private void Hover(Link? link)
    {
        if (hovered == link) return;
        motion.Stop(); hovered = link; from.Clear();
        foreach (var value in links)
            if (value == link || emphasis.GetValueOrDefault(value) > 0) from[value] = emphasis.GetValueOrDefault(value);
        if (from.Count > 0) motion.Start();
    }
    private void Move(MouseEventArgs args)
    {
        var point = WindowPoint(args);
        if (pressed != null && (Math.Abs(point.X - down.X) > 4 || Math.Abs(point.Y - down.Y) > 4)) dragged = true;
        var hit = args.LeftButton ? null : Hit(point);
        editor.Cursor = hit != null ? CursorType.Hand : CursorType.IBeam; Hover(hit);
    }
    private void Leave() { Hover(null); editor.Cursor = CursorType.IBeam; }
    private void Down(MouseEventArgs args)
    {
        pressed = null;
        if (args.Button != MouseButton.Left || args.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        down = WindowPoint(args); pressed = Hit(down); dragged = false;
    }
    private void Up(MouseEventArgs args)
    {
        var link = pressed; pressed = null;
        if (args.Button != MouseButton.Left || link == null || dragged || editor.SelectionLength != 0 ||
            args.Modifiers.HasFlag(ModifierKeys.Shift) || Hit(WindowPoint(args)) != link) return;
        // A link click is navigation, so do not leave the editor's caret focused
        // when the browser opens or when the user returns to this window.
        if (editor.FindVisualRoot() is Window window && window.FocusManager.FocusedElement == editor)
            window.FocusManager.ClearFocus();
        OpenLink(link.Uri);
    }

    public void Dispose()
    {
        loading?.Cancel(); loading?.Dispose(); loading = null; motion.Stop();
        foreach (var icon in icons.Values) icon.Dispose(); icons.Clear();
        editor.DocumentChanged -= Changed; editor.MouseMove -= Move; editor.MouseLeave -= Leave;
        editor.MouseDown -= Down; editor.MouseUp -= Up;
        editor.Extensions.Classifiers.Remove(this); editor.Extensions.Transformers.Remove(this);
    }
}
