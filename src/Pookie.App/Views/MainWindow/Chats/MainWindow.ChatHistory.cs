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
            var reactions = await store.ReactionsAsync(peer, token, chatMessages.Concat(result.Messages).Where(m => !ChatProtocol.Project(m, account, peer).IsReaction).Select(m => m.Id).Distinct().TakeLast(200));
            if (chatHistory == store && chatRecipient?.Id == peer) chatReactions = reactions;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or InvalidOperationException)
        { if (chatHistory == store) { chatHistory = null; _ = store.DisposeAsync(); } chatThreadStatus.Value = "Кеш истории недоступен."; }
    }
    private Task LoadChatOlderWindowAsync() => ShiftChatWindowAsync(true);
    private async Task ShiftChatWindowAsync(bool older)
    {
        if (chatThreadBusy || chatWindowBusy || chatRecipient == null || chatHistory == null || chatMessages.Count == 0) return;
        var store = chatHistory; var revision = chatRevision; var peer = chatRecipient.Id; chatWindowBusy = true;
        try
        {
            var segment = older ? await store.BeforeAsync(peer, chatMessages[0], 100, ChatReadToken()) : await store.AfterAsync(peer, chatMessages[^1], 100, ChatReadToken());
            if (revision != chatRevision) return;
            if (segment.Length == 0)
            { if (older) await RefreshChatThreadAsync(older: true); else chatWindowHasNewer = false; return; }
            var viewer = chatMessagesView.FindVisualChild<ScrollViewer>() as ScrollViewer;
            var anchor = ChatRows.Where(r => r.Bound && r.Message != null && r.Root.Bounds.Bottom > (viewer?.Bounds.Y ?? 0) + 1).OrderBy(r => r.Root.Bounds.Y).FirstOrDefault();
            var combined = chatMessages.Concat(segment).DistinctBy(m => m.Id).OrderBy(m => m.SentAt).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
            var anchorIndex = anchor == null ? -1 : Array.FindIndex(combined, m => m.Id == anchor.Message!.Id);
            var start = older ? 0 : Math.Max(0, combined.Length - 200);
            if (anchorIndex >= 0) start = Math.Clamp(start, Math.Max(0, anchorIndex - 180), anchorIndex);
            chatMessages.Clear(); chatMessages.AddRange(combined.Skip(start).Take(200));
            if (anchor != null && viewer != null)
            { chatRestoreAnchor = anchor.Message!.Id; chatRestoreAnchorY = anchor.Root.Bounds.Y - viewer.Bounds.Y; chatRestoreOffset = viewer.VerticalOffset; chatRestorePasses = 0; }
            var newer = (await store.AfterAsync(peer, chatMessages[^1], 1, ChatReadToken())).Length > 0;
            if (revision != chatRevision) return;
            chatWindowHasNewer = newer;
            // A background search may have filled the remaining server ranges.
            // Keep paging from the cache instead of reusing the old network cursor.
            var coverage = await store.LoadWindowAsync(peer, limit: 1, token: ChatReadToken());
            if (revision != chatRevision) return;
            chatMessageCursor = coverage.Cursor;
            chatReactions = await store.ReactionsAsync(peer, ChatReadToken(), chatMessages.Select(m => m.Id));
            if (revision != chatRevision) return;
            RefreshChatTimeline();
            if (chatRestoreAnchor != null) { var index = chatMessageItems.ToList().FindIndex(m => m.Id == chatRestoreAnchor); if (index >= 0) chatMessagesView.ScrollIntoView(index); }
            Window.PerformLayout(); RememberChatThread();
        }
        catch (OperationCanceledException) { }
        finally { chatWindowBusy = false; }
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
