using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Messages;

internal sealed record ChatHistoryRange(string? Cursor, bool Complete, long Count);
internal sealed record ChatSearchHit(long PeerId, ChatMessage Message);
internal sealed record ChatHistoryWindow(ChatMessage[] Messages, string? Cursor, bool Complete);

/// <summary>One connection and one sequential worker. No SQLite work executes on a UI/audio callback.</summary>
internal sealed class ChatHistoryStore : IAsyncDisposable
{
    internal const long DefaultBudget = 256L * 1024 * 1024;
    private readonly Channel<Action<SqliteConnection>> work = Channel.CreateUnbounded<Action<SqliteConnection>>(new() { SingleReader = true });
    private readonly Task worker;
    private readonly string path;
    private readonly long budget;
    internal ChatHistoryStore(AppDataPaths paths, long account, long budget = DefaultBudget)
    {
        if (account <= 0) throw new ArgumentOutOfRangeException(nameof(account));
        path = Path.Combine(paths.Cache, "Chats", account.ToString(System.Globalization.CultureInfo.InvariantCulture), "history.sqlite3");
        this.budget = Math.Max(64 * 1024, budget);
        worker = Task.Run(RunAsync);
    }
    private async Task RunAsync()
    {
        try
        {
            AppDataPaths.CreatePrivateDirectory(Path.GetDirectoryName(path)!);
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            db.Open();
            Execute(db, """
                PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=2000;
                CREATE TABLE IF NOT EXISTS dialogs(peer INTEGER PRIMARY KEY, data TEXT NOT NULL, touched INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS messages(peer INTEGER NOT NULL,id TEXT NOT NULL,sender INTEGER NOT NULL,body TEXT NOT NULL,stamp INTEGER NOT NULL,touched INTEGER NOT NULL, PRIMARY KEY(peer,id));
                CREATE INDEX IF NOT EXISTS messages_time ON messages(peer,stamp,id);
                CREATE VIRTUAL TABLE IF NOT EXISTS message_search USING fts5(body,peer UNINDEXED,id UNINDEXED,tokenize='unicode61');
                CREATE TABLE IF NOT EXISTS ranges(peer INTEGER NOT NULL,cursor TEXT NOT NULL,next_cursor TEXT,complete INTEGER NOT NULL,touched INTEGER NOT NULL, PRIMARY KEY(peer,cursor));
                CREATE TABLE IF NOT EXISTS media_metadata(peer INTEGER NOT NULL,id TEXT NOT NULL,data TEXT NOT NULL,PRIMARY KEY(peer,id));
                CREATE TABLE IF NOT EXISTS attachments(peer INTEGER NOT NULL,id TEXT NOT NULL,kind TEXT NOT NULL,name TEXT NOT NULL, PRIMARY KEY(peer,id,kind,name));
                CREATE TABLE IF NOT EXISTS events(peer INTEGER NOT NULL,event_id TEXT NOT NULL,message_id TEXT NOT NULL,PRIMARY KEY(peer,event_id));
                CREATE TABLE IF NOT EXISTS reactions(peer INTEGER NOT NULL,target TEXT NOT NULL,sender INTEGER NOT NULL,emoji TEXT,stamp INTEGER NOT NULL,event_id TEXT NOT NULL,PRIMARY KEY(peer,target,sender));
                PRAGMA user_version=1;
                """);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await foreach (var item in work.Reader.ReadAllAsync()) item(db);
            Execute(db, "PRAGMA wal_checkpoint(TRUNCATE)");
        }
        catch (Exception error)
        {
            work.Writer.TryComplete(error);
            // Drain callers as failures, rather than leaving pending UI awaits forever.
            using var failed = new SqliteConnection();
            while (work.Reader.TryRead(out var item)) item(failed);
        }
    }
    private Task<T> Run<T>(Func<SqliteConnection, T> action, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!work.Writer.TryWrite(db =>
        {
            if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
            try { completion.TrySetResult(action(db)); }
            catch (Exception error) { try { Execute(db, "ROLLBACK"); } catch { } completion.TrySetException(error); }
        })) completion.TrySetException(new ObjectDisposedException(nameof(ChatHistoryStore)));
        return completion.Task;
    }
    private static int Execute(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        using var command = Command(db, sql, args); return command.ExecuteNonQuery();
    }
    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var command = db.CreateCommand(); command.CommandText = sql;
        foreach (var arg in args) command.Parameters.AddWithValue(arg.Name, arg.Value ?? DBNull.Value);
        return command;
    }
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal Task SaveDialogsAsync(IEnumerable<ChatConversation> dialogs, CancellationToken token = default)
    {
        var snapshot = dialogs.ToArray();
        return Run(db =>
        {
            Execute(db, "BEGIN IMMEDIATE");
            foreach (var dialog in snapshot) Execute(db, "INSERT INTO dialogs VALUES($p,$d,$t) ON CONFLICT(peer) DO UPDATE SET data=$d,touched=$t",
                ("$p", dialog.Recipient.Id), ("$d", JsonSerializer.Serialize(dialog, ChatHistoryJson.Default.ChatConversation)), ("$t", dialog.LastMessage?.SentAt.ToUnixTimeMilliseconds() ?? Now));
            Execute(db, "DELETE FROM dialogs WHERE peer NOT IN (SELECT peer FROM dialogs ORDER BY touched DESC LIMIT 1000)");
            Execute(db, "COMMIT"); Maintain(db); return true;
        }, token);
    }
    internal Task<ChatConversation[]> LoadDialogsAsync(CancellationToken token = default) => Run(db =>
    {
        var result = new List<ChatConversation>();
        using var command = Command(db, "SELECT data FROM dialogs ORDER BY touched DESC LIMIT 500");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            try { if (JsonSerializer.Deserialize(reader.GetString(0), ChatHistoryJson.Default.ChatConversation) is { } dialog) result.Add(dialog); }
            catch (JsonException) { }
        }
        return result.OrderByDescending(c => c.LastMessage?.SentAt).ToArray();
    }, token);
    internal Task SavePageAsync(long account, long peer, ChatMessagePage page, string? requestedCursor = null, CancellationToken token = default) => Run(db =>
    {
        Execute(db, "BEGIN IMMEDIATE");
        {
            foreach (var message in page.Messages)
            {
                if (message.SenderId != account && message.SenderId != peer || message.Body.Length > 20000 || message.Id.Length > 512) continue;
                using var existing = Command(db, "SELECT 1 FROM messages WHERE peer=$p AND id=$i", ("$p", peer), ("$i", message.Id));
                var known = existing.ExecuteScalar() != null;
                Execute(db, "INSERT INTO messages VALUES($p,$i,$s,$b,$d,$t) ON CONFLICT(peer,id) DO UPDATE SET touched=$t",
                    ("$p", peer), ("$i", message.Id), ("$s", message.SenderId), ("$b", message.Body), ("$d", message.SentAt.ToUnixTimeMilliseconds()), ("$t", Now));
                var projection = ChatProtocol.Project(message, account, peer);
                if (!projection.IsReaction && !known)
                    Execute(db, "INSERT INTO message_search(body,peer,id) VALUES($b,$p,$i)", ("$b", projection.Text), ("$p", peer), ("$i", message.Id));
                else if (projection.IsReaction && Execute(db, "INSERT OR IGNORE INTO events VALUES($p,$event,$id)", ("$p", peer), ("$event", message.SenderId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + projection.EventId), ("$id", message.Id)) > 0)
                    Execute(db, """
                        INSERT INTO reactions VALUES($p,$target,$sender,$emoji,$stamp,$event)
                        ON CONFLICT(peer,target,sender) DO UPDATE SET emoji=$emoji,stamp=$stamp,event_id=$event
                        WHERE $stamp>reactions.stamp OR ($stamp=reactions.stamp AND $event>reactions.event_id)
                        """, ("$p", peer), ("$target", ChatProtocol.DecodeTarget(message.Body)), ("$sender", message.SenderId),
                        ("$emoji", projection.Reaction), ("$stamp", message.SentAt.ToUnixTimeMilliseconds()), ("$event", projection.EventId));
                var kind = ChatMediaReference.Parse(message.Body) != null ? "media" : ChatMusicLink.Find(message.Body).Length > 0 ? "music" : projection.Text.Contains("https://", StringComparison.OrdinalIgnoreCase) ? "link" : null;
                if (kind != null) Execute(db, "INSERT OR IGNORE INTO attachments VALUES($p,$i,$k,'')", ("$p", peer), ("$i", message.Id), ("$k", kind));
            }
            Execute(db, "INSERT INTO ranges VALUES($p,$c,$n,$f,$t) ON CONFLICT(peer,cursor) DO UPDATE SET next_cursor=$n,complete=$f,touched=$t",
                ("$p", peer), ("$c", requestedCursor ?? ""), ("$n", page.NextHref), ("$f", page.NextHref == null ? 1 : 0), ("$t", Now));
            Execute(db, "COMMIT");
        }
        Maintain(db); return true;
    }, token);
    internal Task<ChatHistoryWindow> LoadWindowAsync(long peer, string? anchor = null, int limit = 200, CancellationToken token = default) => Run(db =>
    {
        limit = Math.Clamp(limit, 1, 200);
        var result = new List<ChatMessage>();
        // Center on a saved anchor when it exists; otherwise recover the latest window.
        using var command = Command(db, anchor == null ? "SELECT id,sender,body,stamp FROM messages WHERE peer=$p ORDER BY stamp DESC,id DESC LIMIT $l" :
            "SELECT id,sender,body,stamp FROM messages WHERE peer=$p ORDER BY ABS(stamp-COALESCE((SELECT stamp FROM messages WHERE peer=$p AND id=$a),9223372036854775807)),stamp DESC LIMIT $l",
            ("$p", peer), ("$a", anchor), ("$l", limit));
        using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(ReadMessage(reader));
        if (result.Count > 0) Execute(db, "UPDATE messages SET touched=$t WHERE peer=$p AND stamp BETWEEN $a AND $b", ("$t", Now), ("$p", peer), ("$a", result.Min(m => m.SentAt.ToUnixTimeMilliseconds())), ("$b", result.Max(m => m.SentAt.ToUnixTimeMilliseconds())));
        string? cursor = ""; var complete = false; var visited = new HashSet<string>();
        while (cursor != null && visited.Add(cursor))
        {
            using var range = Command(db, "SELECT next_cursor,complete FROM ranges WHERE peer=$p AND cursor=$c", ("$p", peer), ("$c", cursor));
            using var coverage = range.ExecuteReader();
            if (!coverage.Read()) break;
            complete = coverage.GetInt32(1) == 1; cursor = coverage.IsDBNull(0) ? null : coverage.GetString(0);
        }
        return new ChatHistoryWindow(result.OrderBy(m => m.SentAt).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray(), cursor == "" ? null : cursor, complete);
    }, token);
    internal Task SaveMediaMetadataAsync(long peer, string id, MediaFile[] files, string caption, CancellationToken token = default) => Run(db =>
    {
        var safe = files.Select(f => f with { Video = null }).ToArray();
        Execute(db, "INSERT INTO media_metadata VALUES($p,$i,$d) ON CONFLICT(peer,id) DO UPDATE SET data=$d", ("$p", peer), ("$i", id), ("$d", JsonSerializer.Serialize(safe, ChatHistoryJson.Default.MediaFileArray)));
        Execute(db, "DELETE FROM attachments WHERE peer=$p AND id=$i", ("$p", peer), ("$i", id));
        foreach (var file in safe)
        {
            var kind = file.Type.StartsWith("image/") ? "photo" : file.Type.StartsWith("video/") ? "video" : file.Type.StartsWith("audio/") ? "music" : "file";
            Execute(db, "INSERT OR IGNORE INTO attachments VALUES($p,$i,$k,$n)", ("$p", peer), ("$i", id), ("$k", kind), ("$n", file.Name));
        }
        if (!string.IsNullOrWhiteSpace(caption))
        {
            Execute(db, "DELETE FROM message_search WHERE peer=$p AND id=$i", ("$p", peer), ("$i", id));
            Execute(db, "INSERT INTO message_search(body,peer,id) VALUES($b,$p,$i)", ("$b", caption), ("$p", peer), ("$i", id));
        }
        Maintain(db); return true;
    }, token);
    internal Task<Dictionary<string, MediaFile[]>> MediaMetadataAsync(long peer, CancellationToken token = default) => Run(db =>
    {
        var result = new Dictionary<string, MediaFile[]>();
        using var command = Command(db, "SELECT id,data FROM media_metadata WHERE peer=$p LIMIT 1000", ("$p", peer)); using var reader = command.ExecuteReader();
        while (reader.Read())
        { try { if (JsonSerializer.Deserialize(reader.GetString(1), ChatHistoryJson.Default.MediaFileArray) is { } files) result[reader.GetString(0)] = files; } catch (JsonException) { } }
        return result;
    }, token);
    internal Task<ChatMessage[]> MediaMessagesAsync(long peer, CancellationToken token = default, int offset = 0) => Run(db =>
    {
        using var command = Command(db, "SELECT m.id,m.sender,m.body,m.stamp FROM messages m WHERE m.peer=$p AND EXISTS(SELECT 1 FROM attachments a WHERE a.peer=m.peer AND a.id=m.id) ORDER BY m.stamp DESC LIMIT 50 OFFSET $o", ("$p", peer), ("$o", Math.Max(0, offset)));
        using var reader = command.ExecuteReader(); var result = new List<ChatMessage>(); while (reader.Read()) result.Add(ReadMessage(reader)); return result.ToArray();
    }, token);
    internal Task<ChatMessage[]> BeforeAsync(long peer, ChatMessage first, int limit = 100, CancellationToken token = default) => Run(db =>
    {
        using var command = Command(db, "SELECT id,sender,body,stamp FROM messages WHERE peer=$p AND (stamp<$s OR (stamp=$s AND id<$i)) ORDER BY stamp DESC,id DESC LIMIT $n", ("$p", peer), ("$s", first.SentAt.ToUnixTimeMilliseconds()), ("$i", first.Id), ("$n", Math.Clamp(limit, 1, 100)));
        using var reader = command.ExecuteReader(); var result = new List<ChatMessage>();
        while (reader.Read()) result.Add(ReadMessage(reader)); result.Reverse(); return result.ToArray();
    }, token);
    internal Task<ChatMessage[]> AfterAsync(long peer, ChatMessage last, int limit = 100, CancellationToken token = default) => Run(db =>
    {
        using var command = Command(db, "SELECT id,sender,body,stamp FROM messages WHERE peer=$p AND (stamp>$s OR (stamp=$s AND id>$i)) ORDER BY stamp,id LIMIT $n", ("$p", peer), ("$s", last.SentAt.ToUnixTimeMilliseconds()), ("$i", last.Id), ("$n", Math.Clamp(limit, 1, 100)));
        using var reader = command.ExecuteReader(); var result = new List<ChatMessage>();
        while (reader.Read()) result.Add(ReadMessage(reader)); return result.ToArray();
    }, token);
    internal Task<ChatSearchHit[]> SearchAsync(string query, long? peer = null, CancellationToken token = default) => Run(db =>
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(12).Select(w => "\"" + w.Replace("\"", "\"\"") + "\"*");
        var match = string.Join(" AND ", words);
        if (match.Length == 0) return [];
        using var command = Command(db, """
            SELECT m.peer,m.id,m.sender,m.body,m.stamp FROM message_search f JOIN messages m ON m.peer=f.peer AND m.id=f.id
            WHERE message_search MATCH $q AND ($p IS NULL OR m.peer=$p) ORDER BY m.stamp DESC LIMIT 100
            """, ("$q", match), ("$p", peer));
        using var reader = command.ExecuteReader(); var result = new List<ChatSearchHit>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), new(reader.GetString(1), reader.GetInt64(2), reader.GetString(3), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)))));
        return result.ToArray();
    }, token);
    internal Task<Dictionary<string, Dictionary<long, string>>> ReactionsAsync(long peer, CancellationToken token = default, IEnumerable<string>? targets = null)
    {
        var ids = targets?.Distinct().Take(200).ToArray();
        return Run(db =>
        {
            var result = new Dictionary<string, Dictionary<long, string>>();
            if (ids is { Length: 0 }) return result;
            var filter = ids == null ? "" : " AND target IN (" + string.Join(',', ids.Select((_, i) => "$i" + i)) + ")";
            using var command = Command(db, "SELECT target,sender,emoji FROM reactions WHERE peer=$p AND emoji IS NOT NULL" + filter, ("$p", peer));
            if (ids != null) for (var i = 0; i < ids.Length; i++) command.Parameters.AddWithValue("$i" + i, ids[i]);
            using var reader = command.ExecuteReader();
            while (reader.Read()) { var id = reader.GetString(0); if (!result.TryGetValue(id, out var authors)) result[id] = authors = []; authors[reader.GetInt64(1)] = reader.GetString(2); }
            return result;
        }, token);
    }
    internal Task<ChatMessage?> FindConfirmationAsync(long peer, long account, ChatPending pending, CancellationToken token = default) => Run(db =>
    {
        using var command = Command(db, "SELECT id,sender,body,stamp FROM messages WHERE peer=$p AND sender=$s AND body=$b AND stamp>=$d ORDER BY stamp DESC LIMIT 100", ("$p", peer), ("$s", account), ("$b", pending.Body), ("$d", pending.CreatedAt.AddSeconds(-5).ToUnixTimeMilliseconds()));
        using var reader = command.ExecuteReader(); while (reader.Read()) { var message = ReadMessage(reader); if (pending.BeforeIds?.Contains(message.Id) != true) return message; }
        return null;
    }, token);
    private static ChatMessage ReadMessage(SqliteDataReader reader) => new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)));
    internal Task<long> SizeAsync() => Run(db => { using var command = Command(db, "PRAGMA page_count"); var pages = (long)command.ExecuteScalar()!; using var size = Command(db, "PRAGMA page_size"); return pages * (long)size.ExecuteScalar()!; });
    internal Task ClearAsync() => Run(db => { Execute(db, "DELETE FROM messages; DELETE FROM message_search; DELETE FROM ranges; DELETE FROM media_metadata; DELETE FROM reactions; DELETE FROM events; DELETE FROM attachments; DELETE FROM dialogs; INSERT INTO message_search(message_search) VALUES('rebuild'); VACUUM; PRAGMA wal_checkpoint(TRUNCATE)"); return true; });
    private void Maintain(SqliteConnection db)
    {
        long Scalar(string sql) { using var command = Command(db, sql); return Convert.ToInt64(command.ExecuteScalar()); }
        var estimated = Scalar("SELECT COALESCE(SUM(length(body)*2+length(id)*2+256),0) FROM messages");
        var physical = Scalar("PRAGMA page_count") * Scalar("PRAGMA page_size");
        if (estimated < budget / 3 && physical < budget * .85) return;
        // Reclaim FTS pages as well as rows. Check actual allocated size, not just body lengths.
        for (var pass = 0; pass < 64; pass++)
        {
            Execute(db, "BEGIN IMMEDIATE");
            Execute(db, "CREATE TEMP TABLE IF NOT EXISTS evict(peer INTEGER,id TEXT); DELETE FROM evict");
            var total = Scalar("SELECT COUNT(*) FROM messages");
            var average = Math.Max(256, estimated / (double)Math.Max(1, total));
            var remove = Math.Clamp((long)Math.Ceiling((estimated - budget / 4) / average), 1, Math.Max(1, Math.Min(10000, total)));
            Execute(db, "INSERT INTO evict SELECT peer,id FROM messages ORDER BY touched,stamp LIMIT $n", ("$n", remove));
            var count = Scalar("SELECT COUNT(*) FROM evict");
            Execute(db, "DELETE FROM ranges WHERE peer IN (SELECT peer FROM evict); DELETE FROM events WHERE peer IN (SELECT peer FROM evict); DELETE FROM reactions WHERE peer IN (SELECT peer FROM evict); DELETE FROM media_metadata WHERE (peer,id) IN (SELECT peer,id FROM evict); DELETE FROM attachments WHERE (peer,id) IN (SELECT peer,id FROM evict); DELETE FROM message_search WHERE (peer,id) IN (SELECT peer,id FROM evict); DELETE FROM messages WHERE (peer,id) IN (SELECT peer,id FROM evict)");
            Execute(db, "COMMIT; INSERT INTO message_search(message_search) VALUES('rebuild'); VACUUM; PRAGMA wal_checkpoint(TRUNCATE)");
            physical = Scalar("PRAGMA page_count") * Scalar("PRAGMA page_size");
            estimated = Scalar("SELECT COALESCE(SUM(length(body)*2+length(id)*2+256),0) FROM messages");
            if (count == 0 && physical >= budget * .85)
            {
                Execute(db, "DELETE FROM dialogs WHERE peer IN (SELECT peer FROM dialogs ORDER BY touched LIMIT 100); DELETE FROM media_metadata WHERE NOT EXISTS(SELECT 1 FROM messages m WHERE m.peer=media_metadata.peer AND m.id=media_metadata.id); VACUUM; PRAGMA wal_checkpoint(TRUNCATE)");
            }
            if (estimated < budget / 3 && physical < budget * .85) break;
        }
    }
    public async ValueTask DisposeAsync() { work.Writer.TryComplete(); await worker.ConfigureAwait(false); }
}
[JsonSerializable(typeof(ChatConversation))]
[JsonSerializable(typeof(MediaFile[]))]
internal partial class ChatHistoryJson : JsonSerializerContext;
