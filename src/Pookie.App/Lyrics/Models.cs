using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Pookie.Lyrics;

public enum LyricsStatus { Loading, Synced, Plain, Instrumental, NotFound, Unavailable }
public sealed record LyricsLine(double Start, string Text);
public sealed record LyricsDocument(string Source, long? ProviderId, string Title, string Artist,
    string PlainText, LyricsLine[] Lines, bool Instrumental = false)
{
    public LyricsStatus Status => Instrumental ? LyricsStatus.Instrumental : Lines.Length > 0 ? LyricsStatus.Synced : LyricsStatus.Plain;
    public int ActiveLine(double position, int delayMs = 0)
    {
        var target = position - delayMs / 1000d;
        var low = 0; var high = Lines.Length - 1; var found = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (Lines[middle].Start <= target) { found = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return found;
    }
}
public sealed record LyricsResult(LyricsStatus Status, LyricsDocument? Document = null);
public sealed record LyricsCandidate
{
    public long Id { get; init; }
    public string TrackName { get; init; } = "";
    public string ArtistName { get; init; } = "";
    public string? AlbumName { get; init; }
    public double Duration { get; init; }
    public bool Instrumental { get; init; }
    public string? PlainLyrics { get; init; }
    public string? SyncedLyrics { get; init; }
    public LyricsDocument ToDocument() => LrcParser.Parse(SyncedLyrics, PlainLyrics, "LRCLIB", Id, TrackName, ArtistName, Instrumental);
}
public sealed record LyricsIdentity(long TrackId, string Title, string Artist, double Duration,
    string? Album = null, bool TimingAvailable = true)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"v7-speed-effects\n{Title}\n{Artist}\n{Duration:R}\n{Album}\n{TimingAvailable}"))));
    public LyricsQuery[] Queries(bool stripParentheses = false)
    {
        var clean = LyricsMatcher.SearchTitle(Title);
        if (stripParentheses) clean = LyricsMatcher.WithoutParentheses(clean);
        var queries = new List<LyricsQuery> { new(clean, Artist, Album, Duration) };
        if (LyricsMatcher.SplitArtistTitle(clean) is { } separator)
            queries.Insert(0, new(separator.Title, separator.Artist, Album, Duration));
        return queries.Distinct().ToArray();
    }
}
public sealed record LyricsQuery(string Title, string Artist, string? Album, double Duration);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LyricsCandidate))]
[JsonSerializable(typeof(LyricsCandidate[]))]
[JsonSerializable(typeof(LyricsCacheRecord))]
internal partial class LyricsJson : JsonSerializerContext;

internal sealed record LyricsCacheRecord(string Fingerprint, DateTimeOffset At, LyricsResult Result, bool Complete = true);
