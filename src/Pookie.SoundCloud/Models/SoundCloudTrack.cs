using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

public sealed record SoundCloudTrack
{
    public string? Sharing { get; init; }
    public bool? Commentable { get; init; }
    public long Id { get; init; }
    public string Urn { get; init; } = "";
    public string Title { get; init; } = "";
    public double Duration { get; init; }
    public string? ArtworkUrl { get; init; }
    public string? WaveformUrl { get; init; }
    public TrackVisuals? Visuals { get; init; }
    [JsonIgnore] public string? TrackArtUrl => Visuals is { Enabled: true, Visuals: { } visuals }
        ? visuals.FirstOrDefault(visual => visual is { EntryTime: 0 } && !string.IsNullOrWhiteSpace(visual.VisualUrl))?.VisualUrl : null;
    public string? CreatedAt { get; init; }
    public string? Genre { get; init; }
    public string? Description { get; init; }
    public long? PlaybackCount { get; init; }
    public long? LikesCount { get; init; }
    public long? CommentCount { get; init; }
    public long? RepostsCount { get; init; }
    public string? License { get; init; }
    public string? PermalinkUrl { get; init; }
    public string? Access { get; init; }
    public string? Policy { get; init; }
    public double FullDuration { get; init; }
    public string? TrackAuthorization { get; init; }
    public SoundCloudUser? User { get; init; }
    public TrackMedia? Media { get; init; }
    public PublisherMetadata? PublisherMetadata { get; init; }
    public double DurationSeconds => Duration / 1000;
    public string Author => User?.Username ?? "Unknown artist";
}

public sealed record TrackVisuals(bool? Enabled, TrackVisual[]? Visuals);
public sealed record TrackVisual(string? VisualUrl, double? EntryTime = 0);

public sealed record PublisherMetadata
{
    public string? Artist { get; init; }
    public string? AlbumTitle { get; init; }
    public string? Isrc { get; init; }
}

public sealed record TrackMedia
{
    public Transcoding[] Transcodings { get; init; } = [];
}

public sealed record Transcoding
{
    public string Url { get; init; } = "";
    public string Preset { get; init; } = "";
    public string? Quality { get; init; }
    public bool Snipped { get; init; }
    public TranscodingFormat? Format { get; init; }
}

public sealed record TranscodingFormat
{
    public string Protocol { get; init; } = "";
    public string MimeType { get; init; } = "";
}

public sealed record TrackPage(SoundCloudTrack[] Tracks, string? NextHref);
