using System.Globalization;

namespace Pookie.Audio;

internal sealed record ProtectedHlsPlaylist(Uri Initialization, byte[] InitData, HlsSegment[] Segments, double Duration)
{
    private const string Widevine = "urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed";
    public static ProtectedHlsPlaylist Parse(string text, Uri origin, Func<Uri, bool> allowed)
    {
        if (!text.TrimStart('\uFEFF').StartsWith("#EXTM3U", StringComparison.Ordinal)) throw Invalid();
        Uri? initialization = null;
        byte[]? initData = null;
        var segments = new List<HlsSegment>();
        double duration = 0, start = 0;
        var complete = false;
        foreach (var line in text.Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith("#EXT-X-STREAM-INF:") || line.StartsWith("#EXT-X-BYTERANGE:"))
                throw new DrmPlaybackException("Нужен отдельный конечный DRM HLS-поток без master-плейлиста и byte ranges.");
            if (line.StartsWith("#EXT-X-MAP:"))
            {
                var attributes = Attributes(line[11..]);
                if (attributes.ContainsKey("BYTERANGE") || !attributes.TryGetValue("URI", out var map) ||
                    !Uri.TryCreate(origin, map, out var uri) || !allowed(uri) || initialization != null && initialization != uri) throw Invalid();
                initialization = uri;
            }
            else if (line.StartsWith("#EXT-X-KEY:"))
            {
                var attributes = Attributes(line[11..]);
                if (!attributes.TryGetValue("KEYFORMAT", out var format)) throw Invalid();
                // A playlist may declare PlayReady alongside Widevine. Only the Widevine declaration is used.
                if (format != Widevine) continue;
                if (!attributes.TryGetValue("METHOD", out var method) || method is not ("SAMPLE-AES" or "SAMPLE-AES-CTR") ||
                    !attributes.TryGetValue("URI", out var data) || !data.StartsWith("data:", StringComparison.Ordinal) ||
                    data.Length > 90000) throw Invalid();
                var comma = data.IndexOf(',');
                if (comma < 0 || !data[..comma].EndsWith(";base64", StringComparison.Ordinal)) throw Invalid();
                byte[] parsed;
                try { parsed = Convert.FromBase64String(data[(comma + 1)..]); }
                catch (FormatException) { throw Invalid(); }
                if (parsed.Length is < 32 or > 65536 || initData != null && !initData.AsSpan().SequenceEqual(parsed)) throw Invalid();
                initData = parsed;
            }
            else if (line.StartsWith("#EXTINF:"))
            {
                if (!double.TryParse(line[8..].Split(',')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out duration) ||
                    !double.IsFinite(duration) || duration <= 0 || duration > 300) throw Invalid();
            }
            else if (line == "#EXT-X-ENDLIST") complete = true;
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                if (duration <= 0 || !Uri.TryCreate(origin, line, out var uri) || !allowed(uri) || segments.Count >= 10000) throw Invalid();
                segments.Add(new(uri, start, duration, null, null)); start += duration; duration = 0;
            }
        }
        if (!complete || initialization == null || initData == null || segments.Count == 0) throw Invalid();
        return new(initialization, initData, segments.ToArray(), start);
    }

    internal static Dictionary<string, string> Attributes(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position < line.Length)
        {
            var equal = line.IndexOf('=', position);
            if (equal <= position) throw Invalid();
            var name = line[position..equal].Trim(); position = equal + 1;
            string value;
            if (position < line.Length && line[position] == '"')
            {
                var end = line.IndexOf('"', ++position);
                if (end < 0) throw Invalid();
                value = line[position..end]; position = end + 1;
                if (position < line.Length && line[position] != ',') throw Invalid();
            }
            else
            {
                var end = line.IndexOf(',', position);
                if (end < 0) end = line.Length;
                value = line[position..end].Trim(); position = end;
            }
            if (!result.TryAdd(name, value)) throw Invalid();
            if (position < line.Length) position++;
        }
        return result;
    }
    private static DrmPlaybackException Invalid() => new("SoundCloud вернул неподдерживаемый или некорректный Widevine HLS-плейлист.");
}
