using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Text;
using Pookie.App.Messages;
using Pookie.App.Storage;

namespace Pookie.App;

// One text-layout transform covers labels, styled runs and native edit controls.
// The document and its UTF-16 offsets are never rewritten.
internal static class ApplicationEmojiText
{
    private static readonly ResourceCache<(string Value, double Size), Inline> objects = new(512, new CacheBudget(128 * 1024), _ => 128);
    private readonly record struct TextKey(string Source, int Start, int Length)
    {
        public bool Equals(TextKey other) => ReferenceEquals(Source, other.Source) && Start == other.Start && Length == other.Length;
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Source), Start, Length);
    }
    private sealed record Scan(IReadOnlyList<(int Start, int Length, string Value)> Matches, long Bytes);
    private static readonly ResourceCache<TextKey, Scan> scans = new(512, new CacheBudget(2 * 1024 * 1024), value => value.Bytes);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookieTextLayoutTransform")]
    private static extern ref Func<TextLayoutRequest, TextLayoutRequest>? Transform(TextLayoutRequest? declaringType);
    internal static void Install() => Transform(null) = Project;

    internal static TextLayoutRequest Project(TextLayoutRequest request)
    {
        IReadOnlyList<(int Start, int Length, string Value)> matches;
        if (MemoryMarshal.TryGetString(request.Text, out var source, out var start, out var length) && source != null)
        {
            var key = new TextKey(source, start, length);
            if (!scans.TryGetValue(key, out var scan))
            {
                matches = ChatEmojiCatalog.Spans(request.Text.Span);
                scans[key] = scan = new(matches, source.Length * 2L + matches.Count * 48 + 64);
            }
            matches = scan.Matches;
        }
        else matches = ChatEmojiCatalog.Spans(request.Text.Span);
        if (matches.Count == 0) return request;
        List<InlineRun>? additions = null;
        foreach (var match in matches)
        {
            if (request.Inlines.Any(existing => match.Start < existing.Position + existing.Length && match.Start + match.Length > existing.Position)) continue;
            var size = request.DefaultStyle.FontSize;
            foreach (var run in request.Runs) if (match.Start >= run.Start && match.Start < run.End) size = run.Style.FontSize;
            var key = (match.Value, size);
            if (!objects.TryGetValue(key, out var inline)) objects[key] = inline = new(match.Value, size);
            (additions ??= []).Add(new(match.Start, match.Length, inline, true));
        }
        return additions == null ? request : request with { Inlines = request.Inlines.Concat(additions).OrderBy(i => i.Position).ToArray() };
    }

    private sealed class Inline(string value, double fontSize) : IInlineTextObject
    {
        private double Size => fontSize * 1.3;
        public InlineMetrics Measure() => new(Size, Size, Size * .82);
        public void Draw(ITextRenderContext context, Point origin) =>
            ChatEmojiArtwork.Draw(context.Graphics, value, new Rect(origin.X, origin.Y, Size, Size));
    }
}
