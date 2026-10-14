using Pookie.SoundCloud;
using Core = Pookie.Recommendations;

namespace Pookie.App.Home;

internal enum WaveMode { Balance, Favorites, Discoveries }

internal static class WaveTrack
{
    internal static Core.WaveTrack From(SoundCloudTrack track) => new(track.Id, track.Title,
        track.PublisherMetadata?.Artist ?? track.Author, track.User?.Id ?? 0, track.Genre ?? "", track.Duration,
        track.ArtworkUrl, track.PermalinkUrl, track.PublisherMetadata?.Isrc, track.Author)
        { Playable = HomeRecommendations.Playable(track) };

    internal static SoundCloudTrack ToTrack(this Core.WaveTrack track) => new() { Id = track.Id, Title = track.Title,
        Duration = track.Duration, Genre = track.Genre, ArtworkUrl = track.Artwork, PermalinkUrl = track.Url,
        User = new() { Id = track.Uploader, Username = track.UploaderName ?? track.Artist },
        PublisherMetadata = new() { Artist = track.Artist, Isrc = track.Isrc } };
}

// Only transport conversion lives here. Selection rules belong to the standalone module.
internal sealed class WaveTaste(Core.WaveProfile? profile = null)
{
    private readonly Core.WaveTaste taste = new(profile);
    internal Core.Storage.RecommendationSnapshot? Learning { get; set; }
    internal Guid? SessionId { get; set; }
    private Func<Core.WaveTrack, double> LearnedScorer()
    {
        var learner = new Core.Learning.OnlineRanker(Learning?.Model);
        var liked = taste.Snapshot().Tracks.Where(item => item.Liked).Select(item => item.Track.Id).ToHashSet();
        return taste.PersonalizedScorer(track => learner.Score(track, Learning?.Vectors.GetValueOrDefault(track.Id),
            liked.Contains(track.Id), session: SessionId), Learning?.Model.Samples ?? 0);
    }
    internal WaveMode Mode => (WaveMode)taste.Mode;
    internal bool Dirty => taste.Dirty;
    internal IEnumerable<SoundCloudTrack> KnownTracks => taste.KnownTracks.Select(WaveTrack.ToTrack);
    internal Core.WaveProfile Snapshot() => taste.Snapshot();
    internal void Saved() => taste.Saved();
    internal void SyncLikes(IReadOnlySet<long> liked) => taste.SyncLikes(liked);
    internal void SetMode(WaveMode mode) => taste.SetMode((Core.WaveMode)mode);
    internal void Relate(SoundCloudTrack seed, IEnumerable<SoundCloudTrack> candidates) => taste.Relate(WaveTrack.From(seed), candidates.Select(WaveTrack.From));
    internal void Import(SoundCloudTrack track, bool familiar, bool? liked = null) => taste.Import(WaveTrack.From(track), familiar, liked);
    internal void Block(SoundCloudTrack track) => taste.Block(WaveTrack.From(track));
    internal bool IsBlocked(SoundCloudTrack track) => taste.IsBlocked(WaveTrack.From(track));
    internal bool IsFamiliar(SoundCloudTrack track) => taste.IsFamiliar(WaveTrack.From(track));
    internal SoundCloudTrack[] Unblock(SoundCloudTrack track) => taste.Unblock(WaveTrack.From(track)).Select(WaveTrack.ToTrack).ToArray();
    internal void ResetBlocks() => taste.ResetBlocks();
    internal void Record(SoundCloudTrack track, double seconds, bool earlySkip, DateTimeOffset now, bool rejected = false) => taste.Record(WaveTrack.From(track), seconds, earlySkip, now, rejected);
    internal SoundCloudTrack[] Seeds(IEnumerable<SoundCloudTrack> candidates, int count = 3)
    {
        var originals = candidates.Where(HomeRecommendations.Playable).DistinctBy(track => track.Id).ToDictionary(track => track.Id);
        return taste.Seeds(originals.Values.Select(WaveTrack.From), count, LearnedScorer()).Select(track => originals[track.Id]).ToArray();
    }
    internal SoundCloudTrack[] Rank(IEnumerable<SoundCloudTrack> candidates, IEnumerable<SoundCloudTrack> recent,
        int count, DateTimeOffset now, Random? random = null, IEnumerable<SoundCloudTrack>? reserved = null, int startIndex = 0)
    {
        var originals = candidates.Where(HomeRecommendations.Playable).DistinctBy(track => track.Id).ToDictionary(track => track.Id);
        return taste.Rank(originals.Values.Select(WaveTrack.From), recent.Select(WaveTrack.From), count, now,
            random, reserved?.Select(WaveTrack.From), LearnedScorer(), startIndex: startIndex).Select(track => originals[track.Id]).ToArray();
    }
    internal static string ArtistKey(SoundCloudTrack track) => Core.WaveTaste.ArtistKey(WaveTrack.From(track));
    internal static string WorkKey(SoundCloudTrack track) => Core.WaveTaste.WorkKey(WaveTrack.From(track));
}
