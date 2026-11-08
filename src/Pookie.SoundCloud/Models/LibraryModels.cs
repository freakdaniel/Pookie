namespace Pookie.SoundCloud;

public sealed record LibraryItem(string Key, string Title, string Subtitle, string? ArtworkUrl,
    SoundCloudTrack? Track = null, SoundCloudPlaylist? Playlist = null, SoundCloudUser? User = null)
{
    public string? ActivityType { get; init; }
    public string? ActivityAt { get; init; }
    public bool MatchedByLyrics { get; init; }
    public bool IsAlbum => Playlist is { IsAlbum: true } || Playlist?.PlaylistType?.ToLowerInvariant() is "album" or "ep" or "single" or "compilation";
    public static LibraryItem FromTrack(SoundCloudTrack track) => new("track:" + track.Id, track.Title, track.Author, track.ArtworkUrl ?? track.User?.AvatarUrl, Track: track);
}
public sealed record CollectionDetail(LibraryItem Item, TrackPage Tracks);

public sealed record LibraryPage(LibraryItem[] Items, string? NextHref);
