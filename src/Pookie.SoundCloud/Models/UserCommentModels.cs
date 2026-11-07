namespace Pookie.SoundCloud;

public sealed record UserComment(SoundCloudComment Comment, SoundCloudTrack? Track);
public sealed record UserCommentPage(UserComment[] Comments, string? NextHref);
