using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ChatHistoryStore? chatHistory;
    private ChatWorkspaceStore? chatWorkspaceStore;
    private ChatWorkspaceState chatWorkspace = ChatWorkspaceStore.Empty;
    private bool chatDiskRestored, chatWindowHasNewer, chatWindowBusy;
    private string? chatOlderCacheBoundary;
    private DateTimeOffset chatOlderRetryAfter;
    private Dictionary<string, Dictionary<long, string>> chatReactions = [];
    private void RefreshChatFavoriteMedia()
    {
        if (chatMedia.DiskCache is { } cache)
            Run(() => cache.SetPinnedAsync(chatWorkspace.Favorites.Select(f => ChatEnvelopeHosting.StorageCode(f.Message.Body)).Where(body => ChatMediaReference.Parse(body) != null).Distinct()));
    }
    private void SaveChatWorkspace()
    {
        if (!chatAccountReady || chatWorkspaceStore == null) return;
        var store = chatWorkspaceStore;
        var save = store.SaveAsync(preferences.ChatSaveDrafts ? chatWorkspace : chatWorkspace with { Drafts = null });
        Run(async () =>
        {
            try { await save; }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            { if (!disposed && chatWorkspaceStore == store) chatThreadStatus.Value = "Не удалось сохранить локальные данные чатов."; }
        });
    }
    private async Task RestoreChatHistoryAsync()
    {
        if (chatDiskRestored || chatHistory == null) return;
        chatDiskRestored = true;
        var account = chatAccountId; var store = chatHistory; var token = chatAccountLifetime!.Token;
        try
        {
            var dialogs = await store.LoadDialogsAsync(token);
            if (chatHistory != store || chatAccountId != account) return;
            chatConversations.AddRange(dialogs.Where(d => !chatConversations.Any(c => c.Recipient.Id == d.Recipient.Id)));
            RenderChatInbox();
            if (chatWorkspace.SavedOpen) { await OpenChatFavoritesAsync(); return; }
            if (chatRecipient == null && chatWorkspace.LastPeer is { } peer && chatConversations.FirstOrDefault(c => c.Recipient.Id == peer) is { } last)
            {
                var position = chatWorkspace.Positions.GetValueOrDefault(peer);
                var window = await store.LoadWindowAsync(peer, position is { Bottom: false } ? position.MessageId : null, token: token);
                var reactions = await store.ReactionsAsync(peer, token, window.Messages.Select(m => m.Id));
                var newer = window.Messages.Length > 0 && (await store.AfterAsync(peer, window.Messages[^1], 1, token)).Length > 0;
                if (chatHistory != store || chatAccountId != account) return;
                if (window.Messages.Length > 0) chatThreadCache[peer] = new(window.Messages, [], window.Cursor, window.Cursor, 0, position?.Bottom ?? true, position?.MessageId, position?.Y ?? 0, reactions, newer);
                await SelectChatAsync(last.Recipient);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or InvalidOperationException)
        { if (chatHistory == store) { chatHistory = null; _ = store.DisposeAsync(); } chatsStatus.Value = "Кеш истории недоступен. Переписка загрузится из SoundCloud."; }
    }
    private async Task RestoreChatWindowAsync(SoundCloudUser peer)
    {
        if (chatHistory == null) return;
        var store = chatHistory; var revision = chatRevision; var token = ChatReadToken();
        try
        {
            var position = chatWorkspace.Positions.GetValueOrDefault(peer.Id);
            var window = await store.LoadWindowAsync(peer.Id, position is { Bottom: false } ? position.MessageId : null, token: token);
            var reactions = await store.ReactionsAsync(peer.Id, token, window.Messages.Select(m => m.Id));
            if (chatHistory != store || chatRevision != revision || chatRecipient?.Id != peer.Id) return;
            chatReactions = reactions;
            if (window.Messages.Length == 0) return;
            chatThreadLoaded = true; chatMessages.AddRange(window.Messages);
            var newer = (await store.AfterAsync(peer.Id, window.Messages[^1], 1, token)).Length > 0;
            if (chatRevision != revision) return;
            chatWindowHasNewer = newer;
            chatMessageCursor = window.Cursor; chatLatestCursor = window.Cursor;
            if (position is { Bottom: false, MessageId: not null })
            { chatRestoreAnchor = position.MessageId; chatRestoreAnchorY = position.Y; chatRestoreOffset = 0; }
            else RequestChatBottom();
        }
        catch (OperationCanceledException) { }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
    }
    private async Task CacheChatPageAsync(long account, long peer, ChatMessagePage result, string? cursor, CancellationToken token)
    {
        if (chatHistory is not { } store) return;
        try
        {
            await store.SavePageAsync(account, peer, result, cursor, token);
            if (chatHistory == store && chatRecipient?.Id == peer) chatOlderCacheBoundary = null;
            var reactions = await store.ReactionsAsync(peer, token, chatMessages.Concat(result.Messages).Where(m => !ChatProtocol.Project(m, account, peer).IsReaction).Select(m => m.Id).Distinct().TakeLast(200));
            if (chatHistory == store && chatRecipient?.Id == peer) chatReactions = reactions;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or InvalidOperationException)
        { if (chatHistory == store) { chatHistory = null; _ = store.DisposeAsync(); } chatThreadStatus.Value = "Кеш истории недоступен."; }
    }
    private (string Id, double Y, double Offset)? ReadChatReadingAnchor(ScrollViewer? viewer)
    {
        if (viewer == null) return null;
        var anchor = ChatRows.Where(r => r.Bound && r.Message != null && r.Root.Bounds.Bottom > viewer.Bounds.Y + 1 &&
            r.Root.Bounds.Y < viewer.Bounds.Bottom).OrderBy(r => r.Root.Bounds.Y).FirstOrDefault();
        if (anchor == null) return null;
        return (anchor.Message!.Id, anchor.Bubble.Bounds.Y - viewer.Bounds.Y, viewer.VerticalOffset);
    }
    private bool CaptureChatReadingAnchor(ScrollViewer? viewer)
    {
        if (ReadChatReadingAnchor(viewer) is not { } anchor) return false;
        RestoreChatReadingAnchor(anchor); return true;
    }
    private void RestoreChatReadingAnchor((string Id, double Y, double Offset) anchor)
    {
        // Prepending can remove this row's date badge. Anchor the bubble itself
        // so the text stays put even when that decoration changes height.
        chatRestoreBubbleAnchor = true;
        chatRestoreAnchor = anchor.Id; chatRestoreAnchorY = anchor.Y;
        chatRestoreOffset = anchor.Offset; chatRestorePasses = 0; chatPrependAnchor = null;
    }
    private async Task ShiftChatWindowAsync(bool older)
    {
        if (chatThreadBusy || chatWindowBusy || chatRecipient == null || chatMessages.Count == 0) return;
        var store = chatHistory; var revision = chatRevision; var peer = chatRecipient.Id; chatWindowBusy = true;
        var first = chatMessages[0].Id; var count = chatMessages.Count; var cursor = chatMessageCursor;
        try
        {
            var segment = store == null || older && chatOlderCacheBoundary == first ? [] :
                older ? await store.BeforeAsync(peer, chatMessages[0], 100, ChatReadToken()) : await store.AfterAsync(peer, chatMessages[^1], 100, ChatReadToken());
            if (revision != chatRevision || chatHistory != store) return;
            if (segment.Length == 0)
            {
                if (older) { chatOlderCacheBoundary = first; await RefreshChatThreadAsync(older: true); }
                else chatWindowHasNewer = false;
                return;
            }
            var viewer = chatMessagesView.FindVisualChild<ScrollViewer>() as ScrollViewer;
            var anchor = ReadChatReadingAnchor(viewer);
            var combined = chatMessages.Concat(segment).DistinctBy(m => m.Id).OrderBy(m => m.SentAt).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
            var anchorIndex = anchor == null ? -1 : Array.FindIndex(combined, m => m.Id == anchor.Value.Id);
            var start = older ? 0 : Math.Max(0, combined.Length - 200);
            if (anchorIndex >= 0) start = Math.Clamp(start, Math.Max(0, anchorIndex - 180), anchorIndex);
            var window = combined.Skip(start).Take(200).ToArray();
            var newer = (await store!.AfterAsync(peer, window[^1], 1, ChatReadToken())).Length > 0;
            if (revision != chatRevision || chatHistory != store) return;
            // A background search may have filled the remaining server ranges.
            // Keep paging from the cache instead of reusing the old network cursor.
            var coverage = await store.LoadWindowAsync(peer, limit: 1, token: ChatReadToken());
            if (revision != chatRevision || chatHistory != store) return;
            var reactions = await store.ReactionsAsync(peer, ChatReadToken(), window.Select(m => m.Id));
            if (revision != chatRevision || chatHistory != store) return;
            // Do not start restoring against the old item source while the
            // metadata reads are still in flight: painted frames would finish
            // that restoration before the new window had even been installed.
            if (chatMessages.Count != count || chatMessages[0].Id != first) return;
            anchor = ReadChatReadingAnchor(viewer) ?? anchor;
            if (anchor is { } currentAnchor && !window.Any(message => message.Id == currentAnchor.Id)) return;
            var follow = older && (chatFollowLayout || chatScrollBottom || ChatIsAtBottom());
            if (follow && window[^1].Id != chatMessages[^1].Id) return;
            chatMessages.Clear(); chatMessages.AddRange(window);
            chatWindowHasNewer = newer; chatMessageCursor = coverage.Cursor; chatReactions = reactions;
            if (follow) RequestChatBottom();
            else if (anchor is { } reading) RestoreChatReadingAnchor(reading);
            RefreshChatTimeline();
            if (chatRestoreAnchor != null) { var index = chatMessageItems.ToList().FindIndex(m => m.Id == chatRestoreAnchor); if (index >= 0) chatMessagesView.ScrollIntoView(index); }
            Window.PerformLayout(); RememberChatThread();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (revision == chatRevision)
            {
                chatWindowBusy = false;
                if (count != chatMessages.Count || first != chatMessages[0].Id || cursor != chatMessageCursor)
                { chatPresentationDirty = true; chatViewport.InvalidateVisual(); }
                else if (older && chatMessageCursor != null) chatOlderRetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            }
        }
    }
    private async Task ShowLatestChatAsync()
    {
        if (chatHistory != null && chatRecipient != null)
        {
            var revision = chatRevision; var window = await chatHistory.LoadWindowAsync(chatRecipient.Id, token: ChatReadToken());
            if (revision != chatRevision) return;
            chatMessages.Clear(); chatMessages.AddRange(window.Messages); chatWindowHasNewer = false;
            chatRestoreAnchor = null; chatRestoreOffset = null; chatReactions = await chatHistory.ReactionsAsync(chatRecipient.Id, ChatReadToken(), chatMessages.Select(m => m.Id)); if (revision != chatRevision) return; RefreshChatTimeline();
        }
        RequestChatBottom(); await RefreshChatThreadAsync();
    }
}
