namespace Pookie.SoundCloud;

public sealed record PlaylistUpdateRequest(long UserId, long PlaylistId, string Title, string Description, string Sharing,
    long[]? TrackIds = null, long[]? ExpectedTrackIds = null, PlaylistMetadataUpdate? Metadata = null)
{
    public bool IsValid() => UserId is > 0 and <= 9007199254740991 && PlaylistId is > 0 and <= 9007199254740991 &&
        Title is { Length: > 0 and <= 255 } && Title == Title.Trim() && !Title.Any(char.IsControl) &&
        Description is { Length: <= 4000 } && !Description.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) &&
        Sharing is "public" or "private" && (Metadata == null || Metadata.IsValid()) &&
        (TrackIds == null || TrackIds.Length <= 500 && TrackIds.All(id => id is > 0 and <= 9007199254740991)) &&
        (ExpectedTrackIds == null || ExpectedTrackIds.Length <= 500 && ExpectedTrackIds.All(id => id is > 0 and <= 9007199254740991));
}
