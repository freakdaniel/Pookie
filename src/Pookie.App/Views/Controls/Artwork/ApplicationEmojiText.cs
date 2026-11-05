using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Text;
using Pookie.App.Messages;
using Pookie.App.Storage;

namespace Pookie.App;

// Shared presentation keeps the document and its UTF-16 offsets unchanged.
// Multi-line editors use public generators/transformers; the compatibility hook
// covers standard labels and single-line editors without that extension API.
internal static class ApplicationEmojiText
{
    private static readonly ResourceCache<(string Value, double Size), Inline> objects = new(512, new CacheBudget(128 * 1024), _ => 128);
    private readonly record struct TextKey(string Source, int Start, int Length)
    {
        public bool Equals(TextKey other) => ReferenceEquals(Source, other.Source) && Start == other.Start && Length == other.Length;
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Source), Start, Length);
    }
    private sealed record Scan(IReadOnlyList<(int Start, int Length, string Value)> Matches, IReadOnlyList<(int Start, int Length)> HiddenMarks, long Bytes);
    private static readonly ResourceCache<TextKey, Scan> scans = new(512, new CacheBudget(2 * 1024 * 1024), value => value.Bytes);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookieTextLayoutTransform")]
    private static extern ref Func<TextLayoutRequest, TextLayoutRequest>? Transform(TextLayoutRequest? declaringType);
    internal static void InstallCompatibility() => Transform(null) = Project;

    internal static void AddEditorCombiningInlines(ReadOnlyMemory<char> text, IList<InlineRun> inlines)
    {
        foreach (var run in CombiningText.HiddenRuns(text.Span))
        {
            var overlap = false;
            for (var index = 0; index < inlines.Count; index++)
            {
                var existing = inlines[index];
                if (run.Start < existing.Position + existing.Length && run.Start + run.Length > existing.Position)
                { overlap = true; break; }
            }
            if (!overlap) inlines.Add(new(run.Start, run.Length, HiddenMarks.Instance, true));
        }
    }

    internal static TextLayoutRequest Project(TextLayoutRequest request)
    {
        IReadOnlyList<(int Start, int Length, string Value)> matches;
        IReadOnlyList<(int Start, int Length)> hidden;
        if (MemoryMarshal.TryGetString(request.Text, out var source, out var start, out var length) && source != null)
        {
            var key = new TextKey(source, start, length);
            if (!scans.TryGetValue(key, out var scan))
            {
                matches = ChatEmojiCatalog.Spans(request.Text.Span);
                hidden = CombiningText.HiddenRuns(request.Text.Span);
                scans[key] = scan = new(matches, hidden, source.Length * 2L + matches.Count * 48 + hidden.Count * 16 + 64);
            }
            matches = scan.Matches; hidden = scan.HiddenMarks;
        }
        else { matches = ChatEmojiCatalog.Spans(request.Text.Span); hidden = CombiningText.HiddenRuns(request.Text.Span); }
        if (matches.Count == 0 && hidden.Count == 0) return request;
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
        foreach (var run in hidden)
        {
            if (request.Inlines.Any(existing => run.Start < existing.Position + existing.Length && run.Start + run.Length > existing.Position)) continue;
            (additions ??= []).Add(new(run.Start, run.Length, HiddenMarks.Instance, true));
        }
        return additions == null ? request : request with { Inlines = request.Inlines.Concat(additions).OrderBy(i => i.Position).ToArray() };
    }

    private sealed class HiddenMarks : IInlineTextObject
    {
        internal static readonly HiddenMarks Instance = new();
        public InlineMetrics Measure() => new(0, 0, 0);
        // These original document characters deliberately contribute no glyphs.
        public void Draw(ITextRenderContext context, Point origin) { }
    }

    private sealed class Inline(string value, double fontSize) : IInlineTextObject
    {
        private double Size => fontSize * 1.3;
        public InlineMetrics Measure() => new(Size, Size, Size * .82);
        public void Draw(ITextRenderContext context, Point origin) =>
            ChatEmojiArtwork.Draw(context.Graphics, value, new Rect(origin.X, origin.Y, Size, Size));
    }
}
