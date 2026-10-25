using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Grid chatReplyBar = null!;
    private ChatQuoteView chatReplyQuote = null!;
    private Button chatReplyCancel = null!;
    private readonly Dictionary<(long Peer,string Id),Task<ChatMessage?>> chatReplyLookups = [];

    private FrameworkElement ChatReplyView()
    {
        chatReplyQuote = new ChatQuoteView(ChatAccent,composer:true);
        var quote = TrackButtons.Action(chatReplyQuote, () =>
        { if (chatReply is { } reply) Run(() => JumpChatMessageAsync(reply.PeerId,reply.MessageId,keepDetails:true)); },TrackButtons.Text,40).Padding(0).CornerRadius(8);
        chatReplyQuote.HorizontalAlignment = HorizontalAlignment.Stretch;
        chatReplyCancel = TrackButtons.Icon(Icons.View("x-mark",18,ChatAccent), () =>
        { chatReply = null; RefreshChatReply(); SaveChatDraft(); chatInput.Focus(); },TrackButtons.Text,36).ToolTip("Отменить ответ");
        chatReplyBar = new Grid().Columns("40,*,40").Rows("40").Spacing(4).Padding(0,4).Height(48).Children(
            Icons.View("arrow-bend-up-left",22,ChatAccent).Center().Column(0),quote.Column(1),chatReplyCancel.Center().Column(2)).IsVisible(false);
        return chatReplyBar;
    }
    private void ReplyToChat(ChatMessage message)
    {
        if (chatRecipient == null || ChatOutgoing(message) != null) return;
        chatReply = new(chatRecipient.Id,message.Id,ChatProtocol.Clip(ChatReplyPreview(message,chatRecipient.Id),160));
        RefreshChatReply(); SaveChatDraft(); chatInput.Focus();
    }
    private void RefreshChatReply()
    {
        if (chatReplyQuote == null || chatComposerLayout == null) return;
        if (chatReply is { } reply) BindChatQuote(chatReplyQuote,reply,composer:true);
        else chatReplyQuote.BeginBinding();
        if (chatComposerLayout.SetReplyOpen(chatReply != null)) chatComposerSurface.AnimateReplyChange();
    }
    private string ChatReplyPreview(ChatMessage message,long peer)
    {
        if (ChatMediaReference.Parse(message.Body) != null)
        {
            var summary = ChatMediaSummaryFor(message,peer);
            return ChatPresentation.Preview(!string.IsNullOrWhiteSpace(summary?.Caption) ? summary.Caption : summary?.Label ?? "Вложение");
        }
        return ChatPresentation.Preview(ChatProtocol.Project(message,chatAccountId,peer).Text);
    }
    private void BindChatQuote(ChatQuoteView quote,ChatReplyReference reference,bool composer = false)
    {
        var version = quote.BeginBinding(); var account = chatAccountId; var revision = chatRevision;
        var original = chatRecipient?.Id == reference.PeerId ? chatMessages.FirstOrDefault(m => m.Id == reference.MessageId) : null;
        original ??= chatWorkspace.Favorites.FirstOrDefault(f => f.PeerId == reference.PeerId && f.Message.Id == reference.MessageId)?.Message;
        void Apply(ChatMessage? message)
        {
            var author = message == null ? "Сообщение" : message.SenderId == account ? me?.Username ?? "Вы" :
                chatConversations.FirstOrDefault(c => c.Recipient.Id == reference.PeerId)?.Recipient.Username ?? chatRecipient?.Username ?? "Собеседник";
            var summary = message == null ? null : ChatMediaSummaryFor(message,reference.PeerId);
            var preview = message == null ? reference.Preview : ChatReplyPreview(message,reference.PeerId);
            quote.Set(composer ? message == null ? "Ответ на сообщение" : "Ответ для "+author : author,
                string.IsNullOrWhiteSpace(preview) ? "Сообщение" : preview,summary?.Thumbnail);
        }
        if (original != null) { Apply(original); return; }
        var store = chatHistory;
        if (store == null) { Apply(null); return; }
        var key = (reference.PeerId,reference.MessageId);
        if (!chatReplyLookups.TryGetValue(key,out var lookup))
        {
            lookup = store.FindMessageAsync(key.PeerId,key.MessageId,ChatReadToken());
            chatReplyLookups[key] = lookup;
            while (chatReplyLookups.Count > 128) chatReplyLookups.Remove(chatReplyLookups.Keys.First());
        }
        if (lookup.IsCompletedSuccessfully) { Apply(lookup.Result); return; }
        Apply(null);
        Run(async () =>
        {
            try
            {
                var loaded = await lookup;
                if (!disposed && account == chatAccountId && revision == chatRevision && store == chatHistory && quote.BindingVersion == version) Apply(loaded);
            }
            catch (OperationCanceledException) { if (chatReplyLookups.GetValueOrDefault(key) == lookup) chatReplyLookups.Remove(key); }
            catch { if (chatReplyLookups.GetValueOrDefault(key) == lookup) chatReplyLookups.Remove(key); }
        });
    }
}
