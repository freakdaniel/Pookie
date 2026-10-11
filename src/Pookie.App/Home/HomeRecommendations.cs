using Pookie.SoundCloud;

namespace Pookie.App.Home;

internal sealed record HomeContent(LibraryItem[] Recent, LibraryItem[] Recommendations, LibraryItem[] Releases, LibraryItem[] Mixes, string[] Seeds);

internal static class HomeRecommendations
{
    internal static bool Playable(SoundCloudTrack track) => track.Id > 0 && !string.IsNullOrWhiteSpace(track.Title) &&
        track.Access is not ("blocked" or "preview") && track.Policy is not ("BLOCK" or "SNIP");

    internal static SoundCloudTrack[] Seeds(SoundCloudTrack[] likes, LibraryItem[] history) =>
        likes.Where(Playable).Take(3).Concat(history.Select(item => item.Track).OfType<SoundCloudTrack>().Where(Playable).Take(3))
            .Where(Playable).DistinctBy(track => track.Id).Take(5).ToArray();

    internal static HomeContent RefreshRecent(HomeContent content, IEnumerable<LibraryItem> local)
    {
        var recent = local.Concat(content.Recent).Where(item => item.Playlist != null || item.Track is { } track && Playable(track))
            .DistinctBy(item => item.Key).Take(6).ToArray();
        var keys = recent.Select(item => item.Key).ToHashSet();
        return content with { Recent = recent, Recommendations = content.Recommendations.Where(item => !keys.Contains(item.Key)).ToArray(),
            Releases = content.Releases.Where(item => !keys.Contains(item.Key)).ToArray(), Mixes = content.Mixes.Where(item => !keys.Contains(item.Key)).ToArray() };
    }

    internal static HomeContent Build(LibraryItem[] recent, LibraryItem[] history, SoundCloudTrack[] likes,
        SoundCloudTrack[] seeds, TrackPage[] related, SoundCloudTrack[] feed, IReadOnlySet<long> following,
        LibraryItem[] mixes, DateTimeOffset now, IReadOnlySet<long>? knownLikedIds = null)
    {
        var resume = recent.Concat(history).Where(item => item.Playlist != null || item.Track is { } track && Playable(track))
            .DistinctBy(item => item.Key).Take(6).ToArray();
        var used = resume.Select(item => item.Key).ToHashSet();
        var known = likes.Select(track => track.Id).Concat(history.Select(item => item.Track?.Id ?? 0)).ToHashSet();
        if (knownLikedIds != null) known.UnionWith(knownLikedIds);
        var recommendations = new List<LibraryItem>();
        var artists = new Dictionary<string, int>();
        // Interleave seed results so one song cannot fill the entire recommendation section.
        for (var row = 0; row < related.Select(page => page.Tracks.Length).DefaultIfEmpty().Max(); row++)
            foreach (var page in related)
            {
                if (recommendations.Count >= 12 || page.Tracks.ElementAtOrDefault(row) is not { } track || !Playable(track) || known.Contains(track.Id)) continue;
                var artist = track.User?.Id is > 0 ? "id:" + track.User.Id : "name:" + track.Author.Trim().ToUpperInvariant();
                if (artists.GetValueOrDefault(artist) >= 2 || !used.Add("track:" + track.Id)) continue;
                artists[artist] = artists.GetValueOrDefault(artist) + 1;
                recommendations.Add(LibraryItem.FromTrack(track));
            }
        var releases = feed.Where(track => Playable(track) && track.User != null && following.Contains(track.User.Id) &&
                DateTimeOffset.TryParse(track.CreatedAt, out var created) && created <= now && created >= now.AddDays(-30))
            .DistinctBy(track => track.Id).OrderByDescending(track => DateTimeOffset.Parse(track.CreatedAt!))
            .Where(track => used.Add("track:" + track.Id)).Take(6).Select(LibraryItem.FromTrack).ToArray();
        var personal = mixes.Where(item => used.Add(item.Key)).Take(6).ToArray();
        return new(resume, recommendations.ToArray(), releases, personal, seeds.Select(track => track.Title).Take(2).ToArray());
    }
}
