using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pookie.SoundCloud;

namespace Pookie.App.Messages;

internal sealed record ChatReplyReference(long PeerId, string MessageId, string Preview);
internal sealed record ChatReactionEvent(string TargetId, string? Emoji);
internal sealed record ChatVoiceMetadata(double Duration, byte[] Peaks)
{
    [JsonIgnore] internal bool Valid => double.IsFinite(Duration) && Duration is > 0 and <= 300 && Peaks is { Length: > 0 and <= 64 };
}
internal sealed record ChatEnvelope(int Version, string Type, string EventId, long PeerId,
    string TargetId, string? Text = null, string? Preview = null, string? Emoji = null, long ParticipantId = 0)
{
    public override string ToString() => "Pookie message envelope (content redacted)";
}
internal sealed record ChatMessageProjection(ChatMessage Source, string Text, ChatReplyReference? Reply,
    bool IsReaction, string? Reaction, string? EventId, string? TargetId = null);

/// <summary>Only the transport's sender is authoritative. Invalid extensions remain readable text.</summary>
internal static class ChatProtocol
{
    private sealed record CachedProjection(long Account,long Peer,ChatMessageProjection Result);
    private sealed class ProjectionSlot { internal CachedProjection? Value; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ChatMessage,ProjectionSlot> Projections=new();
    internal static readonly string[] Reactions = ["👍", "❤️", "🔥", "😂", "😮", "👎"];
    internal const int MaxHostedContent = 200000;
    internal static string Reply(long account, long peer, string target, string preview, string text, bool hosted = false) =>
        Encode(new(2, "reply", Guid.NewGuid().ToString("N"), OtherPartyKey(account, peer), target, text, Clip(preview, 160), ParticipantId: Math.Max(account, peer)), hosted);
    internal static string React(long account, long peer, string target, string? emoji) =>
        Encode(new(2, "reaction", Guid.NewGuid().ToString("N"), OtherPartyKey(account, peer), target, Emoji: emoji, ParticipantId: Math.Max(account, peer)));
    // Stable across both participants; no envelope-supplied author or recipient is trusted.
    private static long OtherPartyKey(long account, long peer) => Math.Min(account, peer);
    internal static string Clip(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        if (maximum <= 0) return "";
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        var index = Array.BinarySearch(boundaries, maximum);
        var end = index >= 0 ? boundaries[index] : boundaries[Math.Max(0, ~index - 1)];
        return text[..end];
    }
    internal static string Encode(ChatEnvelope envelope, bool hosted = false)
    {
        var body = "p2:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(envelope, ChatProtocolJson.Default.ChatEnvelope))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (body.Length > (hosted ? MaxHostedContent : ChatRequest.MaxBodyLength)) throw new ArgumentException("Сообщение с ответом слишком длинное.");
        return body;
    }
    internal static ChatMessageProjection Project(ChatMessage message, long account, long peer)
    {
        // Immutable messages can share one decoded projection across layout,
        // previews and indexing. Weak keys do not retain old history windows.
        var slot=Projections.GetValue(message,static _=>new());
        var cached=Volatile.Read(ref slot.Value);
        if(cached?.Account==account && cached.Peer==peer)return cached.Result;
        var result=ProjectCore(message,account,peer);
        Volatile.Write(ref slot.Value,new(account,peer,result));return result;
    }
    private static ChatMessageProjection ProjectCore(ChatMessage message,long account,long peer)
    {
        var hosted = ChatEnvelopeHosting.IsReference(message.Body);
        var plain = new ChatMessageProjection(message, hosted ? "Сообщение Pookie" : message.Body, null, false, null, null);
        var body = hosted ? message.ClientContent : message.Body;
        if (message.SenderId != account && message.SenderId != peer || body == null || !body.StartsWith("p2:", StringComparison.Ordinal) ||
            body.Length > (hosted ? MaxHostedContent : ChatRequest.MaxBodyLength)) return plain;
        try
        {
            var raw = body[3..];
            if (raw.Length == 0 || raw.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return plain;
            raw = raw.Replace('-', '+').Replace('_', '/');
            var envelope = JsonSerializer.Deserialize(Convert.FromBase64String(raw.PadRight((raw.Length + 3) / 4 * 4, '=')), ChatProtocolJson.Default.ChatEnvelope);
            if (envelope is not { Version: 2, EventId.Length: 32, TargetId.Length: > 0 and <= 512 } ||
                !Guid.TryParseExact(envelope.EventId, "N", out var eventIdentity) || envelope.PeerId != OtherPartyKey(account, peer) || envelope.ParticipantId != Math.Max(account, peer)) return plain;
            if (envelope.Type == "reply" && envelope.Text is { Length: > 0 and <= 20000 } && envelope.Preview is { Length: <= 160 })
                return new(message, envelope.Text, new(peer, envelope.TargetId, envelope.Preview), false, null, eventIdentity.ToString("N"), envelope.TargetId);
            if (envelope.Type == "reaction" && envelope.Text == null && envelope.Preview == null &&
                (envelope.Emoji == null || Reactions.Contains(envelope.Emoji) || ChatEmojiCatalog.IsEmoji(envelope.Emoji)))
                return new(message, "Реакция " + (envelope.Emoji ?? "снята"), null, true, envelope.Emoji, eventIdentity.ToString("N"), envelope.TargetId);
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException) { }
        return plain;
    }
    internal static Dictionary<string, Dictionary<long, string>> Fold(IEnumerable<ChatMessage> messages, long account, long peer)
    {
        var result = new Dictionary<string, Dictionary<long, string>>(StringComparer.Ordinal);
        var events = new HashSet<(long, string)>();
        foreach (var message in messages.OrderBy(m => m.SentAt).ThenBy(m => m.Id, StringComparer.Ordinal))
        {
            var projection = Project(message, account, peer);
            if (!projection.IsReaction || !events.Add((message.SenderId, projection.EventId!))) continue;
            var target = projection.TargetId!;
            if (!result.TryGetValue(target, out var participants)) result[target] = participants = [];
            if (projection.Reaction == null) participants.Remove(message.SenderId);
            else participants[message.SenderId] = projection.Reaction;
        }
        return result;
    }
}
[JsonSerializable(typeof(ChatEnvelope))]
internal partial class ChatProtocolJson : JsonSerializerContext;
