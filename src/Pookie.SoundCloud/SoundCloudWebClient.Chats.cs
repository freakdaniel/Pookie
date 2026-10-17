using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed record ChatMessage(string Id, long SenderId, string Body, DateTimeOffset SentAt)
{
    public override string ToString() => "SoundCloud message (content redacted)";
}
public sealed record ChatConversation(string Id, SoundCloudUser Recipient, ChatMessage? LastMessage, bool Read);
public sealed record ChatConversationPage(ChatConversation[] Conversations, string? NextHref);
public sealed record ChatMessagePage(ChatMessage[] Messages, string? NextHref);

public sealed partial class SoundCloudWebClient
{
    public async Task<ChatConversationPage> GetConversationsAsync(long userId, string? cursor = null, CancellationToken token = default)
    {
        using var json = await RequestChatAsync(new("list", userId, Cursor: cursor), token);
        return ParseConversations(json.RootElement, userId);
    }

    public async Task<ChatMessagePage> GetMessagesAsync(long userId, long recipientId, string? cursor = null, CancellationToken token = default)
    {
        using var json = await RequestChatAsync(new("thread", userId, recipientId, Cursor: cursor), token);
        return ParseMessages(json.RootElement, userId, recipientId);
    }

    public async Task<ChatConversation> SendMessageAsync(long userId, long recipientId, string body, CancellationToken token = default)
    {
        using var json = await RequestChatAsync(new("send", userId, recipientId, body), token);
        return ParseConversation(json.RootElement, userId) ?? throw new SoundCloudException("SoundCloud не подтвердил отправку сообщения. Обнови переписку перед повтором.");
    }

    public async Task MarkConversationReadAsync(long userId, long recipientId, CancellationToken token = default)
    { using var json = await RequestChatAsync(new("read", userId, recipientId), token); }

    private Task<JsonDocument> RequestChatAsync(ChatRequest request, CancellationToken token)
    {
        if (!request.IsValid()) throw new ArgumentException("Некорректный запрос переписки.");
        if (Session?.IsValid() != true) throw new SoundCloudException("Войди в SoundCloud, чтобы открыть чаты.");
        return (BrowserTransport ?? throw new SoundCloudException("Соединение с SoundCloud ещё не готово.")).RequestChatAsync(request, token);
    }

    public static ChatConversationPage ParseConversations(JsonElement root, long userId)
    {
        var rows = ChatRows(root).Select(row => ParseConversation(row, userId)).OfType<ChatConversation>()
            .DistinctBy(c => c.Id).OrderByDescending(c => c.LastMessage?.SentAt).ToArray();
        var next = ChatText(root, "next_href");
        return new(rows, next != null && ChatRequest.IsCursor(next, userId, 0) ? next : null);
    }

    public static ChatMessagePage ParseMessages(JsonElement root, long userId, long recipientId)
    {
        var messages = ChatRows(root).Select(ParseChatMessage).OfType<ChatMessage>()
            .Where(m => m.SenderId == userId || m.SenderId == recipientId).DistinctBy(m => m.Id)
            .OrderBy(m => m.SentAt).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
        var next = ChatText(root, "next_href");
        return new(messages, next != null && ChatRequest.IsCursor(next, userId, recipientId) ? next : null);
    }

    private static ChatConversation? ParseConversation(JsonElement root, long userId)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array) return null;
        var parsed = users.EnumerateArray().Select(row =>
        {
            try { return row.Deserialize(SoundCloudJson.Default.SoundCloudUser); }
            catch (JsonException) { return null; }
        }).OfType<SoundCloudUser>().ToArray();
        // Never accept another account's conversation, even from a stale browser response.
        if (!parsed.Any(u => u.Id == userId)) return null;
        var recipient = parsed.FirstOrDefault(u => u.Id > 0 && u.Id != userId);
        if (recipient == null) return null;
        var last = root.TryGetProperty("last_message", out var message) ? ParseChatMessage(message) : null;
        if (last != null && last.SenderId != userId && last.SenderId != recipient.Id) last = null;
        return new(ChatText(root, "id") ?? $"{Math.Min(userId, recipient.Id)}:{Math.Max(userId, recipient.Id)}", recipient, last,
            root.TryGetProperty("read", out var read) && read.ValueKind == JsonValueKind.True);
    }

    private static ChatMessage? ParseChatMessage(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || ChatText(row, "content") is not { } body || body.Length > ChatRequest.MaxBodyLength) return null;
        var urn = ChatText(row, "sender_urn") ?? "";
        var sender = long.TryParse(urn.AsSpan(urn.LastIndexOf(':') + 1), out var id) ? id : 0;
        if (sender <= 0 && row.TryGetProperty("sender", out var user) && user.ValueKind == JsonValueKind.Object &&
            user.TryGetProperty("id", out var senderId) && senderId.ValueKind == JsonValueKind.Number && senderId.TryGetInt64(out id)) sender = id;
        var sent = ChatText(row, "sent_at");
        if (sender <= 0 || !DateTimeOffset.TryParse(sent, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return null;
        var key = ChatText(row, "id") ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{sender}\n{date.UtcTicks}\n{body}"))));
        return new(key, sender, body, date);
    }

    private static IEnumerable<JsonElement> ChatRows(JsonElement root) => root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() :
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("collection", out var rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : [];
    private static string? ChatText(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null : null;
}
