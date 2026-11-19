using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

public sealed record SoundCloudPlaylist
{
    public long Id { get; init; }
    public string Urn { get; init; } = "";
    public string Title { get; init; } = "";
    public string? ArtworkUrl { get; init; }
    public string? CalculatedArtworkUrl { get; init; }
    [JsonIgnore] internal bool ArtworkFieldMissing { get; init; }
    [JsonIgnore] internal bool UsesTrackArtwork => IsAlbum || PlaylistType?.ToLowerInvariant() is "album" or "ep" or "single" or "compilation" ||
        IsSystemPlaylist || Urn.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal);
    public string? ShortTitle { get; init; }
    public string? ShortDescription { get; init; }
    public SoundCloudUser? MadeFor { get; init; }
    public string? PermalinkUrl { get; init; }
    public string? Permalink { get; init; }
    public string? SetType { get; init; }
    public string? ReleaseDate { get; init; }
    public int? ReleaseDay { get; init; }
    public int? ReleaseMonth { get; init; }
    public int? ReleaseYear { get; init; }
    public string? PurchaseUrl { get; init; }
    public string? LabelName { get; init; }
    public string? License { get; init; }
    public string? PlaylistType { get; init; }
    public bool IsAlbum { get; init; }
    public bool? IsPublic { get; init; }
    [JsonIgnore] public bool IsSystemMix => IsSystemPlaylist || Urn?.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal) == true;
    public bool IsSystemPlaylist { get; init; }
    public int TrackCount { get; init; }
    public double Duration { get; init; }
    public string? Description { get; init; }
    public string? Sharing { get; init; }
    public string? SecretToken { get; init; }
    public string? Uri { get; init; }
    [JsonIgnore] public bool IsPrivate => string.Equals(Sharing, "private", StringComparison.OrdinalIgnoreCase);
    public string? Genre { get; init; }
    public string[] Tags { get; init; } = [];
    public string? TagList { get; init; }
    public string? CreatedAt { get; init; }
    public string? LastModified { get; init; }
    public SoundCloudUser? User { get; init; }
    public SoundCloudTrack[] Tracks { get; init; } = [];
    // Marks a fully enumerated track order, including an empty playlist.
    [JsonIgnore] public bool TrackPagesComplete { get; init; }
}
