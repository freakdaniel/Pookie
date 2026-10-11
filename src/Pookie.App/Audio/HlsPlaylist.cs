using System.Globalization;

namespace Pookie.Audio;

internal sealed record HlsSegment(Uri Uri, double Start, double Duration, long? Offset, long? Length);

internal sealed record HlsPlaylist(HlsSegment[] Segments, double Duration)
{
    public static HlsPlaylist Parse(string text, Uri origin, Func<Uri, bool> allowed)
    {
        if (!text.TrimStart('\uFEFF').StartsWith("#EXTM3U", StringComparison.Ordinal))
            throw new InvalidOperationException("SoundCloud вернул некорректный HLS-поток.");
        var segments = new List<HlsSegment>();
        double start = 0, duration = 0;
        long? offset = null, length = null;
        long nextOffset = 0;
        Uri? previousUri = null;
        var implicitOffset = false;
        var complete = false;
        foreach (var line in text.Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith("#EXT-X-KEY:") && !line[11..].Split(',').Contains("METHOD=NONE"))
                throw new InvalidOperationException("Зашифрованные HLS-потоки не поддерживаются.");
            if (line.StartsWith("#EXT-X-STREAM-INF:") || line.StartsWith("#EXT-X-MAP:"))
                throw new InvalidOperationException("Этот вариант HLS пока не поддерживается.");
            if (line.StartsWith("#EXTINF:"))
            {
                if (!double.TryParse(line[8..].Split(',')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
                    || !double.IsFinite(duration) || duration <= 0)
                    throw new InvalidOperationException("Некорректная длительность сегмента HLS.");
            }
            else if (line.StartsWith("#EXT-X-BYTERANGE:"))
            {
                var range = line[17..].Split('@');
                if (range.Length > 2 || !long.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0)
                    throw new InvalidOperationException("Некорректный диапазон HLS.");
                length = size;
                implicitOffset = range.Length == 1;
                if (implicitOffset) offset = nextOffset;
                else if (long.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out var position)) offset = position;
                else throw new InvalidOperationException("Некорректный диапазон HLS.");
            }
            else if (line == "#EXT-X-ENDLIST") complete = true;
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                if (duration <= 0 || !Uri.TryCreate(origin, line, out var uri) || !allowed(uri)
                    || (implicitOffset && previousUri != uri))
                    throw new InvalidOperationException("Некорректный адрес сегмента HLS.");
                segments.Add(new(uri, start, duration, offset, length));
                start += duration;
                if (length != null) nextOffset = checked(offset!.Value + length.Value);
                else nextOffset = 0;
                previousUri = length != null ? uri : null;
                duration = 0; offset = null; length = null; implicitOffset = false;
                if (segments.Count > 10000) throw new InvalidOperationException("Слишком большой плейлист HLS.");
            }
        }
        if (!complete || segments.Count == 0) throw new InvalidOperationException("Ожидался полный HLS-трек с конечной длительностью.");
        return new(segments.ToArray(), start);
    }
}
