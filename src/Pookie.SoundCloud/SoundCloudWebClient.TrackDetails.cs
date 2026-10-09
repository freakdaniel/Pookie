using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<TrackCommentPage> GetTrackCommentsAsync(long trackId, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        try
        {
            using var graph = await ReadTrackDocumentAsync(new("comments", trackId), token);
            return ParseGraphComments(GraphObject(GraphData(graph), "trackComments"), trackId);
        }
        catch (NotSupportedException) { /* Compatibility with REST-only transports. */ }
        using var document = await GetJsonAsync(Api($"tracks/{trackId}/comments?limit=30&linked_partitioning=1&threaded=0&filter_replies=1"), token);
        return ParseComments(document.RootElement);
    }

    public async Task<TrackCommentPage> GetTrackCommentsNextAsync(long trackId, string cursor, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        if (cursor.StartsWith("sc-comments:", StringComparison.Ordinal))
        {
            var prefix = $"sc-comments:{trackId}:";
            if (!cursor.StartsWith(prefix, StringComparison.Ordinal)) throw new SoundCloudException("Неверный курсор комментариев.");
            string after;
            try { after = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor[prefix.Length..])); }
            catch (FormatException) { throw new SoundCloudException("Неверный курсор комментариев."); }
            var request = new TrackReadRequest("comments", trackId, Cursor: after);
            if (!request.IsValid()) throw new SoundCloudException("Неверный курсор комментариев.");
            using var graph = await ReadTrackDocumentAsync(request, token);
            return ParseGraphComments(GraphObject(GraphData(graph), "trackComments"), trackId);
        }
        if (!BrowserRequestCommand.IsReadUrl(cursor) || !Uri.TryCreate(cursor, UriKind.Absolute, out var uri) ||
            uri.AbsolutePath != $"/tracks/{trackId}/comments")
            throw new SoundCloudException("Неверный адрес страницы комментариев.");
        using var document = await GetJsonAsync(uri, token);
        return ParseComments(document.RootElement);
    }

    public async Task<TrackReplyPage> GetTrackRepliesAsync(long trackId, long commentId, string? cursor, CancellationToken token = default)
    {
        var request = new TrackReadRequest("replies", trackId, commentId, cursor);
        if (!request.IsValid()) throw new ArgumentException("Invalid track replies request.");
        using var graph = await ReadTrackDocumentAsync(request, token);
        var root = GraphObject(GraphData(graph), "trackCommentReplies");
        return new(ParseGraphCommentArray(root.GetProperty("comments")), (int)GraphNumber(root, "total"), GraphCursor(root, replies: true));
    }

    public async Task<TrackSidebar> GetTrackSidebarAsync(long trackId, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        using var graph = await ReadTrackDocumentAsync(new("sidebar", trackId), token);
        var data = GraphData(graph);
        var fans = new List<TrackFan>();
        var hidden = false;
        if (data.TryGetProperty("topFans", out var top) && top.ValueKind == JsonValueKind.Object)
        {
            hidden = top.TryGetProperty("hasArtistOptedOut", out var opted) && opted.ValueKind == JsonValueKind.True;
            if (!hidden && top.TryGetProperty("allTime", out var all) && all.ValueKind == JsonValueKind.Object &&
                all.TryGetProperty("fans", out var rows) && rows.ValueKind == JsonValueKind.Array)
                foreach (var row in rows.EnumerateArray().Take(5))
                    if (row.TryGetProperty("fan", out var fan) && ParseGraphUser(fan) is { Id: > 0 } user)
                        fans.Add(new(user, GraphNumber(row, "totalPlays")));
        }
        // The featured GraphQL preview is capped at three. Read the collection
        // itself to fill the overview's two-by-two playlist grid.
        using var preview = await GetJsonAsync(Api($"tracks/{trackId}/playlists_without_albums?limit=4&linked_partitioning=1"), token);
        var playlists = await ParseLibraryAsync(preview.RootElement, token);
        var items = playlists.Items.DistinctBy(item => item.Key).ToList();
        var cursor = playlists.NextHref;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        // SoundCloud's limit includes inaccessible playlists. Fill the visible
        // preview from subsequent pages without following an unbounded cursor chain.
        for (var page = 0; page < 2 && items.Count < 4 && cursor != null && visited.Add(cursor); page++)
        {
            var next = await GetTrackSectionAsync(trackId, "playlists", cursor, token);
            var keys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            items.AddRange(next.Items.Where(item => keys.Add(item.Key)));
            cursor = next.NextHref;
            if (next.Items.Length == 0) break;
        }
        return new(fans.ToArray(), items.Take(4).ToArray(), ParseGraphCollections(data, "albums", true).Take(3).ToArray(),
            hidden, items.Count > 4 || cursor != null);
    }

    private async Task<JsonDocument> ReadTrackDocumentAsync(TrackReadRequest request, CancellationToken token)
    {
        if (!request.IsValid()) throw new ArgumentException("Invalid track page read.");
        if (Session is { } account && !account.IsValid()) throw new SoundCloudException("Некорректная сессия SoundCloud.");
        if (BrowserTransport is { } browser && Session != null) return await browser.ReadTrackAsync(request, token);
        if (Session != null && RequireBrowserTransport) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        var clientId = await GetClientIdAsync(token);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("query", request.Kind switch { "comments" => TrackReadQueries.Comments, "replies" => TrackReadQueries.Replies, _ => TrackReadQueries.Sidebar });
            writer.WriteStartObject("variables"); writer.WriteString("trackUrn", $"soundcloud:tracks:{request.TrackId}");
            if (request.Kind == "replies") writer.WriteString("commentUrn", $"soundcloud:comments:{request.CommentId}");
            if (request.Kind != "sidebar")
            {
                writer.WriteStartObject("options"); writer.WriteNumber("first", 30);
                writer.WriteString("sort", request.Kind == "comments" ? "NEWEST" : "ASCENDING");
                if (request.Kind == "comments") { writer.WriteNumber("repliesFirst", 16); writer.WriteString("repliesSort", "ASCENDING"); }
                if (request.Cursor != null) writer.WriteString("after", request.Cursor);
                writer.WriteEndObject();
            }
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://graph.soundcloud.com/graphql?client_id=" + Uri.EscapeDataString(clientId));
        message.Content = new ByteArrayContent(output.ToArray());
        message.Content.Headers.ContentType = new("application/json");
        message.Headers.TryAddWithoutValidation("apollographql-client-name", "webi");
        message.Headers.UserAgent.ParseAdd(Session?.UserAgent ?? DefaultUserAgent);
        message.Headers.Referrer = new("https://soundcloud.com/");
        if (Session is { } session)
        {
            if (!session.IsValid()) throw new SoundCloudException("Некорректная сессия SoundCloud.");
            message.Headers.Authorization = new("OAuth", session.AccessToken);
        }
        using var response = await http.SendAsync(message, token);
        if (!response.IsSuccessStatusCode) throw new SoundCloudException("SoundCloud не загрузил сведения о треке.", (int)response.StatusCode);
        await using var body = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(body, cancellationToken: token);
    }

    private static JsonElement GraphData(JsonDocument document) => document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
        ? data : throw new SoundCloudException("SoundCloud не вернул сведения о треке.");

    private static JsonElement GraphObject(JsonElement data, string name) => data.TryGetProperty(name, out var section) && section.ValueKind == JsonValueKind.Object
        ? section : throw new SoundCloudException("SoundCloud не загрузил этот раздел трека.");

    private static TrackCommentPage ParseGraphComments(JsonElement root, long trackId)
    {
        var after = GraphCursor(root);
        return new(ParseGraphCommentArray(root.GetProperty("comments")), after == null ? null :
            $"sc-comments:{trackId}:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(after)));
    }

    private static SoundCloudComment[] ParseGraphCommentArray(JsonElement rows) => rows.ValueKind == JsonValueKind.Array
        ? rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object).Select(ParseGraphComment)
            .Where(comment => comment.Id > 0 && !string.IsNullOrWhiteSpace(comment.Body)).DistinctBy(comment => comment.Id).ToArray() : [];

    private static SoundCloudComment ParseGraphComment(JsonElement row)
    {
        var replies = row.TryGetProperty("replies", out var nested) && nested.ValueKind == JsonValueKind.Object;
        return new() { Id = GraphId(row), Body = GraphText(row, "body") ?? "", CreatedAt = GraphText(row, "createdAt"),
            Timestamp = row.TryGetProperty("trackTime", out var time) && time.ValueKind == JsonValueKind.Number && time.TryGetDouble(out var ms) ? ms : null,
            User = row.TryGetProperty("user", out var user) ? ParseGraphUser(user) : null,
            Replies = replies && nested.TryGetProperty("comments", out var comments) ? ParseGraphCommentArray(comments) : [],
            ReplyCount = replies ? (int)GraphNumber(nested, "total") : 0,
            RepliesCursor = replies ? GraphCursor(nested, replies: true) : null };
    }

    private static SoundCloudUser? ParseGraphUser(JsonElement row) => row.ValueKind != JsonValueKind.Object ? null : new() {
        Id = GraphId(row), Username = GraphText(row, "username") ?? "", AvatarUrl = GraphText(row, "avatarUrl"),
        PermalinkUrl = GraphText(row, "permalinkUrl"), FollowersCount = GraphNumber(row, "followersCount"), TrackCount = GraphNumber(row, "tracksCount") };

    private static LibraryItem[] ParseGraphCollections(JsonElement data, string name, bool album)
    {
        if (!data.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array) return [];
        return rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object && GraphId(row) > 0).Select(row => LibraryData.FromPlaylist(new() {
            Id = GraphId(row), Urn = GraphText(row, "urn") ?? "", Title = GraphText(row, "title") ?? "",
            ArtworkUrl = GraphText(row, "artworkUrl"), PermalinkUrl = GraphText(row, "permalinkUrl"), IsAlbum = album,
            User = row.TryGetProperty("user", out var user) ? ParseGraphUser(user) : null })).DistinctBy(item => item.Key).ToArray();
    }

    private static string? GraphText(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long GraphNumber(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
    private static long GraphId(JsonElement row) => long.TryParse((GraphText(row, "urn") ?? "").Split(':').Last(), out var id) && id is > 0 and <= 9007199254740991 ? id : 0;
    private static string? GraphCursor(JsonElement row, bool replies = false) => row.TryGetProperty("pageInfo", out var page) && page.ValueKind == JsonValueKind.Object &&
        (!replies || page.TryGetProperty("hasNextPage", out var more) && more.ValueKind == JsonValueKind.True) ? GraphText(page, "endCursor") : null;

    public async Task<TrackPage> GetRelatedTracksAsync(long trackId, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        using var document = await GetJsonAsync(Api($"tracks/{trackId}/related?limit=6&linked_partitioning=1"), token);
        return ParsePage(document.RootElement);
    }

    public async Task<LibraryPage> GetTrackSectionAsync(long trackId, string section, string? cursor = null, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        var endpoint = section switch
        {
            "reposts" => "reposters", "albums" => "albums", "playlists" => "playlists_without_albums",
            "related" => "related", _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        var path = $"/tracks/{trackId}/{endpoint}";
        Uri uri;
        if (cursor == null) uri = Api(path.TrimStart('/') + "?limit=30&linked_partitioning=1");
        else if (!BrowserRequestCommand.IsReadUrl(cursor) || !Uri.TryCreate(cursor, UriKind.Absolute, out uri!) || uri.AbsolutePath != path)
            throw new SoundCloudException("Неверный адрес раздела трека.");
        using var document = await GetJsonAsync(uri, token);
        if (section != "related") return await ParseLibraryAsync(document.RootElement, token);
        var tracks = ParsePage(document.RootElement);
        return new(tracks.Tracks.Select(LibraryItem.FromTrack).ToArray(), tracks.NextHref);
    }

    private static void ValidateDetailTrackId(long id)
    {
        if (id is <= 0 or > 9007199254740991) throw new ArgumentOutOfRangeException(nameof(id));
    }

    private static TrackCommentPage ParseComments(JsonElement root)
    {
        var collection = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("collection");
        var comments = new List<SoundCloudComment>();
        foreach (var item in collection.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var comment = item.Deserialize(SoundCloudJson.Default.SoundCloudComment);
            if (comment is { Id: > 0, Body.Length: > 0 }) comments.Add(comment);
        }
        var next = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("next_href", out var href) &&
            href.ValueKind == JsonValueKind.String ? href.GetString() : null;
        return new(comments.ToArray(), next);
    }
}
