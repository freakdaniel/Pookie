using System.Text;
using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudHttpTransport
{
    public async Task<JsonDocument> CreatePlaylistAsync(PlaylistCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsValid()) throw new ArgumentException("Некорректные данные плейлиста.");
        var account = Account();
        if (account.MobileIdentity != null)
        {
            using var created = await ExecuteAsync(HttpMethod.Post, SoundCloudMobileRoutes.Uri("/playlists"), Json(writer =>
            {
                writer.WriteStartObject(); writer.WriteStartObject("playlist");
                writer.WriteString("title", request.Title); writer.WriteBoolean("public", request.Sharing == "public");
                writer.WriteEndObject(); writer.WriteStartArray("track_urns");
                writer.WriteStringValue(SoundCloudMobileRoutes.Urn("tracks", request.TrackId));
                writer.WriteEndArray(); writer.WriteEndObject();
            }), null, cancellationToken).ConfigureAwait(false);
            long playlistId = 0;
            try
            {
                var metadata = SoundCloudMobilePlaylist.ParseMetadata(created.RootElement);
                playlistId = metadata.Id;
                if (metadata.User?.Id != request.UserId || metadata.Title != request.Title || metadata.Sharing != request.Sharing)
                    throw new SoundCloudException("SoundCloud не подтвердил создание плейлиста.");
                if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
                // Android returns ApiPlaylistWrapper, without the seed track list.
                // Confirm it by reading the new resource; never repeat the POST.
                var playlist = await GetMobilePlaylistAsync(metadata.Id, cancellationToken).ConfigureAwait(false);
                var result = JsonSerializer.SerializeToDocument(playlist, SoundCloudJson.Default.SoundCloudPlaylist);
                if (request.Confirms(result.RootElement)) return result;
                result.Dispose();
                throw new SoundCloudException("SoundCloud не подтвердил создание плейлиста.");
            }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or JsonException)
            { throw new PlaylistCreationUnconfirmedException(playlistId, error); }
        }
        // The web client serializes tracks to IDs and wraps attributes in playlist.
        return await ExecuteAsync(HttpMethod.Post, Api("/playlists"), Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteStartObject("playlist");
            writer.WriteString("title", request.Title); writer.WriteString("sharing", request.Sharing);
            writer.WriteStartArray("tracks"); writer.WriteNumberValue(request.TrackId); writer.WriteEndArray();
            writer.WriteEndObject(); writer.WriteEndObject();
        }), verification == null ? null : ct => verification.CreatePlaylistAsync(request, ct), cancellationToken,
            validate: root => { if (!request.Confirms(root)) throw new SoundCloudException("SoundCloud не подтвердил создание плейлиста."); }).ConfigureAwait(false);
    }

    private readonly object identityGate = new();
    private string? identityToken;
    private Task<long>? identity;
    private readonly string playbackSession = Guid.NewGuid().ToString();

    public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken cancellationToken = default)
    {
        ValidateId(userId); ValidateId(trackId);
        return Account().MobileIdentity == null
            ? ActionAsync(liked ? HttpMethod.Put : HttpMethod.Delete, $"/users/{userId}/track_likes/{trackId}", null,
                verification == null ? null : ct => verification.SetLikedAsync(userId, trackId, liked, ct), cancellationToken)
            : LikeAsync("tracks", trackId, liked, verification == null ? null : ct => verification.SetLikedAsync(userId, trackId, liked, ct), cancellationToken);
    }

    public Task SetPlaylistLikedAsync(long userId, long playlistId, bool liked, CancellationToken cancellationToken = default)
    {
        ValidateId(userId); ValidateId(playlistId);
        return Account().MobileIdentity == null
            ? ActionAsync(liked ? HttpMethod.Put : HttpMethod.Delete, $"/users/{userId}/playlist_likes/{playlistId}", null,
                verification == null ? null : ct => verification.SetPlaylistLikedAsync(userId, playlistId, liked, ct), cancellationToken)
            : LikeAsync("playlists", playlistId, liked, verification == null ? null : ct => verification.SetPlaylistLikedAsync(userId, playlistId, liked, ct), cancellationToken);
    }

    private async Task LikeAsync(string kind, long id, bool liked, Func<CancellationToken, Task>? verify, CancellationToken token)
    {
        var urn = SoundCloudMobileRoutes.Urn(kind, id);
        using var result = await ExecuteAsync(HttpMethod.Post, SoundCloudMobileRoutes.Uri($"/likes/{kind}/{(liked ? "create" : "delete")}"), Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteStartArray("likes"); writer.WriteStartObject();
            writer.WriteString("target_urn", urn);
            writer.WriteEndObject(); writer.WriteEndArray(); writer.WriteEndObject();
        }), verify == null ? null : async ct => { await verify(ct).ConfigureAwait(false); return JsonDocument.Parse("{}"); }, token,
            validate: root =>
            {
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array ||
                    !collection.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("target_urn", out var target) &&
                        target.ValueKind == JsonValueKind.String && target.GetString() == urn))
                    throw new SoundCloudException("SoundCloud не подтвердил изменение лайка.");
            }).ConfigureAwait(false);
    }

    public Task SetRepostedAsync(long trackId, bool reposted, CancellationToken cancellationToken = default)
    {
        ValidateId(trackId);
        return RepostAsync("tracks", trackId, reposted,
            verification == null ? null : ct => verification.SetRepostedAsync(trackId, reposted, ct), cancellationToken);
    }

    public Task SetPlaylistRepostedAsync(long playlistId, bool reposted, CancellationToken cancellationToken = default)
    {
        ValidateId(playlistId);
        return RepostAsync("playlists", playlistId, reposted,
            verification == null ? null : ct => verification.SetPlaylistRepostedAsync(playlistId, reposted, ct), cancellationToken);
    }

    private Task RepostAsync(string kind, long id, bool reposted, Func<CancellationToken, Task>? verify, CancellationToken token) =>
        ActionAsync(reposted ? Account().MobileIdentity == null ? HttpMethod.Put : HttpMethod.Post : HttpMethod.Delete,
            Account().MobileIdentity == null ? $"/me/{(kind == "tracks" ? "track" : "playlist")}_reposts/{id}" : $"/reposts/{kind}/{SoundCloudMobileRoutes.Urn(kind, id)}",
            null, verify, token, mobileApi: Account().MobileIdentity != null);

    public async Task SetFollowingAsync(long userId, long artistId, bool following, CancellationToken cancellationToken = default)
    {
        ValidateId(userId); ValidateId(artistId);
        if (userId == artistId) throw new ArgumentException("Нельзя подписаться на свой профиль.");
        var mobile = Account().MobileIdentity != null;
        var path = mobile ? $"/follows/users/{SoundCloudMobileRoutes.Urn("users", artistId)}" : $"/me/followings/{artistId}";
        if (following && !mobile)
        {
            if (verification == null) throw new SoundCloudException("Подпись подписки SoundCloud ещё не подключена.");
            // The site's signing helper is an authorization operation. The actual
            // follow request remains native and carries the current session.
            var signature = await verification.GetFollowSignatureAsync(userId, artistId, cancellationToken).ConfigureAwait(false);
            if (signature is not { Length: > 0 and <= 4096 } || signature.Any(char.IsControl))
                throw new SoundCloudException("SoundCloud не предоставил подпись подписки.");
            path += "?signature=" + Uri.EscapeDataString(signature);
        }
        await ActionAsync(following ? HttpMethod.Post : HttpMethod.Delete, path, null,
            verification == null ? null : ct => verification.SetFollowingAsync(userId, artistId, following, ct), cancellationToken, mobileApi: mobile).ConfigureAwait(false);
    }

    private readonly SemaphoreSlim playlistEdits = new(1, 1);
    public async Task AddToPlaylistAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken = default)
    {
        ValidateId(userId); ValidateId(playlistId); ValidateId(trackId);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await playlistEdits.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try { await AddToPlaylistCoreAsync(userId, playlistId, trackId, cancellation.Token).ConfigureAwait(false); }
        finally { playlistEdits.Release(); }
    }
    private async Task AddToPlaylistCoreAsync(long userId, long playlistId, long trackId, CancellationToken cancellationToken)
    {
        var account = Account();
        var mobile = account.MobileIdentity != null;
        if (mobile)
        {
            var snapshot = await GetMobilePlaylistAsync(playlistId, cancellationToken).ConfigureAwait(false);
            if (!account.Equals(Account())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
            if (snapshot.User?.Id != userId || snapshot.IsAlbum || snapshot.IsSystemPlaylist)
                throw new SoundCloudException("Этот плейлист нельзя редактировать.");
            if (snapshot.Tracks.Any(track => track.Id == trackId)) return;
            if (snapshot.TrackCount >= 500) throw new SoundCloudException("Плейлист заполнен.");
            await ActionAsync(HttpMethod.Post, $"/playlists/{SoundCloudMobileRoutes.Urn("playlists", playlistId)}/tracks", Json(writer =>
            {
                writer.WriteStartObject(); writer.WriteString("track_urn", SoundCloudMobileRoutes.Urn("tracks", trackId)); writer.WriteEndObject();
            }), null, cancellationToken, mobileApi: true).ConfigureAwait(false);
            return;
        }
        using var current = await GetJsonAsync(Api($"/playlists/{playlistId}?representation=full"), cancellationToken).ConfigureAwait(false);
        var playlist = current.RootElement;
        if (!MatchesResource(playlist, "playlists", playlistId) || !playlist.TryGetProperty("user", out var user) || !MatchesResource(user, "users", userId) ||
            playlist.TryGetProperty("is_album", out var album) && album.ValueKind == JsonValueKind.True)
            throw new SoundCloudException("Этот плейлист нельзя редактировать.");
        playlist.TryGetProperty("tracks", out var tracks);
        if (tracks.ValueKind == JsonValueKind.Array && tracks.EnumerateArray().Any(track => MatchesResource(track, "tracks", trackId))) return;
        if (playlist.TryGetProperty("track_count", out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var total) && total >= 500)
            throw new SoundCloudException("Плейлист заполнен.");
        // SoundCloud's playlist model saves its complete numeric track order.
        // Do not replace the list unless the fresh full snapshot is intact.
        if (tracks.ValueKind != JsonValueKind.Array || !playlist.TryGetProperty("track_count", out var fullCount) ||
            fullCount.ValueKind != JsonValueKind.Number || !fullCount.TryGetInt32(out var expected) ||
            expected < 0 || tracks.GetArrayLength() != expected)
            throw new SoundCloudException("SoundCloud не подтвердил полный список треков плейлиста.");
        var ids = new List<long>();
        foreach (var track in tracks.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.Object || !track.TryGetProperty("id", out var value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var id) || id is <= 0 or > 9007199254740991)
                throw new SoundCloudException("SoundCloud не подтвердил полный список треков плейлиста.");
            ids.Add(id);
        }
        ids.Add(trackId);
        await ActionAsync(HttpMethod.Put, $"/playlists/{playlistId}", Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteStartObject("playlist"); writer.WriteStartArray("tracks");
            foreach (var id in ids) writer.WriteNumberValue(id);
            writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject();
        }), verification == null ? null : ct => verification.AddToPlaylistAsync(userId, playlistId, trackId, ct), cancellationToken).ConfigureAwait(false);
    }

    private static bool MatchesResource(JsonElement value, string kind, long id)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        if (value.TryGetProperty("id", out var number) && number.ValueKind == JsonValueKind.Number) return number.TryGetInt64(out var actual) && actual == id;
        return value.TryGetProperty("urn", out var urn) && urn.ValueKind == JsonValueKind.String && urn.GetString() == SoundCloudMobileRoutes.Urn(kind, id);
    }

    public Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsValid()) throw new ArgumentException("Некорректный запрос страницы трека.");
        return GraphAsync(request.Kind switch { "comments" => TrackReadQueries.Comments, "replies" => TrackReadQueries.Replies, _ => TrackReadQueries.Sidebar }, writer =>
        {
            writer.WriteString("trackUrn", $"soundcloud:tracks:{request.TrackId}");
            if (request.Kind == "replies") writer.WriteString("commentUrn", $"soundcloud:comments:{request.CommentId}");
            if (request.Kind == "sidebar") return;
            writer.WriteStartObject("options"); writer.WriteNumber("first", 30);
            writer.WriteString("sort", request.Kind == "comments" ? request.Sort : "ASCENDING");
            if (request.Kind == "comments") { writer.WriteNumber("repliesFirst", 16); writer.WriteString("repliesSort", "ASCENDING"); }
            if (request.Cursor != null) writer.WriteString("after", request.Cursor);
            writer.WriteEndObject();
        }, verification == null ? null : ct => verification.ReadTrackAsync(request, ct), cancellationToken);
    }

    public Task<JsonDocument> WriteCommentAsync(CommentWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsValid()) throw new ArgumentException("Некорректный комментарий.");
        return GraphAsync(request.Kind == "create" ? CommentWriteQueries.Create : CommentWriteQueries.Delete, writer =>
        {
            writer.WriteStartObject("input");
            if (request.Kind == "create")
            {
                writer.WriteString("trackUrn", $"soundcloud:tracks:{request.TrackId}"); writer.WriteString("body", request.Body);
                writer.WriteNumber("timestamp", Math.Round(request.Timestamp ?? 0, MidpointRounding.AwayFromZero));
            }
            else writer.WriteString("commentUrn", $"soundcloud:comments:{request.CommentId}");
            writer.WriteEndObject();
        }, verification == null ? null : ct => verification.WriteCommentAsync(request, ct), cancellationToken);
    }

    private Task<JsonDocument> GraphAsync(string query, Action<Utf8JsonWriter> variables,
        Func<CancellationToken, Task<JsonDocument>>? verify, CancellationToken token) =>
        ExecuteAsync(HttpMethod.Post, new("https://graph.soundcloud.com/graphql"), Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("query", query); writer.WriteStartObject("variables");
            variables(writer); writer.WriteEndObject(); writer.WriteEndObject();
        }), verify, token);

    public async Task<JsonDocument> RequestChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsValid()) throw new ArgumentException("Некорректный запрос переписки.");
        if (await UserIdAsync(cancellationToken).ConfigureAwait(false) != request.UserId)
            throw new SoundCloudException("Переписка принадлежит другой учётной записи.");
        var path = $"/users/{request.UserId}/conversations" + (request.Kind == "list" ? "" : $"/{request.RecipientId}") +
            (request.Kind == "thread" ? "/messages" : "");
        var uri = request.Cursor == null ? Api(path) : new Uri(request.Cursor);
        if (!request.IsWrite) uri = new UriBuilder(uri) { Query = uri.Query.TrimStart('?') + "&limit=30&linked_partitioning=1" }.Uri;
        var body = request.Kind == "send" ? Json(writer =>
        { writer.WriteStartObject(); writer.WriteString("contents", request.Body); writer.WriteEndObject(); }) :
            request.Kind == "read" ? Encoding.UTF8.GetBytes("read=true") : null;
        return await ExecuteAsync(request.Kind == "send" ? HttpMethod.Post : request.Kind == "read" ? HttpMethod.Put : HttpMethod.Get,
            uri, body, verification == null ? null : ct => verification.RequestChatAsync(request, ct), cancellationToken,
            request.Kind == "read" ? "application/x-www-form-urlencoded" : "application/json").ConfigureAwait(false);
    }

    private Task<long> UserIdAsync(CancellationToken token)
    {
        lock (identityGate)
        {
            var accessToken = Account().AccessToken;
            if (identity == null || identity.IsCanceled || identity.IsFaulted || identityToken != accessToken)
            { identityToken = accessToken; identity = ReadUserIdAsync(lifetime.Token); }
            return identity.WaitAsync(token);
        }
    }

    private async Task<long> ReadUserIdAsync(CancellationToken token)
    {
        using var document = await GetJsonAsync(Api("/me"), token).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt64(out var value) || value <= 0)
            throw new SoundCloudException("SoundCloud не подтвердил учётную запись.");
        return value;
    }

    private async Task ActionAsync(HttpMethod method, string path, byte[]? body,
        Func<CancellationToken, Task>? verify, CancellationToken token, bool mobileApi = false)
    {
        var uri = mobileApi ? SoundCloudMobileRoutes.Uri(path) : Api(path);
        using var document = await ExecuteAsync(method, uri, body, verify == null ? null : async ct =>
        { await verify(ct).ConfigureAwait(false); return JsonDocument.Parse("{}"); }, token, expectJson: false).ConfigureAwait(false);
    }

    private static void ValidateId(long id)
    {
        if (id is <= 0 or > 9007199254740991) throw new ArgumentOutOfRangeException(nameof(id));
    }
}
