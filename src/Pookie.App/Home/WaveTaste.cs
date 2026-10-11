using System.Text;
using System.Text.RegularExpressions;
using Pookie.SoundCloud;

namespace Pookie.App.Home;

internal enum WaveMode { Balance, Favorites, Discoveries }
// Only public track metadata is persisted; stream authorization and session tokens never enter the profile.
internal sealed record WaveTrack(long Id, string Title, string Artist, long Uploader, string Genre,
    double Duration, string? Artwork, string? Url, string? Isrc, string? UploaderName = null)
{
    internal static WaveTrack From(SoundCloudTrack track) => new(track.Id, track.Title,
        track.PublisherMetadata?.Artist ?? track.Author, track.User?.Id ?? 0, track.Genre ?? "", track.Duration,
        track.ArtworkUrl, track.PermalinkUrl, track.PublisherMetadata?.Isrc, track.Author);
    internal SoundCloudTrack ToTrack() => new() { Id = Id, Title = Title, Duration = Duration,
        Genre = Genre, ArtworkUrl = Artwork, PermalinkUrl = Url, User = new() { Id = Uploader, Username = UploaderName ?? Artist },
        PublisherMetadata = new() { Artist = Artist, Isrc = Isrc } };
}
internal sealed record WaveTrackTaste(WaveTrack Track, bool Familiar = false, bool Liked = false, bool Blocked = false,
    int GoodPlays = 0, int EarlySkips = 0, double ListenedSeconds = 0, DateTimeOffset? LastPlayed = null, int Plays = 0);
internal sealed record WaveProfile(WaveMode Mode, WaveTrackTaste[] Tracks);

internal sealed class WaveTaste
{
    private readonly Dictionary<long, WaveTrackTaste> tracks = [];
    private readonly List<(WaveTrack Track, double Weight)> session = [];
    private readonly Dictionary<long, Dictionary<long, int>> related = [];
    internal WaveMode Mode { get; set; }
    internal bool Dirty { get; private set; }
    internal IEnumerable<SoundCloudTrack> KnownTracks => tracks.Values.Select(item => item.Track.ToTrack());
    internal WaveTaste(WaveProfile? profile = null)
    {
        Mode = profile != null && Enum.IsDefined(profile.Mode) ? profile.Mode : WaveMode.Balance;
        foreach (var item in profile?.Tracks ?? [])
            if (item?.Track is { Id: > 0 } track && !string.IsNullOrWhiteSpace(track.Title))
                tracks[track.Id] = item with { Track = track with { Artist = track.Artist ?? "", Genre = track.Genre ?? "",
                    Duration = double.IsFinite(track.Duration) ? Math.Max(0, track.Duration) : 0 },
                    Plays = Math.Clamp(item.Plays, 0, 1000000), GoodPlays = Math.Clamp(item.GoodPlays, 0, 10000), EarlySkips = Math.Clamp(item.EarlySkips, 0, 10000),
                    ListenedSeconds = double.IsFinite(item.ListenedSeconds) ? Math.Clamp(item.ListenedSeconds, 0, 1e9) : 0 };
    }
    internal WaveProfile Snapshot() => new(Mode, tracks.Values.OrderByDescending(item => item.Blocked || item.Liked)
        .ThenByDescending(item => item.LastPlayed).Take(1500).ToArray());
    internal void Saved() => Dirty = false;
    internal void SyncLikes(IReadOnlySet<long> liked)
    {
        foreach (var (id, item) in tracks.ToArray())
            if (item.Liked != liked.Contains(id)) { tracks[id] = item with { Liked = liked.Contains(id) }; Dirty = true; }
    }
    internal void SetMode(WaveMode mode) { Mode = mode; Dirty = true; }
    internal void Relate(SoundCloudTrack seed, IEnumerable<SoundCloudTrack> candidates)
    {
        Import(seed, false);
        var rank = 0;
        foreach (var track in candidates.Where(HomeRecommendations.Playable))
        {
            if (!related.TryGetValue(track.Id, out var sources)) related[track.Id] = sources = [];
            sources[seed.Id] = rank++;
            if (sources.Count > 6) sources.Remove(sources.Keys.First());
        }
        if (related.Count > 1200)
            foreach (var id in related.Keys.Take(related.Count - 1000).ToArray()) related.Remove(id);
    }
    internal void Import(SoundCloudTrack track, bool familiar, bool? liked = null)
    {
        if (!HomeRecommendations.Playable(track)) return;
        var previous = tracks.GetValueOrDefault(track.Id) ?? new(WaveTrack.From(track));
        var next = previous with { Track = WaveTrack.From(track), Familiar = previous.Familiar || familiar || liked == true,
            Liked = liked ?? previous.Liked };
        if (!tracks.ContainsKey(track.Id) || next != previous) { tracks[track.Id] = next; Dirty = true; }
        if (tracks.Count > 2000)
            foreach (var id in tracks.Values.Where(item => !item.Liked && !item.Blocked).OrderBy(item => item.LastPlayed)
                .Take(tracks.Count - 1800).Select(item => item.Track.Id).ToArray()) tracks.Remove(id);
    }
    internal void Block(SoundCloudTrack track)
    {
        Import(track, true);
        if (tracks.TryGetValue(track.Id, out var item)) tracks[track.Id] = item with { Blocked = true };
        AddSession(WaveTrack.From(track), -1); Dirty = true;
    }
    internal void ResetBlocks()
    {
        foreach (var (id, item) in tracks.ToArray()) if (item.Blocked) tracks[id] = item with { Blocked = false };
        session.Clear(); Dirty = true;
    }
    internal void Record(SoundCloudTrack track, double seconds, bool earlySkip, DateTimeOffset now)
    {
        if (!HomeRecommendations.Playable(track) || !double.IsFinite(seconds)) return;
        Import(track, seconds >= 5);
        var item = tracks[track.Id];
        var good = seconds >= Math.Max(5, Math.Min(60, track.DurationSeconds * .6));
        tracks[track.Id] = item with { Plays = item.Plays + 1, GoodPlays = item.GoodPlays + (good ? 1 : 0),
            EarlySkips = item.EarlySkips + (earlySkip ? 1 : 0), ListenedSeconds = item.ListenedSeconds + Math.Max(0, seconds),
            LastPlayed = seconds >= 5 ? now : item.LastPlayed };
        if (good || earlySkip) AddSession(item.Track, good ? 1 : -.25);
        Dirty = true;
    }
    private void AddSession(WaveTrack track, double weight)
    { session.Add((track, weight)); if (session.Count > 8) session.RemoveAt(0); }
    private bool Favorite(SoundCloudTrack track) => tracks.TryGetValue(track.Id, out var item) && (item.Liked || item.GoodPlays > 0);
    internal SoundCloudTrack[] Seeds(IEnumerable<SoundCloudTrack> candidates, int count = 3)
    {
        var score = Scorer();
        var blocked = tracks.Values.Where(item => item.Blocked).Select(item => WorkKey(item.Track.ToTrack())).ToHashSet();
        return candidates.Where(HomeRecommendations.Playable).Where(track => tracks.GetValueOrDefault(track.Id)?.Blocked != true)
            .Where(track => !blocked.Contains(WorkKey(track))).OrderByDescending(score).DistinctBy(ArtistKey).Take(count).ToArray();
    }
    private sealed record Candidate(SoundCloudTrack Track, string Work, string Artist, bool Familiar, double Score);

    internal SoundCloudTrack[] Rank(IEnumerable<SoundCloudTrack> candidates, IEnumerable<SoundCloudTrack> recent,
        int count, DateTimeOffset now, Random? random = null, IEnumerable<SoundCloudTrack>? reserved = null)
    {
        random ??= Random.Shared;
        var played = recent.ToArray();
        var blockedWorks = tracks.Values.Where(item => item.Blocked).Select(item => WorkKey(item.Track.ToTrack())).ToHashSet();
        var reservedWorks = (reserved ?? []).Select(WorkKey).ToHashSet();
        var familiarWorks = tracks.Values.Where(item => item.Familiar || item.Liked || item.GoodPlays > 0)
            .Select(item => WorkKey(item.Track.ToTrack())).ToHashSet();
        var eligible = candidates.Where(HomeRecommendations.Playable).DistinctBy(track => track.Id)
            .Where(track => tracks.GetValueOrDefault(track.Id)?.Blocked != true)
            .Where(track => !blockedWorks.Contains(WorkKey(track)))
            .Where(track => !reservedWorks.Contains(WorkKey(track)))
            .Where(track => Mode != WaveMode.Discoveries || !familiarWorks.Contains(WorkKey(track)))
            .Where(track => Mode != WaveMode.Favorites || Favorite(track)).ToArray();
        // Relax only the repetition cooldown when a small library cannot fill a stream.
        // Discovery and explicit dislike constraints remain strict in every pass.
        var excluded = played.TakeLast(24).Select(WorkKey).ToHashSet();
        var pool = eligible.Where(track => !excluded.Contains(WorkKey(track))).ToArray();
        if (pool.Length == 0 && Mode != WaveMode.Discoveries)
        {
            var last = reserved == null ? played.LastOrDefault() : null;
            pool = eligible.Where(track => last == null || WorkKey(track) != WorkKey(last)).ToArray();
        }
        var preference = Scorer();
        var weighted = pool.Select(track => new Candidate(track, WorkKey(track), ArtistKey(track), familiarWorks.Contains(WorkKey(track)),
            preference(track) + random.NextDouble() * .5 - RecencyPenalty(track, now))).ToArray();
        var result = new List<SoundCloudTrack>();
        var works = new HashSet<string>();
        var artists = played.TakeLast(3).Select(ArtistKey).ToList();
        while (result.Count < count)
        {
            var available = weighted.Where(candidate => !works.Contains(candidate.Work)).ToArray();
            if (available.Length == 0) break;
            var freshSlot = result.Count % 3 != 0;
            var lane = Mode == WaveMode.Balance ? available.Where(candidate => candidate.Familiar != freshSlot).ToArray() : available;
            if (lane.Length == 0) lane = available;
            var next = lane.MaxBy(candidate => candidate.Score - artists.Count(artist => artist == candidate.Artist) * 3)!;
            works.Add(next.Work); result.Add(next.Track); artists.Add(next.Artist);
            if (artists.Count > 3) artists.RemoveAt(0);
        }
        return result.ToArray();
    }
    private double RecencyPenalty(SoundCloudTrack track, DateTimeOffset now) => tracks.GetValueOrDefault(track.Id)?.LastPlayed is { } last
        ? Math.Max(0, 2 - (now - last).TotalHours / 12) : 0;
    private Func<SoundCloudTrack, double> Scorer()
    {
        var positives = tracks.Values.Where(entry => entry.Liked || entry.GoodPlays > 0).ToArray();
        var artists = positives.GroupBy(entry => ArtistKey(entry.Track.ToTrack())).ToDictionary(group => group.Key, group => group.Count());
        var genres = positives.GroupBy(entry => Clean(entry.Track.Genre)).ToDictionary(group => group.Key, group => group.Count());
        var sessionArtists = session.GroupBy(entry => ArtistKey(entry.Track.ToTrack())).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Weight));
        var sessionGenres = session.GroupBy(entry => Clean(entry.Track.Genre)).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Weight));
        double BaseScore(SoundCloudTrack track)
        {
            var item = tracks.GetValueOrDefault(track.Id);
            var artist = ArtistKey(track); var genre = Clean(track.Genre ?? "");
            var score = (item?.Liked == true ? 3 : 0) + Math.Min(2, Math.Log2(1 + (item?.GoodPlays ?? 0)))
                - Math.Min(1, (item?.EarlySkips ?? 0) * .15);
            score += Math.Min(2, artists.GetValueOrDefault(artist) * .3) + sessionArtists.GetValueOrDefault(artist) * .8;
            if (genre.Length > 0) score += Math.Min(1.5, genres.GetValueOrDefault(genre) * .1) + sessionGenres.GetValueOrDefault(genre) * .4;
            return score;
        }
        var seeds = tracks.ToDictionary(pair => pair.Key, pair => BaseScore(pair.Value.Track.ToTrack()));
        return track => BaseScore(track) + (related.TryGetValue(track.Id, out var sources)
            ? sources.Max(source => (1 + Math.Max(0, seeds.GetValueOrDefault(source.Key))) / (1 + source.Value * .05)) : 0);
    }
    internal static string ArtistKey(SoundCloudTrack track) => Clean(track.PublisherMetadata?.Artist ?? track.Author);
    internal static string WorkKey(SoundCloudTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.PublisherMetadata?.Isrc)) return "isrc:" + Clean(track.PublisherMetadata.Isrc);
        var title = Clean(track.Title);
        title = Regex.Replace(title, @"\([^)]*(?:sp(?:ee|e)d|reverb|slowed)[^)]*\)", "");
        title = Regex.Replace(title, @"\s+(?:sped\s*up|speed\s*up|slowed)(?:\b.*)?$", "");
        return ArtistKey(track) + ":" + title.Trim();
    }
    private static string Clean(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"\s+", " ").Trim();
}
