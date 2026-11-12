using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool shareSending;

    private async Task SendShareMessageAsync()
    {
        if (shareSending || !shareSend.IsEnabled || shareRecipient is not { } recipient || me == null || api.Session == null) return;
        var account = me.Id; var session = api.Session; var generation = shareGeneration;
        var link = shareLink.Text; var item = shareTarget;
        var body = SharedMessageDraft("", shareComposeText.Text, link);
        var posted = false;
        ChatPending? pending = null;
        var token = lifetime.Token;
        bool CurrentAccount() => !disposed && me?.Id == account && api.Session == session;
        bool CurrentDialog() => CurrentAccount() && generation == shareGeneration && shareDialog.IsOpen && !shareClosing;
        shareSending = shareSendBusyHeld = true; RefreshShareCompose(); SetShareMessage("");
        try
        {
            await EnsureChatAccountAsync();
            if (!CurrentAccount() || chatAccountLifetime == null || chatLocalStore == null) return;
            token = chatAccountLifetime.Token;
            if (chatPending.ContainsKey(recipient.Id))
            {
                if (CurrentDialog()) SetShareMessage("Есть неподтверждённая отправка этому получателю. Проверь её в чатах перед повтором.");
                return;
            }
            var before = chatRecipient?.Id == recipient.Id ? chatMessages.Select(message => message.Id).TakeLast(64).ToArray()
                : chatConversations.FirstOrDefault(conversation => conversation.Recipient.Id == recipient.Id)?.LastMessage is { } last ? [last.Id] : Array.Empty<string>();
            pending = new(recipient.Id, body, ChatProtocol.Clip(body, 255), DateTimeOffset.UtcNow, true, before);
            chatPending[recipient.Id] = pending;
            RefreshChatComposer();
            // Use the same durable outbox as chats. Closing Share cannot cancel
            // a committed send, and a lost response never triggers a replay.
            await chatLocalStore.SaveAsync(new(chatDrafts.Where(pair => pair.Value.Length > 0).ToDictionary(),
                chatPending.Values.ToArray(), chatUnread.States.TakeLast(500).ToDictionary()), preferences.ChatSaveDrafts);
            token.ThrowIfCancellationRequested();
            if (!CurrentAccount()) return;
            if (item != null) CacheChatMusic(link, item);
            posted = true;
            var conversation = await api.SendMessageAsync(account, recipient.Id, body, token);
            if (!CurrentAccount() || token.IsCancellationRequested) return;
            if (conversation.Recipient.Id != recipient.Id || conversation.LastMessage is not { } confirmed ||
                confirmed.SenderId != account || confirmed.Body != body)
                throw new SoundCloudException("SoundCloud не подтвердил содержимое отправленного сообщения.");
            if (chatPending.GetValueOrDefault(recipient.Id) == pending) chatPending.Remove(recipient.Id);
            var index = chatConversations.FindIndex(row => row.Recipient.Id == recipient.Id);
            if (index >= 0) chatConversations[index] = conversation; else chatConversations.Insert(0, conversation);
            chatConversations.Sort((left, right) => Nullable.Compare(right.LastMessage?.SentAt, left.LastMessage?.SentAt));
            SaveChatState(); RenderChatInbox();
            if (chatRecipient?.Id == recipient.Id)
            {
                chatThreadRefreshRequested = true;
                if (page.Value == Page.Chats) Run(() => RefreshChatThreadAsync());
            }
            status.Value = "Сообщение отправлено.";
            if (CurrentDialog()) CloseShare();
        }
        catch (OperationCanceledException) when (!CurrentAccount() || token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!CurrentAccount()) return;
            var uncertain = posted && error is not SoundCloudException { StatusCode: >= 400 and < 500 and not 408 };
            if (!uncertain && pending != null && chatPending.GetValueOrDefault(recipient.Id) == pending)
            { chatPending.Remove(recipient.Id); SaveChatState(); }
            var message = uncertain ? "Отправка не подтверждена. Проверь переписку перед повтором — сообщение могло дойти."
                : FriendlyError(error);
            if (CurrentDialog()) SetShareMessage(message); else status.Value = message;
            Pookie.Logging.AppLog.Failure("Pookie.Share", "Не удалось подтвердить отправку сообщения", error);
        }
        finally
        {
            shareSending = false;
            if (!shareDialog.IsVisible || shareDialog.IsOpen && !shareClosing) shareSendBusyHeld = false;
            if (!disposed) { RefreshShareCompose(); RefreshChatComposer(); }
        }
    }
}
