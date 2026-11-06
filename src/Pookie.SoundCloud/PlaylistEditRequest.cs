namespace Pookie.SoundCloud;

public sealed record PlaylistEditRequest(string Kind, long UserId, long PlaylistId, PlaylistUpdateRequest? Update = null,
    long TrackId = 0, string? Artwork = null)
{
    public bool IsValid() => UserId is > 0 and <= 9007199254740991 && PlaylistId is > 0 and <= 9007199254740991 &&
        (Kind == "update" ? Update?.IsValid() == true && Update.UserId == UserId && Update.PlaylistId == PlaylistId && TrackId == 0 && Artwork == null :
         Update == null && (Kind == "delete" ? TrackId == 0 && Artwork == null :
         Kind == "remove" ? TrackId is > 0 and <= 9007199254740991 && Artwork == null :
         Kind == "artwork" && TrackId == 0 && Artwork is { Length: > 0 and <= 1398104 } && ValidJpeg(Artwork)));

    private static bool ValidJpeg(string value)
    {
        var buffer = new byte[value.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(value, buffer, out var count) && count is >= 4 and <= 1048576 &&
            buffer[0] == 255 && buffer[1] == 216 && buffer[count - 2] == 255 && buffer[count - 1] == 217;
    }
    public override string ToString() => $"Playlist edit ({Kind}; content redacted)";
}
