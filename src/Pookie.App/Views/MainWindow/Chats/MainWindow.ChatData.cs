using System.Collections.ObjectModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableCollection<ChatConversation> chatInboxItems = [];
    private readonly ObservableCollection<ChatMessageItem> chatMessageItems = [];
    private (double Maximum, double Offset)? chatPrependAnchor;
    private readonly HashSet<long> chatReadWrites = [];
    private readonly ChatUnreadTracker chatUnread = new();
    private readonly Dictionary<long, DateTimeOffset> chatReadRetryAfter = [];
    private DateTimeOffset chatNextPoll;
    private bool chatListLoaded;
    private CancellationTokenSource? chatPollingCancellation;
    private bool AllowChatRefresh => preferences.ChatBackgroundRefresh || page.Value == Page.Chats && Window.IsActive;
    private CancellationToken ChatPollingToken() => (chatPollingCancellation ??=
        CancellationTokenSource.CreateLinkedTokenSource(chatAccountLifetime?.Token ?? lifetime.Token)).Token;
    private void CancelChatPolling()
    {
        chatPollingCancellation?.Cancel(); chatPollingCancellation?.Dispose(); chatPollingCancellation = null;
    }
    private bool chatThreadRefreshRequested;
    private double chatLastBottomExtent = -1;
    private double chatViewportWidth, chatViewportHeight;
    private bool chatWasAtBottom;
    private ScrollViewer? chatDateViewer;
    private double chatDateOffset;
    private bool ChatIsAtBottom() => chatRestoreOffset == null && (chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer viewer ||
        SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 12);

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
        if (saved.Unread != null) foreach (var pair in saved.Unread) chatUnread.States[pair.Key] = pair.Value;
        if (preferences.ChatSaveDrafts) foreach (var pair in saved.Drafts) chatDrafts[pair.Key] = pair.Value;
        // A crash may have happened after the server accepted the POST.
        foreach (var pending in saved.Pending) chatPending[pending.RecipientId] = pending with { Uncertain = true };
        RestoreChatOutbox();
        if (!preferences.ChatSaveDrafts) SaveChatState();
    }

    private void ResetChats()
    {
        SaveChatDraft(); CancelChatReads(); CloseChatGifs();
        CancelChatPolling();
        chatAccountLifetime?.Cancel(); chatAccountLifetime?.Dispose(); chatAccountLifetime = null; chatSending = false;
        ++chatRevision; ++chatSearchRevision;
        chatAccountId = 0; chatLocalStore = null; chatRecipient = null;
        chatDrafts.Clear(); chatPending.Clear(); chatOutgoing.Clear(); chatMessageEntrances.Clear(); chatMessageOrder.Clear(); chatConversations.Clear(); chatMessages.Clear();
        chatThreadLoaded = false; ClearChatVisualCache();
        chatMessageItems.Clear(); chatInboxItems.Clear(); chatReadWrites.Clear(); chatUnread.States.Clear(); chatReadRetryAfter.Clear();
        ClearChatMediaCache(clearSummaries: true); ClearChatAttachments();
        chatListCursor = chatMessageCursor = chatLatestCursor = null; chatPrependAnchor = null; chatScrollBottom = false;
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
        CloseChatAttachMenu(); CloseChatMusicPicker(); CloseChatVideo(true);
        CancelChatAutomaticMedia(); CancelKlipyReads();
        chatReadCancellation?.Cancel(); chatReadCancellation?.Dispose(); chatReadCancellation = null;
        if (chatComposerSurface != null) SetChatDragging(false);
        chatDateLayer?.ResetOverlay();
        foreach (var row in ChatRows) VisualTree.Visit(row.Attachments, child => { if (child is ChatPhotoFrame frame) frame.Stop(); });
    }
    private bool ChatCurrent(WebSession session, long account, long revision) => !disposed && api.Session == session && me?.Id == account && chatAccountId == account && chatRevision == revision;

    private async Task RefreshChatsAsync()
    {
        if (disposed || chatsBusy || !AllowChatRefresh) return;
        EnsureChatAccount();
        if (demo || me == null || api.Session == null)
        { chatsStatus.Value = "Войди в SoundCloud, чтобы открыть переписку."; RefreshChatComposer(); return; }
        chatsBusy = true; RefreshChatLoading();
        var user = me; var session = api.Session; var revision = chatRevision; var token = ChatPollingToken();
        chatsStatus.Value = "";
        try
        {
            var result = await api.GetConversationsAsync(user.Id, token: token);
            if (!ChatCurrent(session, user.Id, revision) || token.IsCancellationRequested || !AllowChatRefresh) return;
            var unreadChanged = false;
            foreach (var conversation in result.Conversations)
            {
                if (token.IsCancellationRequested || !AllowChatRefresh) return;
                ChatMessage[]? recent = null;
                if (chatUnread.NeedsMessages(conversation) && chatUnread.States.ContainsKey(conversation.Recipient.Id))
                {
                    try { recent = (await api.GetMessagesAsync(user.Id, conversation.Recipient.Id, token: token)).Messages; }
                    catch (SoundCloudException) { continue; }
                }
                if (!ChatCurrent(session, user.Id, revision)) return;
                unreadChanged |= chatUnread.Observe(conversation, user.Id, recent);
            }
            if (unreadChanged) SaveChatState();
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
            chatNextPoll = DateTimeOffset.UtcNow.AddSeconds(error is SoundCloudException { StatusCode: 429 } ? 60 : 15);
            if (error is SoundCloudException { StatusCode: 401 } && api.Session == session) throw;
        }
        finally { if (chatRevision == revision) { chatsBusy = false; RefreshChatLoading(); } }
        if (!token.IsCancellationRequested && AllowChatRefresh && ChatCurrent(session, user.Id, revision) && page.Value == Page.Chats && chatRecipient != null) await RefreshChatThreadAsync();
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
            var unreadChanged = false;
            foreach (var conversation in result.Conversations) unreadChanged |= chatUnread.Observe(conversation, user.Id);
            if (unreadChanged) SaveChatState();
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
        SaveChatDraft(); Window.PerformLayout(); RememberChatThread(); ClearChatAttachments(); CancelChatReads();
        chatComposerSurface.SnapNextLayout();
        foreach (var row in ChatRows) row.Clear();
        ++chatRevision; chatThreadBusy = chatsBusy = false; chatThreadRefreshRequested = false; chatRecipient = recipient;
        chatColumns.Selected = true; chatName.Text = recipient.Username; chatFloatingDate.IsVisible = false;
        chatMessages.Clear(); chatMessageOrder.Clear(); chatMessageCursor = chatLatestCursor = null; chatPrependAnchor = null;
        chatScrollBottom = chatWasAtBottom = false; chatRestoreOffset = null; chatRestoreAnchor = null; chatRestorePasses = 0;
        chatViewportWidth = chatViewportHeight = 0;
        chatThreadLoaded = chatThreadCache.TryGetValue(recipient.Id, out var snapshot);
        if (snapshot != null)
        {
            chatMessages.AddRange(snapshot.Messages); chatMessageCursor = snapshot.Cursor; chatLatestCursor = snapshot.LatestCursor;
            foreach (var pair in snapshot.Order) chatMessageOrder[pair.Key] = pair.Value;
            if (snapshot.AtBottom) RequestChatBottom();
            else { chatRestoreOffset = snapshot.Offset; chatRestoreAnchor = snapshot.AnchorId; chatRestoreAnchorY = snapshot.AnchorY; }
        }
        chatThreadLoading.Reset();
        chatNewMessages.IsVisible = false; chatOlder.IsVisible = chatMessageCursor != null;
        chatThreadStatus.Value = "";
        chatRenderingDraft = true;
        try { chatInput.Text = chatDrafts.GetValueOrDefault(recipient.Id, ""); }
        finally { chatRenderingDraft = false; }
        RefreshChatComposer(); RefreshChatTimeline();
        foreach (var pair in chatInboxBindings)
        {
            var variant = pair.Value.Recipient.Id == recipient.Id ? TrackButtons.Tonal : TrackButtons.Text;
            if (pair.Key.StyleName != variant) TrackButtons.SnapNextChange(pair.Key);
            TrackButtons.SetVariant(pair.Key, variant);
        }
        if (chatRestoreAnchor != null)
        {
            var index = chatMessageItems.ToList().FindIndex(item => item.Id == chatRestoreAnchor);
            if (index >= 0) chatMessagesView.ScrollIntoView(index);
        }
        Window.PerformLayout();
        await RefreshChatThreadAsync(initial: !chatThreadLoaded);
    }

    private Task LoadOlderMessagesAsync() => RefreshChatThreadAsync(older: true);
    private async Task RefreshChatThreadAsync(bool initial = false, bool older = false)
    {
        if (chatThreadBusy || !AllowChatRefresh || chatRecipient == null || me == null || api.Session == null || older && chatMessageCursor == null) return;
        var recipient = chatRecipient; var user = me; var session = api.Session; var revision = chatRevision;
        var cursor = older ? chatMessageCursor : null;
        chatThreadBusy = true; chatThreadRefreshRequested = false; chatOlder.IsEnabled = false; RefreshChatLoading(); RefreshChatComposer();
        var viewer = chatMessagesView.FindVisualChild<ScrollViewer>() as ScrollViewer;
        var nearBottom = chatRestoreOffset == null && (chatScrollBottom || viewer == null || SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 90);
        var before = viewer == null ? ((double, double)?)null : (SmoothScroll.Maximum(viewer), viewer.VerticalOffset);
        using var read = CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken(), ChatPollingToken());
        try
        {
            var result = await api.GetMessagesAsync(user.Id, recipient.Id, cursor, read.Token);
            if (!ChatCurrent(session, user.Id, revision) || page.Value != Page.Chats || read.IsCancellationRequested || !AllowChatRefresh) return;
            chatThreadLoaded = true;
            if (!older) chatLatestCursor = result.NextHref;
            var oldIds = chatMessages.Select(m => m.Id).ToHashSet();
            var added = result.Messages.Where(m => !oldIds.Contains(m.Id)).ToArray();
            chatMessages.AddRange(added);
            if (initial || older || chatMessageCursor == null && chatMessages.Count == added.Length) chatMessageCursor = result.NextHref == cursor ? null : result.NextHref;
            if (!initial && !older) foreach (var message in added.TakeLast(20)) chatMessageEntrances.Add(message.Id);
            if (chatMessageEntrances.Count > 128) chatMessageEntrances.Clear();
            if (chatOutgoing.TryGetValue(recipient.Id, out var outgoing) &&
                (!outgoing.HasMedia || ChatMediaReference.Parse(outgoing.Message.Body) != null) &&
                chatMessages.FirstOrDefault(m => m.SenderId == user.Id && m.Body == outgoing.Message.Body && !outgoing.BeforeSendIds.Contains(m.Id) && m.SentAt >= outgoing.Message.SentAt.AddSeconds(-5)) is { } confirmed)
            {
                if (outgoing.DisplayOrder != default) chatMessageOrder[confirmed.Id] = outgoing.DisplayOrder;
                var slot = chatMessageItems.ToList().FindIndex(m => m.Id == outgoing.Message.Id);
                if (slot >= 0) chatMessageItems[slot].Message = confirmed;
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
            Window.PerformLayout();
            RememberChatThread(); ReconcilePendingChat(); RefreshChatComposer();
            if (nearBottom || initial) await MarkSelectedChatReadAsync(session, user.Id, recipient.Id, revision);
        }
        catch (OperationCanceledException) { }
        catch (SoundCloudException error) when (error.StatusCode == 404 && ChatCurrent(session, user.Id, revision))
        { chatThreadLoaded = true; chatThreadStatus.Value = "Напиши первое сообщение"; }
        catch (Exception error)
        {
            if (ChatCurrent(session, user.Id, revision)) chatThreadStatus.Value = FriendlyError(error);
            if (error is SoundCloudException { StatusCode: 401 } && api.Session == session) throw;
        }
        finally { if (chatRevision == revision) { chatThreadBusy = false; chatOlder.IsEnabled = true; RefreshChatLoading(); RefreshChatComposer(); } }
        // A send completed while this read was in flight; its snapshot may predate the send.
        if (chatThreadRefreshRequested && !read.IsCancellationRequested && AllowChatRefresh && ChatCurrent(session, user.Id, revision) && page.Value == Page.Chats)
            await RefreshChatThreadAsync();
    }

    private async Task MarkSelectedChatReadAsync(WebSession session, long account, long recipient, long revision)
    {
        var conversation = chatConversations.FirstOrDefault(c => c.Recipient.Id == recipient);
        if (!Window.IsActive || page.Value != Page.Chats || !chatColumns.Selected && chatColumns.ActualWidth < 760 || conversation == null || conversation.Read || chatReadRetryAfter.GetValueOrDefault(recipient) > DateTimeOffset.UtcNow || !chatReadWrites.Add(recipient)) return;
        try
        {
            await api.MarkConversationReadAsync(account, recipient, lifetime.Token);
            if (!ChatCurrent(session, account, revision)) return;
            var index = chatConversations.FindIndex(c => c.Recipient.Id == recipient);
            if (index >= 0) chatConversations[index] = chatConversations[index] with { Read = true };
            chatUnread.Clear(recipient); chatReadRetryAfter.Remove(recipient); SaveChatState();
            RenderChatInbox();
        }
        catch (Exception error) when (error is SoundCloudException or HttpRequestException or OperationCanceledException)
        { if (error is not OperationCanceledException) chatReadRetryAfter[recipient] = DateTimeOffset.UtcNow.AddMinutes(error is SoundCloudException { StatusCode: 400 or 403 } ? 5 : 1); }
        finally { chatReadWrites.Remove(recipient); }
    }

    private void RequestChatBottom()
    {
        chatPrependAnchor = null; chatScrollBottom = true; chatLastBottomExtent = -1; chatViewport.InvalidateArrange();
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
        RefreshChatDateOverlay();
        if (chatWasAtBottom && chatThreadLoaded && !chatThreadBusy && me != null && api.Session != null && chatRecipient != null)
            Run(() => MarkSelectedChatReadAsync(api.Session, me.Id, chatRecipient.Id, chatRevision));
    }

    private bool SynchronizeChatScroll()
    {
        if (page.Value != Page.Chats || chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer viewer) return false;
        var changed = false;
        var maximum = SmoothScroll.Maximum(viewer);
        if (chatRestoreOffset is { } restore)
        {
            var anchor = chatRestoreAnchor == null ? null : ChatRows.FirstOrDefault(r => r.Bound && r.Message?.Id == chatRestoreAnchor && r.Root.ActualHeight > 0);
            var requested = anchor != null ? viewer.VerticalOffset + anchor.Root.Bounds.Y - viewer.Bounds.Y - chatRestoreAnchorY : restore;
            var target = Math.Clamp(requested, 0, maximum);
            if (Math.Abs(target - viewer.VerticalOffset) > .1)
            { viewer.SetScrollOffsets(0, target); changed = true; }
            if (anchor != null && Math.Abs(anchor.Root.Bounds.Y - viewer.Bounds.Y - chatRestoreAnchorY) > .5) changed = true;
            // A larger window can make the old anchor unreachable at the end of
            // history. Accept the nearest edge after bounded extent refinement.
            if (++chatRestorePasses >= 3 && (!changed || chatRestorePasses >= 16)) { chatRestoreOffset = null; chatRestoreAnchor = null; }
            else changed = true;
        }
        if (chatRestoreOffset == null && chatWasAtBottom &&
            (Math.Abs(chatViewportWidth - viewer.ActualWidth) > .1 || Math.Abs(chatViewportHeight - viewer.ActualHeight) > .1))
            chatScrollBottom = true;
        chatViewportWidth = viewer.ActualWidth; chatViewportHeight = viewer.ActualHeight;
        if (chatScrollBottom)
        {
            var settled = Math.Abs(maximum - chatLastBottomExtent) < .1 && Math.Abs(maximum - viewer.VerticalOffset) < .1;
            chatLastBottomExtent = maximum;
            chatNewMessages.IsVisible = false;
            if (Math.Abs(maximum - viewer.VerticalOffset) > .1) viewer.SetScrollOffsets(0, maximum);
            if (settled) { chatScrollBottom = false; chatLastBottomExtent = -1; }
            else changed = true;
        }
        else if (chatPrependAnchor is { } anchor)
        {
            chatPrependAnchor = null;
            viewer.SetScrollOffsets(0, anchor.Offset + maximum - anchor.Maximum); changed = true;
        }
        chatWasAtBottom = chatRestoreOffset == null && maximum - viewer.VerticalOffset < 12;
        return changed;
    }

    private void OnChatDateScroll()
    {
        if (chatDateViewer is not { } viewer || Math.Abs(chatDateOffset - viewer.VerticalOffset) < .1) return;
        chatDateOffset = viewer.VerticalOffset;
        if (page.Value == Page.Chats && chatRestoreOffset == null && !chatScrollBottom && chatPrependAnchor == null) chatDateLayer.Pulse();
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
        if (preferences.ChatSaveDrafts && chatRecipient != null && !chatRenderingDraft) chatDrafts[chatRecipient.Id] = chatInput.Text.Length <= 20000 ? chatInput.Text : chatInput.Text[..20000];
        SaveChatState();
    }
    private void SaveChatState()
    {
        try { chatLocalStore?.Save(new(chatDrafts.Where(p => p.Value.Length > 0).TakeLast(50).ToDictionary(), chatPending.Values.Take(50).ToArray(), chatUnread.States.TakeLast(500).ToDictionary()), preferences.ChatSaveDrafts); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { chatThreadStatus.Value = "Не удалось сохранить черновик или исходящее сообщение."; }
    }
    private void RefreshChatComposer()
    {
        var available = !demo && me != null && api.Session != null && chatRecipient != null && !chatSending;
        var pending = chatRecipient != null && chatPending.ContainsKey(chatRecipient.Id);
        chatComposer.IsVisible = chatRecipient != null;
        chatInput.IsEnabled = available && !pending;
        chatAttach.IsEnabled = chatGif.IsEnabled = available && !pending && !chatImporting;
        if (chatGif.IsVisible != preferences.KlipyEnabled)
        { chatGif.IsVisible = preferences.KlipyEnabled; chatComposerLayout.InvalidateMeasure(); }
        chatGif.ToolTip = new TextBlock().Text("Поиск GIF в KLIPY");
        chatSend.ToolTip = new TextBlock().Text(preferences.ChatSendOnEnter ? "Отправить · Enter (Shift+Enter — новая строка)" : "Отправить · Ctrl+Enter");
        chatSend.IsEnabled = available && !pending && !chatImporting && !(chatThreadBusy && chatMessages.Count == 0) &&
            (chatInput.Text.Trim().Length > 0 || chatSelectedFiles.Length > 0 || chatSelectedGif != null) && chatInput.Text.Length <= 20000;
        RefreshChatComposerSurface();
        RefreshChatMusicComposer();
    }

    private void DisposeChats()
    {
        SaveChatDraft(); chatTimer.Dispose(); chatSearchTimer.Dispose(); chatDraftTimer.Dispose(); CancelChatReads();
        chatMusicRecognizeTimer.Dispose(); chatMusicEditor?.Dispose();
        chatAttachInteraction.Dispose(); chatMusicSearchTimer.Dispose(); chatVideoTimer.Dispose(); chatVideoExpansionClock.Stop(); chatVideoSpeedClock.Stop();
        Window.FrameRendered -= ArrangeChatScroll; CloseChatGifs();
        if (chatDateViewer != null) chatDateViewer.ScrollChanged -= OnChatDateScroll;
        chatDateLayer.Dispose();
        chatAttachmentPreviewLoad?.Cancel(); chatAttachmentPreviewLoad?.Dispose();
        chatAttachmentStaging.Dispose();
        foreach (var row in ChatRows) row.Clear();
        CancelChatPolling();
        chatAccountLifetime?.Cancel(); chatAccountLifetime?.Dispose();
        chatInboxLoading.Dispose(); chatThreadLoading.Dispose(); ClearChatMediaCache(clearSummaries: true); ClearChatVisualCache(); chatMedia.Dispose(); klipy.Dispose();
    }
}
