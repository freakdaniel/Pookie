using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Pookie.Recommendations;

public enum WaveMode { Balance, Favorites, Discoveries }
// Only public track metadata is persisted; stream authorization and session tokens never enter the profile.
public sealed record WaveTrack(long Id, string Title, string Artist, long Uploader, string Genre,
    double Duration, string? Artwork, string? Url, string? Isrc, string? UploaderName = null)
{
    [JsonIgnore] public bool Playable { get; init; } = true;
    [JsonIgnore] public double DurationSeconds => Duration / 1000;
}
public sealed record WaveTrackTaste(WaveTrack Track, bool Familiar = false, bool Liked = false, bool Blocked = false,
    int GoodPlays = 0, int EarlySkips = 0, double ListenedSeconds = 0, DateTimeOffset? LastPlayed = null, int Plays = 0);
public sealed record WaveProfile(WaveMode Mode, WaveTrackTaste[] Tracks);

public sealed class WaveTaste
{
    private readonly Dictionary<long, WaveTrackTaste> tracks = [];
    private readonly List<(WaveTrack Track, double Weight)> session = [];
    private readonly Dictionary<long, Dictionary<long, int>> related = [];
    public WaveMode Mode { get; set; }
    public bool Dirty { get; private set; }
    public IEnumerable<WaveTrack> KnownTracks => tracks.Values.Select(item => item.Track);
    public WaveTaste(WaveProfile? profile = null)
    {
        Mode = profile != null && Enum.IsDefined(profile.Mode) ? profile.Mode : WaveMode.Balance;
        foreach (var item in profile?.Tracks ?? [])
            if (item?.Track is { Id: > 0 } track && !string.IsNullOrWhiteSpace(track.Title))
                tracks[track.Id] = item with { Track = track with { Artist = track.Artist ?? "", Genre = track.Genre ?? "",
                    Duration = double.IsFinite(track.Duration) ? Math.Max(0, track.Duration) : 0 },
                    Plays = Math.Clamp(item.Plays, 0, 1000000), GoodPlays = Math.Clamp(item.GoodPlays, 0, 10000), EarlySkips = Math.Clamp(item.EarlySkips, 0, 10000),
                    ListenedSeconds = double.IsFinite(item.ListenedSeconds) ? Math.Clamp(item.ListenedSeconds, 0, 1e9) : 0 };
    }
    public WaveProfile Snapshot()
    {
        var explicitPreferences = tracks.Values.Where(item => item.Blocked || item.Liked).ToArray();
        return new(Mode, explicitPreferences.Concat(tracks.Values.Where(item => !item.Blocked && !item.Liked)
            .OrderByDescending(item => item.LastPlayed).Take(Math.Max(0, 1500 - explicitPreferences.Length))).ToArray());
    }
    public void Saved() => Dirty = false;
    public void SyncLikes(IReadOnlySet<long> liked)
    {
        foreach (var (id, item) in tracks.ToArray())
            if (item.Liked != liked.Contains(id)) { tracks[id] = item with { Liked = liked.Contains(id) }; Dirty = true; }
    }
    public void SetMode(WaveMode mode) { Mode = mode; Dirty = true; }
    public void Relate(WaveTrack seed, IEnumerable<WaveTrack> candidates)
    {
        Import(seed, false);
        var rank = 0;
        foreach (var track in candidates.Where(IsPlayable))
        {
            if (!related.TryGetValue(track.Id, out var sources)) related[track.Id] = sources = [];
            sources[seed.Id] = rank++;
            if (sources.Count > 6) sources.Remove(sources.Keys.First());
        }
        if (related.Count > 1200)
            foreach (var id in related.Keys.Take(related.Count - 1000).ToArray()) related.Remove(id);
    }
    public void Import(WaveTrack track, bool familiar, bool? liked = null)
    {
        if (!IsPlayable(track)) return;
        var previous = tracks.GetValueOrDefault(track.Id) ?? new(track);
        var next = previous with { Track = track, Familiar = previous.Familiar || familiar || liked == true,
            Liked = liked ?? previous.Liked };
        if (!tracks.ContainsKey(track.Id) || next != previous) { tracks[track.Id] = next; Dirty = true; }
        if (tracks.Count > 2000)
            foreach (var id in tracks.Values.Where(item => !item.Liked && !item.Blocked).OrderBy(item => item.LastPlayed)
                .Take(tracks.Count - 1800).Select(item => item.Track.Id).ToArray()) tracks.Remove(id);
    }
    public void Block(WaveTrack track)
    {
        Import(track, true);
        if (tracks.TryGetValue(track.Id, out var item)) tracks[track.Id] = item with { Blocked = true, Liked = false };
        AddSession(track, -1); Dirty = true;
    }
    public void ResetBlocks()
    {
        foreach (var (id, item) in tracks.ToArray()) if (item.Blocked) tracks[id] = item with { Blocked = false };
        session.Clear(); Dirty = true;
    }
    public bool IsBlocked(WaveTrack track) => tracks.Values.Any(item => item.Blocked && WorkKey(item.Track) == WorkKey(track));
    public WaveTrack[] Unblock(WaveTrack track)
    {
        var matching = tracks.Values.Where(item => item.Blocked && WorkKey(item.Track) == WorkKey(track)).ToArray();
        foreach (var item in matching) tracks[item.Track.Id] = item with { Blocked = false };
        if (matching.Length > 0) Dirty = true;
        return matching.Select(item => item.Track).ToArray();
    }
    public void Record(WaveTrack track, double seconds, bool earlySkip, DateTimeOffset now, bool rejected = false)
    {
        if (!IsPlayable(track) || !double.IsFinite(seconds)) return;
        Import(track, seconds >= 5);
        var item = tracks[track.Id];
        var good = !rejected && seconds >= Math.Max(5, Math.Min(60, track.DurationSeconds * .6));
        tracks[track.Id] = item with { Plays = item.Plays + 1, GoodPlays = item.GoodPlays + (good ? 1 : 0),
            EarlySkips = item.EarlySkips + (earlySkip ? 1 : 0), ListenedSeconds = item.ListenedSeconds + Math.Max(0, seconds),
            LastPlayed = seconds >= 5 ? now : item.LastPlayed };
        if (good || earlySkip) AddSession(item.Track, good ? 1 : -.25);
        Dirty = true;
    }
    private void AddSession(WaveTrack track, double weight)
    { session.Add((track, weight)); if (session.Count > 8) session.RemoveAt(0); }
    private bool Favorite(WaveTrack track) => tracks.TryGetValue(track.Id, out var item) && (item.Liked || item.GoodPlays > 0);
    public WaveTrack[] Seeds(IEnumerable<WaveTrack> candidates, int count = 3, Func<WaveTrack, double>? learnedScore = null,
        Func<WaveTrack, double?>? moodMatch = null, double minimumMoodMatch = .4)
    {
        var score = learnedScore ?? Scorer();
        var blocked = tracks.Values.Where(item => item.Blocked).Select(item => WorkKey(item.Track)).ToHashSet();
        return candidates.Where(IsPlayable).Where(track => tracks.GetValueOrDefault(track.Id)?.Blocked != true)
            .Where(track => !blocked.Contains(WorkKey(track))).Where(track => moodMatch?.Invoke(track) is not { } match || match >= minimumMoodMatch)
            .OrderByDescending(track => moodMatch?.Invoke(track) is >= .6).ThenByDescending(score).DistinctBy(ArtistKey).Take(count).ToArray();
    }
    private sealed record Candidate(WaveTrack Track, string Work, string Artist, bool Familiar, double Score, double? Mood);

    public WaveTrack[] Rank(IEnumerable<WaveTrack> candidates, IEnumerable<WaveTrack> recent,
        int count, DateTimeOffset now, Random? random = null, IEnumerable<WaveTrack>? reserved = null,
        Func<WaveTrack, double>? learnedScore = null, Func<WaveTrack, double?>? moodMatch = null, double minimumMoodMatch = .4)
    {
        random ??= Random.Shared;
        var played = recent.ToArray();
        var blockedWorks = tracks.Values.Where(item => item.Blocked).Select(item => WorkKey(item.Track)).ToHashSet();
        var reservedWorks = (reserved ?? []).Select(WorkKey).ToHashSet();
        var familiarWorks = tracks.Values.Where(item => item.Familiar || item.Liked || item.GoodPlays > 0)
            .Select(item => WorkKey(item.Track)).ToHashSet();
        var eligible = candidates.Where(IsPlayable).DistinctBy(track => track.Id)
            .Where(track => tracks.GetValueOrDefault(track.Id)?.Blocked != true)
            .Where(track => !blockedWorks.Contains(WorkKey(track)))
            .Where(track => !reservedWorks.Contains(WorkKey(track)))
            .Where(track => Mode != WaveMode.Discoveries || !familiarWorks.Contains(WorkKey(track)))
            .Where(track => Mode != WaveMode.Favorites || Favorite(track)).ToArray();
        eligible = eligible.Where(track => moodMatch?.Invoke(track) is not { } match || match >= minimumMoodMatch).ToArray();
        // Relax only the repetition cooldown when a small library cannot fill a stream.
        // Discovery and explicit dislike constraints remain strict in every pass.
        var excluded = played.TakeLast(24).Select(WorkKey).ToHashSet();
        var pool = eligible.Where(track => !excluded.Contains(WorkKey(track))).ToArray();
        if (pool.Length == 0 && Mode != WaveMode.Discoveries)
        {
            var last = reserved == null ? played.LastOrDefault() : null;
            pool = eligible.Where(track => last == null || WorkKey(track) != WorkKey(last)).ToArray();
        }
        var preference = learnedScore ?? Scorer();
        var exploration = learnedScore == null ? .5 : .025;
        var cooldown = learnedScore == null ? 1 : .05;
        var diversity = learnedScore == null ? 3 : .08;
        var weighted = pool.Select(track => new Candidate(track, WorkKey(track), ArtistKey(track), familiarWorks.Contains(WorkKey(track)),
            preference(track) + random.NextDouble() * exploration - RecencyPenalty(track, now) * cooldown, moodMatch?.Invoke(track))).ToArray();
        var result = new List<WaveTrack>();
        var unknownSlots = Math.Max(1, (count + 4) / 5);
        var unknownSelected = 0;
        var works = new HashSet<string>();
        var artists = played.TakeLast(3).Select(ArtistKey).ToList();
        while (result.Count < count)
        {
            var available = weighted.Where(candidate => !works.Contains(candidate.Work)).ToArray();
            if (available.Length == 0) break;
            if (moodMatch != null)
            {
                var matching = available.Where(candidate => candidate.Mood is >= .6).ToArray();
                var unknown = unknownSelected < unknownSlots ? available.Where(candidate => candidate.Mood == null).ToArray() : [];
                var uncertain = available.Where(candidate => candidate.Mood is >= .4 and < .6).ToArray();
                // Mood takes precedence over the familiar/new split. Leave one exploration slot per five songs.
                available = matching.Length > 0 ? result.Count % 5 == 4 && unknown.Length > 0 ? unknown : matching
                    : unknown.Length > 0 ? unknown : uncertain;
                if (available.Length == 0) break;
            }
            var freshSlot = result.Count % 3 != 0;
            var lane = Mode == WaveMode.Balance ? available.Where(candidate => candidate.Familiar != freshSlot).ToArray() : available;
            if (lane.Length == 0) lane = available;
            var next = lane.MaxBy(candidate => candidate.Score - artists.Count(artist => artist == candidate.Artist) * diversity)!;
            if (moodMatch != null && next.Mood == null) unknownSelected++;
            works.Add(next.Work); result.Add(next.Track); artists.Add(next.Artist);
            if (artists.Count > 3) artists.RemoveAt(0);
        }
        return result.ToArray();
    }
    private double RecencyPenalty(WaveTrack track, DateTimeOffset now) => tracks.GetValueOrDefault(track.Id)?.LastPlayed is { } last
        ? Math.Max(0, 2 - (now - last).TotalHours / 12) : 0;
    private Func<WaveTrack, double> Scorer()
    {
        var positives = tracks.Values.Where(entry => entry.Liked || entry.GoodPlays > 0).ToArray();
        var artists = positives.GroupBy(entry => ArtistKey(entry.Track)).ToDictionary(group => group.Key, group => group.Count());
        var genres = positives.GroupBy(entry => Clean(entry.Track.Genre)).ToDictionary(group => group.Key, group => group.Count());
        var sessionArtists = session.GroupBy(entry => ArtistKey(entry.Track)).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Weight));
        var sessionGenres = session.GroupBy(entry => Clean(entry.Track.Genre)).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Weight));
        double BaseScore(WaveTrack track)
        {
            var item = tracks.GetValueOrDefault(track.Id);
            var artist = ArtistKey(track); var genre = Clean(track.Genre ?? "");
            var score = (item?.Liked == true ? 3 : 0) + Math.Min(2, Math.Log2(1 + (item?.GoodPlays ?? 0)))
                - Math.Min(1, (item?.EarlySkips ?? 0) * .15);
            score += Math.Min(2, artists.GetValueOrDefault(artist) * .3) + sessionArtists.GetValueOrDefault(artist) * .8;
            if (genre.Length > 0) score += Math.Min(1.5, genres.GetValueOrDefault(genre) * .1) + sessionGenres.GetValueOrDefault(genre) * .4;
            return score;
        }
        var seeds = tracks.ToDictionary(pair => pair.Key, pair => BaseScore(pair.Value.Track));
        return track => BaseScore(track) + (related.TryGetValue(track.Id, out var sources)
            ? sources.Max(source => (1 + Math.Max(0, seeds.GetValueOrDefault(source.Key))) / (1 + source.Value * .05)) : 0);
    }
    public static string ArtistKey(WaveTrack track) => Clean(track.Artist);
    public static string WorkKey(WaveTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.Isrc)) return "isrc:" + Clean(track.Isrc);
        var title = Clean(track.Title);
        title = Regex.Replace(title, @"\([^)]*(?:sp(?:ee|e)d|reverb|slowed)[^)]*\)", "");
        title = Regex.Replace(title, @"\s+(?:sped\s*up|speed\s*up|slowed)(?:\b.*)?$", "");
        return ArtistKey(track) + ":" + title.Trim();
    }
    public static bool IsPlayable(WaveTrack track) => track.Playable && track.Id > 0 && !string.IsNullOrWhiteSpace(track.Title);
    private static string Clean(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"\s+", " ").Trim();
}
