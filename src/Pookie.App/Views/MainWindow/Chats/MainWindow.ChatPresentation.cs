using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool chatPresentationDirty = true;
    private (Rect Bounds, double Offset, double Maximum, int Loads) chatPresentationGeometry;
    private sealed class ChatMessageItem(ChatMessage message)
    {
        internal readonly string Key = message.Id;
        internal ChatMessage Message = message;
        internal string Id => Message.Id;
        internal string Body => Message.Body;
        internal DateTimeOffset SentAt => Message.SentAt;
    }
    private enum ChatDelivery { Sending, Sent, Failed }
    private sealed class ChatOutgoingItem(ChatMessage message, string draft, string[] files, KlipyGif? gif)
    {
        internal ChatMessage Message = message;
        internal string Draft = draft;
        internal string[] Files = files;
        internal KlipyGif? Gif = gif;
        internal ChatDelivery State = ChatDelivery.Sending;
        internal string? Error;
        internal IImageSource? Thumbnail;
        internal Size? ImageSize;
        internal HashSet<string> BeforeSendIds = [];
        internal DateTimeOffset DisplayOrder;
        internal bool HasMedia => Files.Length > 0 || Gif != null || ChatMediaReference.Parse(Message.Body) != null;
        internal HashSet<string> Documents = [];
    }
    private readonly Dictionary<long, ChatOutgoingItem> chatOutgoing = [];
    private readonly HashSet<string> chatMessageEntrances = [];
    private readonly Dictionary<string, DateTimeOffset> chatMessageOrder = [];
    private DateTimeOffset ChatOrder(ChatMessage message) => chatMessageOrder.GetValueOrDefault(message.Id, message.SentAt);
    private sealed record ChatInboxVisual(TextBlock Time, TextBlock Checks, Image Thumbnail, Border ThumbnailFrame, Image PreviewIcon, TextBlock Preview, Border Unread, TextBlock UnreadText)
    { internal long AvatarPeer; internal string? AvatarUrl, PreviewGlyph; }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, ChatInboxVisual> chatInboxVisuals = new();
    private ChatOutgoingItem? ChatOutgoing(ChatMessage? message) => message == null ? null : chatOutgoing.Values.FirstOrDefault(x => x.Message.Id == message.Id);
    private bool ChatStartsDay(ChatMessage message)
    {
        var index = chatMessageItems.ToList().FindIndex(item => item.Id == message.Id);
        return index <= 0 || chatMessageItems[index - 1].SentAt.ToLocalTime().Date != message.SentAt.ToLocalTime().Date;
    }
    private void RefreshChatTimeline()
    {
        chatPresentationDirty = true;
        var rows = chatMessages.Where(m=>!ChatProtocol.Project(m,chatAccountId,chatRecipient?.Id??0).IsReaction).OrderBy(ChatOrder).DistinctBy(m=>(m.SenderId,ChatProtocol.Project(m,chatAccountId,chatRecipient?.Id??0).EventId??m.Id)).ToList();
        if (chatRecipient != null && chatOutgoing.TryGetValue(chatRecipient.Id, out var outgoing)&&!ChatProtocol.Project(outgoing.Message,chatAccountId,chatRecipient.Id).IsReaction) rows.Add(outgoing.Message);
        var current = chatMessageItems.ToDictionary(item => item.Id);
        var desired = rows.Select(message =>
        {
            if (!current.TryGetValue(message.Id, out var item)) return new ChatMessageItem(message);
            item.Message = message; return item;
        }).ToArray();
        PatchChatItems(chatMessageItems, desired, item => item.Key);
        foreach (var row in ChatRows)
        {
            if (row.Bound && row.Message != null && desired.FirstOrDefault(item => item.Key == row.ItemKey) is { } item) { row.Bind(item.Message); row.ItemKey = item.Key; }
            else { row.RefreshDelivery(); row.RefreshDate(); }
            if (row.Bound && row.Message is { } message && chatRecipient != null && ChatOutgoing(message) == null && ChatMediaReference.Parse(message.Body) != null && CachedChatMedia(message) == null &&
                !chatMediaAutoRequested.Contains(ChatMediaCacheId(message, chatRecipient.Id)))
                QueueChatMediaPreview(message, chatRecipient.Id);
        }
        foreach (var binding in chatInboxBindings) RefreshChatInboxPreview(binding.Key, binding.Value);
        RefreshChatLoading();
    }
    private void RestoreChatOutbox()
    {
        foreach (var pending in chatPending.Values)
            chatOutgoing[pending.RecipientId] = new(new ChatMessage($"pending:{pending.RecipientId}:{pending.CreatedAt.UtcTicks}", chatAccountId, pending.Body, pending.CreatedAt){ClientContent=pending.ClientContent},
                chatDrafts.GetValueOrDefault(pending.RecipientId, pending.Body), [], null)
            { State = ChatDelivery.Failed, Error = "Отправка не подтверждена. Нажми, чтобы проверить.", BeforeSendIds = pending.BeforeIds?.ToHashSet() ?? [] };
    }
    private void RefreshChatInboxPreview(Button button, ChatConversation conversation)
    {
        if (!chatInboxVisuals.TryGetValue(button, out var view)) return;
        view.UnreadText.Text = chatUnread.Badge(conversation.Recipient.Id);
        view.Unread.IsVisible = view.UnreadText.Text.Length > 0;
        var local = chatOutgoing.GetValueOrDefault(conversation.Recipient.Id);
        var last = local?.Message ?? conversation.LastMessage;
        view.Time.Text = last == null ? "" : ChatPresentation.InboxTime(last.SentAt, DateTimeOffset.Now);
        view.Checks.IsVisible = last?.SenderId == me?.Id;
        view.Checks.Text = local?.State switch { ChatDelivery.Sending => "◷", ChatDelivery.Failed => "!", _ => "✓" };
        view.Checks.Foreground = local?.State == ChatDelivery.Failed ? Color.FromRgb(255, 132, 132) : ChatAccent;
        view.Thumbnail.Source = null; view.ThumbnailFrame.IsVisible = false;
        view.PreviewIcon.IsVisible = false; view.PreviewIcon.Source = null; view.PreviewGlyph = null;
        view.Preview.Foreground = Muted;
        if (chatDrafts.GetValueOrDefault(conversation.Recipient.Id) is {Length:>0} draft && local==null)
        {view.Preview.Text="Черновик: "+ChatPreview(draft);view.Preview.Foreground=ChatAccent;return;}
        if (last != null && ChatProtocol.Project(last,chatAccountId,conversation.Recipient.Id) is {EventId:not null} projection)
        {view.Preview.Text=ChatPreview(projection.Text);return;}
        if (last == null) { view.Preview.Text = "Начать переписку"; return; }
        if (ChatMediaReference.Parse(last.Body) != null || local?.HasMedia == true)
        {
            var summary = ChatMediaSummaryFor(last, conversation.Recipient.Id);
            var caption = ChatPresentation.Preview(local?.Draft ?? summary?.Caption ?? "");
            if (ChatMediaReference.Parse(caption) != null) caption = "";
            view.Preview.Text = caption.Length > 0 ? caption : summary?.Label ?? (local?.Gif != null ? "GIF" : local?.Files.FirstOrDefault() is { } path && ChatMediaType(path).StartsWith("image/", StringComparison.Ordinal) ? "Фотография" : "Вложение");
            view.Preview.Foreground = caption.Length > 0 ? Muted : ChatAccent;
            view.Thumbnail.Source = summary?.Thumbnail ?? local?.Thumbnail;
            var files = summary?.Files ?? local?.Files.Select(path => new MediaFile(Path.GetFileName(path), ChatMediaType(path), 0, local.Documents.Contains(path))).ToArray();
            var document = files?.Any(f => f.AsFile || !f.Type.StartsWith("image/", StringComparison.Ordinal) && !f.Type.StartsWith("video/", StringComparison.Ordinal)) == true;
            if (document)
            {
                var music = files!.All(f => !f.AsFile && f.Type.StartsWith("audio/", StringComparison.Ordinal));
                SetChatInboxGlyph(view, music ? "music-notes" : "file"); view.Thumbnail.Source = null;
            }
            view.ThumbnailFrame.IsVisible = view.Thumbnail.Source != null;
            if (summary == null && local == null) QueueChatMediaPreview(last, conversation.Recipient.Id);
        }
        else if (ChatMusicLink.Find(last.Body) is { Length: > 0 } links)
        {
            var caption = ChatPresentation.Preview(ChatMusicLink.Caption(last.Body, links));
            foreach (var (start, link) in ChatMusicLink.Occurrences(caption, links).Reverse())
                if (chatMusicCache.TryGetValue(link, out var item)) caption = caption.Remove(start, link.Length).Insert(start, item.Title);
            view.Preview.Text = caption.Length > 0 ? caption : chatMusicCache.GetValueOrDefault(links[0])?.Title ?? "Трек или плейлист";
            if (caption.Length == 0) view.Preview.Foreground = ChatAccent;
            SetChatInboxGlyph(view, "music-notes");
            QueueChatMusicSummary(links[0]);
        }
        else view.Preview.Text = ChatPreview(last.Body);
    }
    private static void SetChatInboxGlyph(ChatInboxVisual view, string glyph)
    {
        view.PreviewGlyph = glyph; view.PreviewIcon.Source = Icons.Source(glyph, view.Preview.Foreground);
        view.PreviewIcon.IsVisible = true;
    }
    private void RefreshChatLoading()
    {
        chatInboxLoading.SetLoading(chatsBusy && !chatListLoaded && chatConversations.Count == 0 && !chatSearchMode);
        chatThreadLoading.SetLoading(chatThreadBusy && !chatThreadLoaded && chatMessages.Count == 0 && chatOutgoing.GetValueOrDefault(chatRecipient?.Id ?? 0) == null);
    }
    private void RefreshChatDateOverlay()
    {
        if (chatRecipient == null || chatMessageItems.Count == 0 || chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer viewer)
        { chatDateLayer.ResetOverlay(); return; }
        var top = viewer.Bounds.Y;
        var rows = ChatRows.Where(r => r.Bound && r.Message != null && r.Root.ActualHeight > 0).OrderBy(r => r.Root.Bounds.Y).ToArray();
        foreach (var row in rows) row.DateBadge.Opacity = 1;
        var visible = rows.FirstOrDefault(r => r.Root.Bounds.Bottom > top + 30 && r.Root.Bounds.Y < viewer.Bounds.Bottom);
        if (visible?.Message == null) { chatDateLayer.ResetOverlay(); return; }
        var day = visible.Message.SentAt.ToLocalTime().Date;
        var original = rows.FirstOrDefault(r => r.Message!.SentAt.ToLocalTime().Date == day && r.DateBadge.IsVisible);
        var pinned = viewer.VerticalOffset > 24 && (original == null || original.DateBadge.Bounds.Y < top + 8);
        var y = original != null && !pinned ? original.DateBadge.Bounds.Y - chatDateLayer.Bounds.Y : top - chatDateLayer.Bounds.Y + 8;
        var next = rows.FirstOrDefault(r => r.DateBadge.IsVisible && r.Message!.SentAt.ToLocalTime().Date > day && r.DateBadge.Bounds.Y > top);
        if (next != null) y = Math.Min(y, next.DateBadge.Bounds.Y - chatDateLayer.Bounds.Y - Math.Max(26, chatFloatingDate.DesiredSize.Height) - 6);
        chatFloatingDateText.Text = ChatPresentation.Day(visible.Message.SentAt);
        chatDateLayer.Place(visible.Root.Bounds.X + visible.Root.ActualWidth / 2 - chatDateLayer.Bounds.X, y, pinned);
        if (original != null && chatFloatingDate.IsVisible && original.DateBadge.Bounds.Bottom > top)
            original.DateBadge.Opacity = 1 - chatFloatingDate.Opacity;
    }
    private async Task RetryChatOutgoingAsync()
    {
        if (chatSending || chatThreadBusy || chatRecipient == null || !chatOutgoing.TryGetValue(chatRecipient.Id, out var outgoing)) return;
        if (chatPending.ContainsKey(chatRecipient.Id)) { await CheckPendingChatAsync(); return; }
        chatRenderingDraft = true; chatInput.Text = outgoing.Draft; chatRenderingDraft = false;
        if (preferences.ChatSaveDrafts) chatDrafts[chatRecipient.Id] = outgoing.Draft;
        chatSelectedFiles = outgoing.Files; chatSelectedGif = outgoing.Gif;
        chatDocumentFiles.Clear(); chatDocumentFiles.UnionWith(outgoing.Documents); UpdateChatAttachments();
        RefreshChatComposer(); await SendChatAsync();
    }
}
