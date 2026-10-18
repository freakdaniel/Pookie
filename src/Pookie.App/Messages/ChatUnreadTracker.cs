using Pookie.SoundCloud;

namespace Pookie.App.Messages;

internal sealed record ChatUnreadState(string LastId, DateTimeOffset LastAt, int Count, bool AtLeast = false);

// SoundCloud exposes a read flag, not a per-message unread count. Preserve the
// observed watermark so several incoming messages between polls are counted once.
internal sealed class ChatUnreadTracker
{
    internal Dictionary<long, ChatUnreadState> States { get; } = [];
    internal string Badge(long peer) => States.GetValueOrDefault(peer) is { Count: > 0 } state
        ? state.Count >= 100 ? "99+" : state.Count + (state.AtLeast ? "+" : "") : "";
    internal bool NeedsMessages(ChatConversation conversation) => !conversation.Read && conversation.LastMessage is { } last &&
        States.GetValueOrDefault(conversation.Recipient.Id)?.LastId != last.Id;
    internal bool Observe(ChatConversation conversation, long account, ChatMessage[]? recent = null)
    {
        if (conversation.LastMessage is not { } last) return false;
        var peer = conversation.Recipient.Id;
        var previous = States.GetValueOrDefault(peer);
        if (conversation.Read)
        {
            var read = new ChatUnreadState(last.Id, last.SentAt, 0);
            if (read == previous) return false;
            States[peer] = read; return true;
        }
        if (previous?.LastId == last.Id) return false;
        var messages = recent ?? [last];
        var anchor = previous == null ? -1 : Array.FindIndex(messages, m => m.Id == previous.LastId);
        var fresh = previous == null ? [] : anchor >= 0 ? messages.Skip(anchor + 1) : messages.Where(m => m.SentAt > previous.LastAt);
        var count = (previous?.Count ?? 0) + fresh.Count(m => m.SenderId != account);
        var uncertain = previous == null || previous.AtLeast || anchor < 0 && previous != null && messages.Length >= 30;
        States[peer] = new(last.Id, last.SentAt, Math.Clamp(Math.Max(count, 1), 0, 9999), uncertain);
        return true;
    }
    internal void Clear(long peer)
    { if (States.TryGetValue(peer, out var state)) States[peer] = state with { Count = 0, AtLeast = false }; }
}
