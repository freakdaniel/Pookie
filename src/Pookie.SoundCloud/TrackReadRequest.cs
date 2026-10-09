namespace Pookie.SoundCloud;

// Bounded, read-only operations from the current SoundCloud track page. The
// browser receives an operation and IDs, never arbitrary GraphQL documents.
public sealed record TrackReadRequest(string Kind, long TrackId, long CommentId = 0, string? Cursor = null)
{
    public bool IsValid() => TrackId is > 0 and <= 9007199254740991 &&
        (Cursor == null || Cursor.Length is > 0 and <= 2048 && !Cursor.Any(char.IsControl)) &&
        (Kind is "comments" or "sidebar" ? CommentId == 0 && (Kind != "sidebar" || Cursor == null) :
         Kind == "replies" && CommentId is > 0 and <= 9007199254740991);

    public override string ToString() => $"Track page read ({Kind}; cursor redacted)";
}

public static class TrackReadQueries
{
    public const string Comments = """
        query PookieTrackComments($trackUrn: ID!, $options: TrackCommentsOptionsInput) {
          trackComments(trackUrn: $trackUrn, options: $options) {
            comments { urn body createdAt trackTime user { urn username avatarUrl permalinkUrl followersCount tracksCount }
              replies { total comments { urn body createdAt trackTime user { urn username avatarUrl permalinkUrl } }
                pageInfo { hasNextPage endCursor } } }
            pageInfo { endCursor }
          }
        }
        """;
    public const string Replies = """
        query PookieTrackReplies($trackUrn: ID!, $commentUrn: ID!, $options: TrackCommentRepliesOptionsInput) {
          trackCommentReplies(trackUrn: $trackUrn, commentUrn: $commentUrn, options: $options) {
            total comments { urn body createdAt trackTime user { urn username avatarUrl permalinkUrl } }
            pageInfo { hasNextPage endCursor }
          }
        }
        """;
    public const string Sidebar = """
        query PookieTrackSidebar($trackUrn: ID!) {
          topFans(input: { trackUrn: $trackUrn }) {
            hasArtistOptedOut
            allTime { fans { fan { urn username avatarUrl permalinkUrl } totalPlays } }
          }
          playlists: featuredPlaylists(input: { trackUrn: $trackUrn, filter: PLAYLIST, limit: 4 }) {
            urn title artworkUrl permalinkUrl user { urn username avatarUrl permalinkUrl }
          }
          albums: featuredPlaylists(input: { trackUrn: $trackUrn, filter: ALBUM, limit: 3 }) {
            urn title artworkUrl permalinkUrl user { urn username avatarUrl permalinkUrl }
          }
        }
        """;
}
