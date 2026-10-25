using Aprillz.MewUI;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task ReactToChatAsync(ChatMessage message, string emoji)
    {
        if (ChatOutgoing(message) != null || chatSending || chatRecipient == null || me == null || api.Session == null || chatPending.ContainsKey(chatRecipient.Id)) return;
        var peer = chatRecipient; var account = me.Id; var token = chatAccountLifetime!.Token;
        var previous = chatReactions.GetValueOrDefault(message.Id)?.GetValueOrDefault(account);
        var body = ChatProtocol.React(account, peer.Id, message.Id, previous == emoji ? null : emoji);
        var pending = new ChatPending(peer.Id, body, "Реакция", DateTimeOffset.UtcNow, true, chatMessages.Select(m => m.Id).TakeLast(64).ToArray());
        chatSending = true; RefreshChatComposer(); var committed = false;
        try
        {
            chatPending[peer.Id] = pending;
            try { chatLocalStore!.Save(new(chatDrafts.Where(p => p.Value.Length > 0).TakeLast(50).ToDictionary(), chatPending.Values.ToArray(), chatUnread.States.TakeLast(500).ToDictionary()), preferences.ChatSaveDrafts); committed = true; }
            catch { chatPending.Remove(peer.Id); throw; }
            // Same durable confirmation/reconciliation path as text and media; never auto-retry POST.
            await SendPendingChatAsync(pending, account, peer, token);
        }
        catch (OperationCanceledException) when (!ChatWriteCurrent(account, token)) { }
        catch (Exception)
        {
            if (ChatWriteCurrent(account, token))
            { if (committed) RestoreChatOutbox(); chatThreadStatus.Value = committed ? "Реакция не подтверждена. Проверь отправку в меню диалога." : "Не удалось сохранить реакцию перед отправкой."; RefreshChatTimeline(); }
        }
        finally { if (ChatWriteCurrent(account, token)) { chatSending = false; RefreshChatComposer(); } }
    }
    private (bool Previous, bool Next) ChatMessageGroup(ChatMessage message)
    {
        var index = chatMessageItems.ToList().FindIndex(m => m.Id == message.Id);
        static bool Connected(ChatMessage a, ChatMessage b) => a.SenderId == b.SenderId &&
            a.SentAt.ToLocalTime().Date == b.SentAt.ToLocalTime().Date && Math.Abs((b.SentAt - a.SentAt).TotalSeconds) <= 120;
        return (index > 0 && Connected(chatMessageItems[index - 1].Message, message), index >= 0 && index + 1 < chatMessageItems.Count && Connected(message, chatMessageItems[index + 1].Message));
    }
}
