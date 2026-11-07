namespace Pookie.SoundCloud;

public sealed record SoundCloudComment
{
    public long Id { get; init; }
    public string Body { get; init; } = "";
    public string? CreatedAt { get; init; }
    public double? Timestamp { get; init; }
    public SoundCloudUser? User { get; init; }
    public SoundCloudComment[] Replies { get; init; } = [];
    public int ReplyCount { get; init; }
    public string? RepliesCursor { get; init; }
}

public sealed record TrackCommentPage(SoundCloudComment[] Comments, string? NextHref);
public sealed record TrackReplyPage(SoundCloudComment[] Comments, int Total, string? Cursor);
public sealed record TrackFan(SoundCloudUser User, long Plays);
public sealed record TrackSidebar(TrackFan[] Fans, LibraryItem[] Playlists, LibraryItem[] Albums, bool FansHidden = false, bool HasMorePlaylists = false);
