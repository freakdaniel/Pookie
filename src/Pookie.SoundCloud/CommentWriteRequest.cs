namespace Pookie.SoundCloud;

public sealed record CommentWriteRequest(string Kind, long TrackId, string? Body = null, double? Timestamp = null, long CommentId = 0)
{
    public const int MaxBodyLength = 1000;
    public bool IsValid() => TrackId is > 0 and <= 9007199254740991 &&
        (Kind == "create" ? CommentId == 0 && Body is { Length: > 0 and <= MaxBodyLength } && !string.IsNullOrWhiteSpace(Body) &&
            !Body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) &&
            (Timestamp == null || double.IsFinite(Timestamp.Value) && Timestamp is >= 0 and <= 86400000) :
         Kind == "delete" && CommentId is > 0 and <= 9007199254740991 && Body == null && Timestamp == null);
    public override string ToString() => $"Comment write ({Kind}; body redacted)";
}

public static class CommentWriteQueries
{
    public const string Create = """
        mutation PookieCreateTrackComment($input: CreateTrackCommentInput!) {
          createTrackComment(input: $input) {
            comment { urn body createdAt trackTime user { urn username avatarUrl permalinkUrl } }
          }
        }
        """;
    public const string Delete = """
        mutation PookieDeleteTrackComment($input: DeleteTrackCommentInput!) {
          deleteTrackComment(input: $input)
        }
        """;
}
