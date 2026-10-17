using System.Collections.ObjectModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableCollection<ChatConversation> chatInboxItems = [];
    private readonly ObservableCollection<ChatMessage> chatMessageItems = [];
    private (double Maximum, double Offset)? chatPrependAnchor;
    private readonly HashSet<long> chatReadWrites = [];
    private bool chatListLoaded;
    private bool chatThreadRefreshRequested;
    private double chatLastBottomExtent = -1;
    private int chatBottomSettledFrames;
    private double chatViewportWidth, chatViewportHeight;
    private bool chatWasAtBottom;
    private ScrollViewer? chatDateViewer;
    private double chatDateOffset;
    private bool ChatIsAtBottom() => chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer viewer ||
        SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 12;

    private Task OpenChatsAsync() => NavigateRouteAsync(new(Page.Chats), async _ =>
    {
        EnsureChatAccount();
        chatColumns.Selected = chatRecipient != null;
        RefreshChatTimeline();
        await RefreshChatsAsync();
    });

    private void EnsureChatAccount()
    {
        var userId = me?.Id ?? 0;
        if (chatAccountId == userId) return;
        ResetChats();
        chatAccountId = userId;
        chatAccountLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        if (userId <= 0) return;
        chatLocalStore = new(dataPaths, userId);
        var saved = chatLocalStore.Load();
        foreach (var pair in saved.Drafts) chatDrafts[pair.Key] = pair.Value;
        // A crash may have happened after the server accepted the POST.
        foreach (var pending in saved.Pending) chatPending[pending.RecipientId] = pending with { Uncertain = true };
        RestoreChatOutbox();
    }

    private void ResetChats()
    {
        SaveChatDraft(); CancelChatReads(); CloseChatGifs();
        chatAccountLifetime?.Cancel(); chatAccountLifetime?.Dispose(); chatAccountLifetime = null; chatSending = false;
        ++chatRevision; ++chatSearchRevision;
        chatAccountId = 0; chatLocalStore = null; chatRecipient = null;
        chatDrafts.Clear(); chatPending.Clear(); chatOutgoing.Clear(); chatMessageEntrances.Clear(); chatMessageOrder.Clear(); chatConversations.Clear(); chatMessages.Clear();
        chatMessageItems.Clear(); chatInboxItems.Clear(); chatReadWrites.Clear();
        ClearChatMediaCache(clearSummaries: true); ClearChatAttachments();
        chatListCursor = chatMessageCursor = null; chatPrependAnchor = null; chatScrollBottom = false;
        chatsBusy = chatThreadBusy = false; chatThreadRefreshRequested = false; chatSearchMode = false; chatListLoaded = false;
        if (chatName != null)
        {
            chatName.Text = "Твои сообщения"; chatFloatingDate.IsVisible = false;
            chatName.InvalidateMeasure(); chatColumns.Selected = false;
            chatRenderingDraft = true; chatInput.Text = ""; chatRenderingDraft = false;
            chatsStatus.Value = chatThreadStatus.Value = "";
            chatMore.IsVisible = chatOlder.IsVisible = chatNewMessages.IsVisible = false;
            RefreshChatLoading();
            RefreshChatComposer();
        }
    }

    private CancellationToken ChatReadToken()
    {
        if (chatReadCancellation == null) chatReadCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        return chatReadCancellation.Token;
    }
    private void CancelChatReads()
    {
        chatReadCancellation?.Cancel(); chatReadCancellation?.Dispose(); chatReadCancellation = null;
        if (chatComposerSurface != null) SetChatDragging(false);
        chatDateLayer?.ResetOverlay();
        chatMediaAutoRequested.Clear(); chatMediaAutoOrder.Clear(); chatMediaLoads.Clear();
        foreach (var row in ChatRows) VisualTree.Visit(row.Attachments, child => { if (child is ChatPhotoFrame frame) frame.Stop(); });
    }
    private bool ChatCurrent(WebSession session, long account, long revision) => !disposed && api.Session == session && me?.Id == account && chatAccountId == account && chatRevision == revision;

    private async Task RefreshChatsAsync()
    {
        if (disposed || page.Value != Page.Chats || chatsBusy) return;
        EnsureChatAccount();
        if (demo || me == null || api.Session == null)
        { chatsStatus.Value = "Войди в SoundCloud, чтобы открыть переписку."; RefreshChatComposer(); return; }
        chatsBusy = true; RefreshChatLoading();
        var user = me; var session = api.Session; var revision = chatRevision;
        chatsStatus.Value = "";
        try
        {
            var result = await api.GetConversationsAsync(user.Id, token: ChatReadToken());
            if (!ChatCurrent(session, user.Id, revision) || page.Value != Page.Chats) return;
            if (!chatListLoaded) { chatListCursor = result.NextHref; chatListLoaded = true; }
            var previous = chatConversations.ToArray();
            chatConversations.Clear(); chatConversations.AddRange(result.Conversations.Concat(previous).DistinctBy(c => c.Id).OrderByDescending(c => c.LastMessage?.SentAt));
            chatsStatus.Value = chatConversations.Count == 0 ? "Пока нет диалогов. Найди собеседника выше." : "";
            RenderChatInbox();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (ChatCurrent(session, user.Id, revision)) chatsStatus.Value = FriendlyError(error);
            if (error is SoundCloudException { StatusCode: 401 } && api.Session == session) throw;
        }
        finally { if (chatRevision == revision) { chatsBusy = false; RefreshChatLoading(); } }
        if (ChatCurrent(session, user.Id, revision) && chatRecipient != null) await RefreshChatThreadAsync();
    }

    private void RenderChatInbox()
    {
        if (chatSearchMode) return;
        PatchChatItems(chatInboxItems, chatConversations, c => c.Id);
        chatMore.IsVisible = chatListCursor != null;
        foreach (var binding in chatInboxBindings) RefreshChatInboxPreview(binding.Key, binding.Value);
    }
    private async Task LoadMoreChatsAsync()
    {
        if (chatsBusy || chatListCursor == null || me == null || api.Session == null) return;
        chatsBusy = true; chatMore.IsEnabled = false;
        var user = me; var session = api.Session; var revision = chatRevision; var cursor = chatListCursor;
        try
        {
            var result = await api.GetConversationsAsync(user.Id, cursor, ChatReadToken());
            if (!ChatCurrent(session, user.Id, revision)) return;
            chatConversations.AddRange(result.Conversations.Where(c => !chatConversations.Any(old => old.Id == c.Id)));
            chatListCursor = result.NextHref == cursor ? null : result.NextHref; RenderChatInbox();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (ChatCurrent(session, user.Id, revision)) chatsStatus.Value = FriendlyError(error); }
        finally { if (chatRevision == revision) { chatsBusy = false; chatMore.IsEnabled = true; } }
    }

    private async Task SearchChatsAsync()
    {
        var query = chatSearch.Text.Trim();
        if (page.Value != Page.Chats || me == null || api.Session == null) return;
        var search = ++chatSearchRevision; var account = me.Id; var session = api.Session;
        chatSearchMode = query.Length > 0;
        if (!chatSearchMode) { chatsStatus.Value = ""; RenderChatInbox(); return; }
        chatMore.IsVisible = false;
        var local = chatConversations.Where(c => c.Recipient.Username.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        PatchChatItems(chatInboxItems, local, c => c.Id);
        if (query.Length < 2) { chatsStatus.Value = "Введи минимум два символа для поиска людей"; return; }
        chatsStatus.Value = "Ищем людей…";
        try
        {
            var results = await api.SearchResultsAsync(query, SearchSection.People, ChatReadToken());
            if (disposed || api.Session != session || me?.Id != account || search != chatSearchRevision || page.Value != Page.Chats) return;
            var users = results.Items.Select(i => i.User).OfType<SoundCloudUser>().Where(u => u.Id > 0 && u.Id != account)
                .Select(u => chatConversations.FirstOrDefault(c => c.Recipient.Id == u.Id) ?? new ChatConversation($"new:{u.Id}", u, null, true));
            var combined = local.Concat(users).DistinctBy(c => c.Recipient.Id).ToArray();
            PatchChatItems(chatInboxItems, combined, c => c.Id);
            chatsStatus.Value = combined.Length == 0 ? "Никого не нашли" : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (search == chatSearchRevision && api.Session == session) chatsStatus.Value = FriendlyError(error); }
    }

    private async Task SelectChatAsync(SoundCloudUser recipient)
    {
        if (me == null || api.Session == null || recipient.Id == me.Id || recipient.Id <= 0) return;
        EnsureChatAccount();
        if (chatRecipient?.Id == recipient.Id) { chatColumns.Selected = true; return; }
        SaveChatDraft(); ClearChatAttachments(); CancelChatReads(); ClearChatMediaCache();
        ++chatRevision; chatThreadBusy = chatsBusy = false; chatThreadRefreshRequested = false; chatRecipient = recipient;
        chatColumns.Selected = true; chatName.Text = recipient.Username; chatFloatingDate.IsVisible = false;
        chatMessages.Clear(); chatMessageOrder.Clear(); chatMessageItems.Clear(); chatMessageCursor = null; chatPrependAnchor = null;
        chatNewMessages.IsVisible = chatOlder.IsVisible = false;
        chatThreadStatus.Value = "";
        chatRenderingDraft = true;
        try { chatInput.Text = chatDrafts.GetValueOrDefault(recipient.Id, ""); }
        finally { chatRenderingDraft = false; }
        RefreshChatComposer(); RefreshChatTimeline();
        foreach (var pair in chatInboxBindings) TrackButtons.SetVariant(pair.Key, pair.Value.Recipient.Id == recipient.Id ? TrackButtons.Tonal : TrackButtons.Text);
        await RefreshChatThreadAsync(initial: true);
    }

    private Task LoadOlderMessagesAsync() => RefreshChatThreadAsync(older: true);
    private async Task RefreshChatThreadAsync(bool initial = false, bool older = false)
    {
        if (chatThreadBusy || chatRecipient == null || me == null || api.Session == null || older && chatMessageCursor == null) return;
        var recipient = chatRecipient; var user = me; var session = api.Session; var revision = chatRevision;
        var cursor = older ? chatMessageCursor : null;
        chatThreadBusy = true; chatThreadRefreshRequested = false; chatOlder.IsEnabled = false; RefreshChatLoading(); RefreshChatComposer();
        var viewer = chatMessagesView.FindVisualChild<ScrollViewer>() as ScrollViewer;
        var nearBottom = viewer == null || SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 90;
        var before = viewer == null ? ((double, double)?)null : (SmoothScroll.Maximum(viewer), viewer.VerticalOffset);
        try
        {
            var result = await api.GetMessagesAsync(user.Id, recipient.Id, cursor, ChatReadToken());
            if (!ChatCurrent(session, user.Id, revision) || page.Value != Page.Chats) return;
            var oldIds = chatMessages.Select(m => m.Id).ToHashSet();
            var added = result.Messages.Where(m => !oldIds.Contains(m.Id)).ToArray();
            chatMessages.AddRange(added);
            if (initial || older) chatMessageCursor = result.NextHref == cursor ? null : result.NextHref;
            if (!initial && !older) foreach (var message in added.TakeLast(20)) chatMessageEntrances.Add(message.Id);
            if (chatMessageEntrances.Count > 128) chatMessageEntrances.Clear();
            if (chatOutgoing.TryGetValue(recipient.Id, out var outgoing) &&
                (!outgoing.HasMedia || ChatMediaReference.Parse(outgoing.Message.Body) != null) &&
                chatMessages.FirstOrDefault(m => m.SenderId == user.Id && m.Body == outgoing.Message.Body && !outgoing.BeforeSendIds.Contains(m.Id) && m.SentAt >= outgoing.Message.SentAt.AddSeconds(-5)) is { } confirmed)
            {
                if (outgoing.DisplayOrder != default) chatMessageOrder[confirmed.Id] = outgoing.DisplayOrder;
                var slot = chatMessageItems.ToList().FindIndex(m => m.Id == outgoing.Message.Id);
                if (slot >= 0) chatMessageItems[slot] = confirmed;
                chatMessageEntrances.Remove(confirmed.Id);
                chatOutgoing.Remove(recipient.Id);
            }
            var ordered = chatMessages.OrderBy(ChatOrder).ToArray(); chatMessages.Clear(); chatMessages.AddRange(ordered);
            RefreshChatTimeline();
            chatOlder.IsVisible = chatMessageCursor != null;
            if (initial || !older && nearBottom) RequestChatBottom();
            else if (older && added.Length > 0 && before.HasValue) chatPrependAnchor = before;
            else if (!older && added.Length > 0) chatNewMessages.IsVisible = true;
            chatThreadStatus.Value = chatMessages.Count == 0 ? "Напиши первое сообщение" : "";
            if (chatMessages.LastOrDefault() is { } latest)
            {
                var index = chatConversations.FindIndex(c => c.Recipient.Id == recipient.Id);
                if (index >= 0 && chatConversations[index].LastMessage != latest)
                {
                    chatConversations[index] = chatConversations[index] with { LastMessage = latest };
                    chatConversations.Sort((a, b) => Nullable.Compare(b.LastMessage?.SentAt, a.LastMessage?.SentAt));
                    RenderChatInbox();
                }
            }
            ReconcilePendingChat(); RefreshChatComposer();
            if (nearBottom || initial) await MarkSelectedChatReadAsync(session, user.Id, recipient.Id, revision);
        }
        catch (OperationCanceledException) { }
        catch (SoundCloudException error) when (error.StatusCode == 404 && ChatCurrent(session, user.Id, revision))
        { chatThreadStatus.Value = "Напиши первое сообщение"; }
        catch (Exception error)
        {
            if (ChatCurrent(session, user.Id, revision)) chatThreadStatus.Value = FriendlyError(error);
            if (error is SoundCloudException { StatusCode: 401 } && api.Session == session) throw;
        }
        finally { if (chatRevision == revision) { chatThreadBusy = false; chatOlder.IsEnabled = true; RefreshChatLoading(); RefreshChatComposer(); } }
        // A send completed while this read was in flight; its snapshot may predate the send.
        if (chatThreadRefreshRequested && ChatCurrent(session, user.Id, revision) && page.Value == Page.Chats)
            await RefreshChatThreadAsync();
    }

    private async Task MarkSelectedChatReadAsync(WebSession session, long account, long recipient, long revision)
    {
        var conversation = chatConversations.FirstOrDefault(c => c.Recipient.Id == recipient);
        if (!Window.IsActive || page.Value != Page.Chats || conversation == null || conversation.Read || !chatReadWrites.Add(recipient)) return;
        try
        {
            await api.MarkConversationReadAsync(account, recipient, lifetime.Token);
            if (!ChatCurrent(session, account, revision)) return;
            var index = chatConversations.FindIndex(c => c.Recipient.Id == recipient);
            if (index >= 0) chatConversations[index] = chatConversations[index] with { Read = true };
            RenderChatInbox();
        }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or OperationCanceledException) { }
        finally { chatReadWrites.Remove(recipient); }
    }

    private void RequestChatBottom()
    {
        chatPrependAnchor = null; chatScrollBottom = true; chatBottomSettledFrames = 0; chatLastBottomExtent = -1; Window.InvalidateVisual();
    }

    private void ArrangeChatScroll()
    {
        foreach (var row in ChatRows.Where(r => !r.Bound && r.Message != null).ToArray()) row.Clear();
        if (page.Value != Page.Chats) return;
        foreach (var control in new[] { chatInbox, chatMessagesView })
            if (control.FindVisualChild<ScrollViewer>() is ScrollViewer scroll) scroll.HorizontalScroll = ScrollMode.Disabled;
        if (chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer viewer) return;
        if (chatDateViewer != viewer)
        {
            if (chatDateViewer != null) chatDateViewer.ScrollChanged -= OnChatDateScroll;
            chatDateViewer = viewer; chatDateOffset = viewer.VerticalOffset;
            viewer.ScrollChanged += OnChatDateScroll;
        }
        if (chatWasAtBottom && (Math.Abs(chatViewportWidth - viewer.ActualWidth) > 1 || Math.Abs(chatViewportHeight - viewer.ActualHeight) > 1)) RequestChatBottom();
        chatViewportWidth = viewer.ActualWidth; chatViewportHeight = viewer.ActualHeight;
        if (chatScrollBottom)
        {
            var maximum = SmoothScroll.Maximum(viewer);
            var settled = Math.Abs(maximum - chatLastBottomExtent) < .5 && Math.Abs(maximum - viewer.VerticalOffset) < .5;
            chatBottomSettledFrames = settled ? chatBottomSettledFrames + 1 : 0;
            chatLastBottomExtent = maximum;
            chatNewMessages.IsVisible = false;
            viewer.SetScrollOffsets(0, maximum);
            if (chatBottomSettledFrames >= 2) { chatScrollBottom = false; chatBottomSettledFrames = 0; chatLastBottomExtent = -1; }
            else Window.InvalidateVisual();
        }
        else if (chatPrependAnchor is { } anchor)
        { chatPrependAnchor = null; viewer.SetScrollOffsets(0, anchor.Offset + SmoothScroll.Maximum(viewer) - anchor.Maximum); }
        RefreshChatDateOverlay();
        chatWasAtBottom = SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 12;
    }

    private void OnChatDateScroll()
    {
        if (chatDateViewer is not { } viewer || Math.Abs(chatDateOffset - viewer.VerticalOffset) < .1) return;
        chatDateOffset = viewer.VerticalOffset;
        if (page.Value == Page.Chats && !chatScrollBottom && chatPrependAnchor == null) chatDateLayer.Pulse();
        RefreshChatDateOverlay();
    }

    private static void PatchChatItems<T>(ObservableCollection<T> current, IEnumerable<T> desired, Func<T, string> key)
    {
        var next = desired.ToArray();
        for (var i = 0; i < next.Length; i++)
        {
            if (i < current.Count && key(current[i]) == key(next[i])) { if (!EqualityComparer<T>.Default.Equals(current[i], next[i])) current[i] = next[i]; continue; }
            var match = -1;
            for (var j = i + 1; j < current.Count; j++) if (key(current[j]) == key(next[i])) { match = j; break; }
            if (match >= 0) { current.Move(match, i); if (!EqualityComparer<T>.Default.Equals(current[i], next[i])) current[i] = next[i]; }
            else current.Insert(i, next[i]);
        }
        while (current.Count > next.Length) current.RemoveAt(current.Count - 1);
    }

    private void SaveChatDraft()
    {
        if (chatLocalStore == null || chatAccountId <= 0) return;
        if (chatRecipient != null && !chatRenderingDraft) chatDrafts[chatRecipient.Id] = chatInput.Text.Length <= 20000 ? chatInput.Text : chatInput.Text[..20000];
        SaveChatState();
    }
    private void SaveChatState()
    {
        try { chatLocalStore?.Save(new(chatDrafts.Where(p => p.Value.Length > 0).TakeLast(50).ToDictionary(), chatPending.Values.Take(50).ToArray())); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { chatThreadStatus.Value = "Не удалось сохранить черновик или исходящее сообщение."; }
    }
    private void RefreshChatComposer()
    {
        var available = !demo && me != null && api.Session != null && chatRecipient != null && !chatSending;
        var pending = chatRecipient != null && chatPending.ContainsKey(chatRecipient.Id);
        chatComposer.IsVisible = chatRecipient != null;
        chatInput.IsEnabled = available && !pending;
        var lines = 1 + chatInput.Text.Count(c => c == '\n');
        chatInput.Height = Math.Clamp(28 + (lines - 1) * 20, 28, 108);
        chatAttach.IsEnabled = chatGif.IsEnabled = available && !pending && !chatImporting;
        chatSend.IsEnabled = available && !pending && !chatImporting && !(chatThreadBusy && chatMessages.Count == 0) &&
            (chatInput.Text.Trim().Length > 0 || chatSelectedFiles.Length > 0 || chatSelectedGif != null) && chatInput.Text.Length <= 20000;
        RefreshChatComposerSurface();

    }

    private void DisposeChats()
    {
        SaveChatDraft(); chatTimer.Dispose(); chatSearchTimer.Dispose(); chatDraftTimer.Dispose(); CancelChatReads();
        Window.FrameRendered -= ArrangeChatScroll; CloseChatGifs();
        if (chatDateViewer != null) chatDateViewer.ScrollChanged -= OnChatDateScroll;
        chatDateLayer.Dispose();
        chatAttachmentStaging.Dispose();
        foreach (var row in ChatRows) row.Clear();
        chatInboxLoading.Dispose(); chatThreadLoading.Dispose(); ClearChatMediaCache(clearSummaries: true); chatAccountLifetime?.Cancel(); chatAccountLifetime?.Dispose(); chatMedia.Dispose(); klipy.Dispose();
    }
}
