using System.Text;
using System.Text.RegularExpressions;
using Pookie.SoundCloud;

namespace Pookie.App.Playback;

// Metadata can identify likely reuploads, not prove that two recordings are identical.
internal sealed partial class TrackIdentity
{
    private readonly HashSet<long> ids = [];
    private readonly Dictionary<string, List<Recording>> recordings = new(StringComparer.Ordinal);
    private readonly record struct Recording(double Duration, string Artist, string Version);

    [GeneratedRegex(@"\b(?:sped[\s-]*up|speed(?:ed)?[\s-]*up|nightcore|ускоренн\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Faster();
    [GeneratedRegex(@"\b(?:slowed(?:[\s-]*down)?|daycore|замедленн\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Slower();
    [GeneratedRegex(@"\s*[-–—:]\s+|\s+[-–—:]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex ArtistSeparator();
    [GeneratedRegex(@"\s*(?:\(|\[)\s*(?:official(?:\s+(?:audio|video))?|audio|re[\s-]*upload|перезалив)\s*(?:\)|\])\s*$|\s+[-–—]\s*(?:official audio|re[\s-]*upload|перезалив)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UploadLabel();
    [GeneratedRegex(@"[^\p{L}\p{M}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separators();

    internal TrackIdentity(IEnumerable<SoundCloudTrack> tracks) { foreach (var track in tracks) Remember(track); }

    internal static bool Matches(SoundCloudTrack first, SoundCloudTrack second) =>
        new TrackIdentity([first]).Contains(second);

    internal bool Contains(SoundCloudTrack track)
    {
        if (ids.Contains(track.Id)) return true;
        var (title, recording) = Describe(track);
        return title.Length > 0 && recordings.TryGetValue(title, out var previous) && previous.Any(other =>
            recording.Duration > 0 && other.Duration > 0 && recording.Version == other.Version &&
            (recording.Artist.Length == 0 || other.Artist.Length == 0 || recording.Artist == other.Artist) &&
            Math.Abs(recording.Duration - other.Duration) <= Math.Clamp(Math.Min(recording.Duration, other.Duration) * .015, 800, 2500));
    }

    internal bool Add(SoundCloudTrack track)
    {
        if (Contains(track)) return false;
        Remember(track); return true;
    }

    private void Remember(SoundCloudTrack track)
    {
        if (track.Id > 0) ids.Add(track.Id);
        var (title, recording) = Describe(track);
        if (title.Length == 0) return;
        if (!recordings.TryGetValue(title, out var matches)) recordings[title] = matches = [];
        matches.Add(recording);
    }

    private static (string Title, Recording Recording) Describe(SoundCloudTrack track)
    {
        var title = track.Title.Normalize(NormalizationForm.FormKC).Trim();
        string version = Faster().IsMatch(title) ? "fast" : Slower().IsMatch(title) ? "slow" : "";
        string previous;
        do { previous = title; title = UploadLabel().Replace(title, ""); } while (title != previous);
        var artist = Clean(track.PublisherMetadata?.Artist ?? "");
        var split = ArtistSeparator().Match(title);
        if (split.Success)
        {
            var prefix = Clean(title[..split.Index]);
            // Uploader names need not match for a reupload. Keep explicit credits
            // as additional evidence, especially for short/common song titles.
            if (artist.Length == 0) artist = prefix;
            if (artist == prefix) title = title[(split.Index + split.Length)..];
        }
        return (Clean(title), new(track.FullDuration > 0 ? track.FullDuration : track.Duration, artist, version));
    }

    private static string Clean(string value) => Separators().Replace(value.ToLowerInvariant(), " ").Trim();
}
