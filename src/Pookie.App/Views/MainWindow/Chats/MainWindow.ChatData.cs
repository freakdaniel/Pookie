using System.Collections.ObjectModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly SemaphoreSlim chatSelectionGate=new(1,1);
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
    private bool chatRestoreBubbleAnchor;
    private ScrollViewer? chatDateViewer;
    private double chatDateOffset;
    private bool ChatIsAtBottom() => chatRestoreOffset == null && (chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer viewer ||
        SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 12);

    private Task OpenChatsAsync() => NavigateRouteAsync(new(Page.Chats), async _ =>
    {
        await EnsureChatAccountAsync();
        await RestoreChatHistoryAsync();
        chatColumns.Selected = chatRecipient != null;
        RefreshChatTimeline();
        await RefreshChatsAsync();
    });

    private Task chatAccountRestore = Task.CompletedTask;
    private bool chatAccountReady;
    private Task chatPersistenceClosing = Task.CompletedTask;
    private Task EnsureChatAccountAsync()
    {
        var userId = me?.Id ?? 0;
        if (chatAccountId == userId) return chatAccountRestore;
        ResetChats();
        chatAccountId = userId;
        chatAccountLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        if (userId <= 0) return chatAccountRestore = Task.CompletedTask;
        chatLocalStore = new(dataPaths, userId);
        chatHistory = new(dataPaths, userId);
        chatMedia.DiskCache = new(dataPaths, userId);
        chatWorkspaceStore = new(dataPaths, userId);
        return chatAccountRestore = RestoreChatAccountAsync(userId, chatLocalStore, chatWorkspaceStore);
    }
    private async Task RestoreChatAccountAsync(long account, ChatLocalStore local, ChatWorkspaceStore workspaceStore)
    {
        await chatPersistenceClosing;
        var saved = await Task.Run(() => (Local: local.Load(), Workspace: workspaceStore.Load()));
        if (disposed || chatAccountId != account || chatLocalStore != local) return;
        chatAccountReady = true;
        chatWorkspace = saved.Workspace;
        if (!preferences.ChatSaveDrafts) chatWorkspace = chatWorkspace with { Drafts = null };
        RefreshChatFavoriteMedia(); CleanupChatVoiceFiles(); chatDiskRestored = false; RefreshChatFolders();
        if (saved.Local.Unread != null) foreach (var pair in saved.Local.Unread) chatUnread.States[pair.Key] = pair.Value;
        if (preferences.ChatSaveDrafts) foreach (var pair in saved.Local.Drafts) chatDrafts[pair.Key] = pair.Value;
        foreach (var pending in saved.Local.Pending) chatPending[pending.RecipientId] = pending with { Uncertain = true };
        RestoreChatOutbox();
        if (!preferences.ChatSaveDrafts) SaveChatState();
    }
    private Task FlushChatPersistenceAsync() => Task.WhenAll(chatPersistenceClosing,
        chatLocalStore?.FlushAsync() ?? Task.CompletedTask, chatWorkspaceStore?.FlushAsync() ?? Task.CompletedTask);

    private void ResetChats()
    {
        chatEnvelopes=new();
        chatReplyLookups.Clear();
        chatReplyQuote.BeginBinding(); chatReplyQuote.Set("","",null); chatComposerSurface.SnapNextLayout();
        SaveChatDraft(); ResetChatVoice(); CloseChatDetails(); CloseChatSearch(); chatFavoritesOpen = chatFollowLayout = false; chatSavedBound = null;
        if (chatSavedView != null) { chatSavedView.IsVisible = false; chatConversationView.IsVisible = true; chatSavedMessages.ItemsSource = ItemsView.Create(Array.Empty<ChatFavorite>(), f => f.Message.Body, f => $"{f.PeerId}:{f.Message.Id}"); }
        chatColumns.ResetDetails(); ResetChatDetailsContent(); chatGlobalMessages = chatGlobalSearchForced = chatFindMessages = chatHistoryLoading = false;
        chatMessageSearch.Text = ""; chatSearchCount.Text = "";
        chatSearch.Text = ""; chatSearchTimer.Stop(); chatGlobalSearchResults.Clear(); chatThreadSearchResults.Clear();
        chatSearchLanding.Child = null; chatSearchLandingKey = null; chatSearchLanding.IsVisible = false;
        chatGlobalSearchFrame.IsVisible = false; chatThreadSearchPopup.Reset(); chatGlobalResultsTransition.Reset(); chatGlobalResultsTransition.Show(chatGlobalSearchResults);
        chatSearchResultsTransition.Reset(); chatSearchResultsTransition.Show(chatThreadSearchResults);
        chatSidebarTransition.Reset(); chatSidebarTransition.Show(chatSidebarNormal); chatSidebarSearching = false; chatScanKey = chatResultKey = chatGlobalResultKey = null;
        RefreshChatGlobalSearchSurface(); RefreshChatSearchActivity(false);
        RefreshChatSaved();
        ClearChatSelection(); chatReply=null; RefreshChatReply(); CancelChatReads(); CloseChatGifs(); chatEmojiPopup?.Close(true); CloseChatMessageMenu(true);
        CancelChatPolling();
        chatAccountLifetime?.Cancel(); chatAccountLifetime?.Dispose(); chatAccountLifetime = null; chatSending = false;
        ++chatRevision; ++chatSearchRevision;
        if (chatHistory != null) { _ = chatHistory.DisposeAsync(); chatHistory = null; }
        ++chatAvatarGeneration;
        if(chatHeaderAvatar!=null)chatHeaderAvatar.Source=Icons.Source("user",Muted);
        chatPersistenceClosing = FlushChatPersistenceAsync();
        chatMedia.DiskCache=null; chatGifMedia = null; chatWorkspaceStore = null; chatWorkspace = ChatWorkspaceStore.Empty; chatReactions.Clear();
        chatAccountReady = false;
        chatAccountId = 0; chatLocalStore = null; chatRecipient = null;
        chatDrafts.Clear(); chatPending.Clear(); chatOutgoing.Clear(); chatMessageEntrances.Clear(); chatMessageOrder.Clear(); chatConversations.Clear(); chatMessages.Clear();
        chatThreadLoaded = false; chatWindowHasNewer=chatWindowBusy=false; ClearChatVisualCache();
        chatMessageItems.Clear(); chatInboxItems.Clear(); chatFolder=null; RefreshChatFolders(); chatReadWrites.Clear(); chatUnread.States.Clear(); chatReadRetryAfter.Clear();
        ClearChatMediaCache(clearSummaries: true); ClearChatAttachments();
        chatListCursor = chatMessageCursor = chatLatestCursor = null; chatPrependAnchor = null; chatScrollBottom = false;
        chatsBusy = chatThreadBusy = false; chatThreadRefreshRequested = false; chatSearchMode = false; chatListLoaded = false;
        if (chatName != null)
        {
            chatName.Text = "Твои сообщения"; chatFloatingDate.IsVisible = false;
            chatName.InvalidateMeasure(); chatColumns.Selected = false;
            chatRenderingDraft = true; chatInput.Text = ""; chatRenderingDraft = false;
            chatsStatus.Value = chatThreadStatus.Value = "";
            chatMore.IsVisible = chatNewMessages.IsVisible = false;
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
        await EnsureChatAccountAsync();
        if (demo || me == null || api.Session == null)
        { chatsStatus.Value = "Войди в SoundCloud, чтобы открыть переписку."; RefreshChatComposer(); return; }
        chatsBusy = true; RefreshChatLoading();
        var user = me; var session = api.Session; var revision = chatRevision; var token = ChatPollingToken();
        chatsStatus.Value = "";
        try
        {
            var result = await GetChatConversationsAsync(user.Id, token: token);
            if (!ChatCurrent(session, user.Id, revision) || token.IsCancellationRequested || !AllowChatRefresh) return;
            var unreadChanged = false;
            foreach (var conversation in result.Conversations)
            {
                if (token.IsCancellationRequested || !AllowChatRefresh) return;
                ChatMessage[]? recent = null;
                if (chatUnread.NeedsMessages(conversation) && chatUnread.States.ContainsKey(conversation.Recipient.Id))
                {
                    try { recent = (await GetChatMessagesAsync(user.Id, conversation.Recipient.Id, token: token)).Messages; }
                    catch (SoundCloudException) { continue; }
                }
                if (!ChatCurrent(session, user.Id, revision)) return;
                unreadChanged |= chatUnread.Observe(conversation, user.Id, recent);
            }
            if (unreadChanged) SaveChatState();
            if (!chatListLoaded) { chatListCursor = result.NextHref; chatListLoaded = true; }
            var previous = chatConversations.ToArray();
            chatConversations.Clear(); chatConversations.AddRange(result.Conversations.Concat(previous).DistinctBy(c => c.Id).OrderByDescending(c => c.LastMessage?.SentAt));
            if(chatRecipient!=null && chatConversations.FirstOrDefault(c=>c.Recipient.Id==chatRecipient.Id) is {} selected)
            {
                var avatarChanged=chatRecipient.AvatarUrl!=selected.Recipient.AvatarUrl;
                chatRecipient=selected.Recipient;chatName.Text=chatRecipient.Username;
                if(avatarChanged)Run(()=>UpdateChatAvatarAsync(selected.Recipient));
            }
            chatsStatus.Value = chatConversations.Count == 0 ? "Пока нет диалогов. Найди собеседника выше." : "";
            RenderChatInbox();
            if (chatHistory != null) await chatHistory.SaveDialogsAsync(chatConversations, token);
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
        RefreshChatSaved(); RefreshChatGlobalSearchSurface();
        if (chatSearchMode) return;
        var folderPeers=chatWorkspace.Folders.FirstOrDefault(f=>f.Id==chatFolder)?.Peers;
        var visible=chatConversations.Where(c=>folderPeers==null||folderPeers.Contains(c.Recipient.Id))
            .OrderBy(c=>{var index=Array.IndexOf(chatWorkspace.Pins,c.Recipient.Id);return index<0?int.MaxValue:index;})
            .ThenByDescending(c=>c.LastMessage?.SentAt).ToArray();
        PatchChatItems(chatInboxItems, visible, c => c.Id);
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
            var result = await GetChatConversationsAsync(user.Id, cursor, ChatReadToken());
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
        if (!chatSearchMode) { chatsStatus.Value = ""; RenderChatInbox(); if (chatGlobalMessages && chatGlobalSearchForced) await SearchChatMessagesAsync(); return; }
        RefreshChatGlobalSearchSurface();
        chatMore.IsVisible = false;
        if (chatGlobalMessages) { chatsStatus.Value = ""; chatFindMessages = true; chatFindPeer = null; await SearchChatMessagesAsync(); return; }
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
        await EnsureChatAccountAsync();
        if (chatRecipient?.Id == recipient.Id) { chatColumns.Selected = true; return; }
        ChatThreadSnapshot? snapshot;var selectingAccount=chatAccountId;var selectingSession=api.Session;long selectedRevision;
        await chatSelectionGate.WaitAsync(lifetime.Token);
        try
        {
        if(me?.Id!=selectingAccount||api.Session!=selectingSession||chatAccountId!=selectingAccount)return;
        if(chatVoiceBusy&&chatVoiceIdle is {} idle)await idle.Task;
        ++chatVoiceGeneration;
        await StopChatVoiceAsync();
        await StopChatVoicePlaybackAsync();
        if(chatVoiceRecorder!=null){await chatVoiceRecorder.DisposeAsync();chatVoiceRecorder=null;}
        if(me?.Id!=selectingAccount||api.Session!=selectingSession||chatAccountId!=selectingAccount)return;
        SaveChatDraft(); ClearChatSelection(); chatReply=null; RefreshChatReply(); CloseChatDetails(); CloseChatSearch(); chatMessageSearch.Text = ""; chatFavoritesOpen = false;
        chatWorkspace = chatWorkspace with { SavedOpen = false }; chatSavedView.IsVisible = false; chatConversationView.IsVisible = true; RefreshChatSaved();
        Window.PerformLayout(); RememberChatThread(); ClearChatAttachments(); CancelChatReads();
        chatComposerSurface.SnapNextLayout();
        foreach (var row in ChatRows) row.Clear();
        selectedRevision=++chatRevision; chatThreadBusy = chatsBusy = chatWindowBusy = false; chatThreadRefreshRequested = false; chatRecipient = recipient;
        chatColumns.Selected = true; chatName.Text = recipient.Username; Run(()=>UpdateChatAvatarAsync(recipient)); chatFloatingDate.IsVisible = false;
        chatMessages.Clear(); chatMessageOrder.Clear(); chatMessageCursor = chatLatestCursor = null; chatPrependAnchor = null;
        chatScrollBottom = chatWasAtBottom = chatFollowLayout = chatRestoreBubbleAnchor = false; chatRestoreOffset = null; chatRestoreAnchor = null; chatRestorePasses = 0;
        chatViewportWidth = chatViewportHeight = 0; chatWindowHasNewer=false;chatReactions=[];
        chatOlderCacheBoundary = null; chatOlderRetryAfter = default;
        chatThreadLoaded = chatThreadCache.TryGetValue(recipient.Id, out snapshot);
        if (snapshot != null)
        {
            chatReactions=snapshot.Reactions??[];chatWindowHasNewer=snapshot.HasNewer;
            chatMessages.AddRange(snapshot.Messages); chatMessageCursor = snapshot.Cursor; chatLatestCursor = snapshot.LatestCursor;
            foreach (var pair in snapshot.Order) chatMessageOrder[pair.Key] = pair.Value;
            if (snapshot.AtBottom) RequestChatBottom();
            else { chatRestoreOffset = snapshot.Offset; chatRestoreAnchor = snapshot.AnchorId; chatRestoreAnchorY = snapshot.AnchorY; }
        }
        }
        finally{chatSelectionGate.Release();}
        if (!chatThreadLoaded) await RestoreChatWindowAsync(recipient);
        if (chatRecipient?.Id != recipient.Id||me?.Id!=selectingAccount||api.Session!=selectingSession||chatRevision!=selectedRevision) return;
        chatWorkspace = chatWorkspace with { LastPeer = recipient.Id }; SaveChatWorkspace();
        chatThreadLoading.Reset();
        chatNewMessages.IsVisible = false;
        chatThreadStatus.Value = "";
        chatRenderingDraft = true;
        try { chatInput.Text = chatDrafts.GetValueOrDefault(recipient.Id, ""); }
        finally { chatRenderingDraft = false; }
        chatReply=chatWorkspace.Drafts?.GetValueOrDefault(recipient.Id)?.Reply; RefreshChatReply(); RestoreChatVoiceDraft(recipient.Id);
        RefreshChatComposer(); RefreshChatTimeline();
        // Returning from Saved Messages may reuse the same keyed timeline.
        // Its containers were cleared above, so explicitly bind them again even
        // when no message IDs or bodies changed on the server.
        chatMessagesView.ItemsSource.Invalidate();
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

    private async Task RefreshChatThreadAsync(bool initial = false, bool older = false)
    {
        if (chatThreadBusy || !AllowChatRefresh || chatRecipient == null || me == null || api.Session == null || older && chatMessageCursor == null) return;
        var recipient = chatRecipient; var user = me; var session = api.Session; var revision = chatRevision;
        var cursor = older ? chatMessageCursor : null;
        chatThreadBusy = true; chatThreadRefreshRequested = false; RefreshChatLoading(); RefreshChatComposer();
        var viewer = chatMessagesView.FindVisualChild<ScrollViewer>() as ScrollViewer;
        var nearBottom = chatRestoreOffset == null && (chatScrollBottom || viewer == null || SmoothScroll.Maximum(viewer) - viewer.VerticalOffset < 90);
        var before = viewer == null ? ((double, double)?)null : (SmoothScroll.Maximum(viewer), viewer.VerticalOffset);
        using var read = CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken(), ChatPollingToken());
        try
        {
            var result = await GetChatMessagesAsync(user.Id, recipient.Id, cursor, read.Token);
            if (!ChatCurrent(session, user.Id, revision) || page.Value != Page.Chats || read.IsCancellationRequested || !AllowChatRefresh) return;
            await CacheChatPageAsync(user.Id, recipient.Id, result, cursor, read.Token);
            if (!ChatCurrent(session,user.Id,revision)) return;
            chatThreadLoaded = true;
            if (!older) chatLatestCursor = result.NextHref;
            var oldIds = chatMessages.Select(m => m.Id).ToHashSet();
            if(!older && oldIds.Count>0 && result.Messages.Length>0 && !result.Messages.Any(m=>oldIds.Contains(m.Id)))
            {
                var newer=result.Messages.ToList();var bridge=result.NextHref;var visited=new HashSet<string>();
                while(bridge!=null && visited.Add(bridge))
                {
                    var segment=await GetChatMessagesAsync(user.Id,recipient.Id,bridge,read.Token);
                    await CacheChatPageAsync(user.Id,recipient.Id,segment,bridge,read.Token);
                    if(!ChatCurrent(session,user.Id,revision))return;
                    if(newer.Count<200)newer.AddRange(segment.Messages.TakeLast(200-newer.Count));
                    if(segment.Messages.Any(m=>oldIds.Contains(m.Id)))break;
                    bridge=segment.NextHref;
                }
                result=result with {Messages=newer.DistinctBy(m=>m.Id).OrderBy(m=>m.SentAt).ToArray()};
            }
            var added = result.Messages.Where(m => !oldIds.Contains(m.Id)).ToArray();
            foreach(var resolved in result.Messages.Where(m=>m.ClientContent!=null))
            {
                var existing=chatMessages.FindIndex(m=>m.Id==resolved.Id && m.Body==resolved.Body);
                if(existing>=0)chatMessages[existing]=resolved;
            }
            var addedVisible=added.Any(m=>!ChatProtocol.Project(m,user.Id,recipient.Id).IsReaction);
            var unseenNewer=!older&&!nearBottom&&(chatWindowHasNewer||chatMessages.Count+added.Length>200);
            var followOlder = older && (chatFollowLayout || chatScrollBottom || ChatIsAtBottom());
            var anchoredOlder = older && added.Length > 0 && !followOlder && CaptureChatReadingAnchor(viewer);
            if(!unseenNewer)chatMessages.AddRange(added);
            else {chatWindowHasNewer=true;if(addedVisible)chatNewMessages.IsVisible=true;}
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
            var ordered = chatMessages.OrderBy(ChatOrder).ToArray(); chatMessages.Clear();
            if (older && !followOlder && ordered.Length > 200) chatWindowHasNewer = true;
            chatMessages.AddRange(ordered.Length>200 ? (older && !followOlder ? ordered.Take(200) : ordered.TakeLast(200)) : ordered);
            RefreshChatTimeline();
            if (anchoredOlder && chatRestoreAnchor != null)
            {
                var anchorIndex = chatMessageItems.ToList().FindIndex(item => item.Id == chatRestoreAnchor);
                if (anchorIndex >= 0) chatMessagesView.ScrollIntoView(anchorIndex);
            }
            if (initial || followOlder || !older && nearBottom) RequestChatBottom();
            else if (older && added.Length > 0 && !anchoredOlder && before.HasValue) chatPrependAnchor = before;
            else if (!older && addedVisible) chatNewMessages.IsVisible = true;
            chatThreadStatus.Value = chatMessages.Count==0?"Напиши первое сообщение":chatMessageItems.Count==0?"Загрузи предыдущие сообщения, чтобы увидеть переписку.":"";
            if ((unseenNewer?result.Messages.LastOrDefault():chatMessages.LastOrDefault()) is { } latest)
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
            RememberChatThread(); ReconcilePendingChat(); await ConfirmCachedPendingChatAsync(); RefreshChatComposer();
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
        finally
        {
            if (chatRevision == revision)
            {
                chatThreadBusy = false; chatPresentationDirty = true;
                chatViewport.InvalidateVisual(); RefreshChatLoading(); RefreshChatComposer();
            }
        }
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
        chatRestoreOffset = null; chatRestoreAnchor = null; chatRestoreBubbleAnchor = false;
        chatPrependAnchor = null; chatScrollBottom = true; chatLastBottomExtent = -1; chatViewport.InvalidateArrange();
    }

    private void ArrangeChatScroll()
    {
        if (disposed || !workspace.IsVisible || page.Value != Page.Chats || Window.WindowState == WindowState.Minimized) return;
        if (chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer currentViewer) return;
        var geometry = (currentViewer.Bounds, currentViewer.VerticalOffset, SmoothScroll.Maximum(currentViewer), chatMediaLoads.Count);
        if (!chatPresentationDirty && chatPresentationGeometry == geometry && !chatColumns.Animating && !chatComposerSurface.IsResizing) return;
        chatPresentationDirty = false; chatPresentationGeometry = geometry;
        foreach (var row in ChatRows.Where(r => !r.Bound && r.Message != null).ToArray()) row.Clear();
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
        if(chatRecipient!=null)
            foreach(var row in ChatRows.Where(r=>r.Bound&&r.Message!=null&&r.Root.Bounds.Bottom>viewer.Bounds.Y&&r.Root.Bounds.Y<viewer.Bounds.Bottom).OrderBy(r=>r.Root.Bounds.Y))
                QueueChatMediaPreview(row.Message!,chatRecipient.Id);
        if(chatRestoreOffset==null&&!chatScrollBottom&&!chatWindowBusy&&!chatThreadBusy&&chatMessages.Count>0)
        {
            var prefetch = Math.Clamp(viewer.ActualHeight * .5, 96, 400);
            if(viewer.VerticalOffset<prefetch && DateTimeOffset.UtcNow >= chatOlderRetryAfter &&
                (chatMessageCursor != null || chatHistory != null && chatOlderCacheBoundary != chatMessages[0].Id))
                Run(()=>ShiftChatWindowAsync(true));
            else if(chatWindowHasNewer&&SmoothScroll.Maximum(viewer)-viewer.VerticalOffset<48)Run(()=>ShiftChatWindowAsync(false));
        }
        if (Window.IsActive && chatWasAtBottom && chatThreadLoaded && !chatThreadBusy && me != null && api.Session != null && chatRecipient != null && chatUnread.States.GetValueOrDefault(chatRecipient.Id) is { Count: > 0 })
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
            var anchorY = anchor == null ? 0 : chatRestoreBubbleAnchor ? anchor.Bubble.Bounds.Y : anchor.Root.Bounds.Y;
            var requested = anchor != null ? viewer.VerticalOffset + anchorY - viewer.Bounds.Y - chatRestoreAnchorY : restore;
            var target = Math.Clamp(requested, 0, maximum);
            if (Math.Abs(target - viewer.VerticalOffset) > .1)
            { viewer.SetScrollOffsets(0, target); changed = true; }
            if (anchor != null && Math.Abs(anchorY - viewer.Bounds.Y - chatRestoreAnchorY) > .5) changed = true;
            // A larger window can make the old anchor unreachable at the end of
            // history. Accept the nearest edge after bounded extent refinement.
            var animating = chatColumns.Animating || ChatRows.Any(row => row.Bound && (row.Reactions.Animating || row.Content.IsResizing));
            if (++chatRestorePasses >= 3 && !animating && (!changed || chatRestorePasses >= 16)) { chatRestoreOffset = null; chatRestoreAnchor = null; chatRestoreBubbleAnchor = false; }
            // Keep the anchor through the drawer animation, but only request
            // another layout when its offset actually needs correcting. The
            // animation already schedules the next frame; reporting a change
            // here forced eight redundant restore passes on every stable frame.
            else if (!animating) changed = true;
        }
        if (chatRestoreOffset == null && chatWasAtBottom &&
            (Math.Abs(chatViewportWidth - viewer.ActualWidth) > .1 || Math.Abs(chatViewportHeight - viewer.ActualHeight) > .1))
            chatScrollBottom = true;
        chatViewportWidth = viewer.ActualWidth; chatViewportHeight = viewer.ActualHeight;
        if (chatFollowLayout) chatScrollBottom = true;
        if (chatScrollBottom)
        {
            var settled = Math.Abs(maximum - chatLastBottomExtent) < .1 && Math.Abs(maximum - viewer.VerticalOffset) < .1;
            chatLastBottomExtent = maximum;
            chatNewMessages.IsVisible = false;
            if (Math.Abs(maximum - viewer.VerticalOffset) > .1) viewer.SetScrollOffsets(0, maximum);
            if (settled) { chatScrollBottom = false; chatLastBottomExtent = -1; if (!chatComposerSurface.IsResizing && !chatColumns.Animating) chatFollowLayout = false; }
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
        chatPresentationDirty = true;
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
        if (!chatAccountReady || chatLocalStore == null || chatAccountId <= 0) return;
        if (chatRecipient != null && !chatRenderingDraft)
        {
            if (chatInput.Text.Length == 0) chatDrafts.Remove(chatRecipient.Id);
            else chatDrafts[chatRecipient.Id] = chatInput.Text.Length <= 20000 ? chatInput.Text : chatInput.Text[..20000];
        }
        if(chatRecipient!=null)
        {
            var drafts=chatWorkspace.Drafts??new Dictionary<long,ChatDraft>();
            var draft = new ChatDraft(chatInput.Text,chatReply,chatVoiceDraft);
            if (draft.IsEmpty) drafts.Remove(chatRecipient.Id); else drafts[chatRecipient.Id] = draft;
            chatWorkspace=chatWorkspace with {Drafts=drafts};SaveChatWorkspace();
        }
        SaveChatState();
    }
    private void SaveChatState()
    {
        var store = chatLocalStore;
        if (!chatAccountReady || store == null) return;
        var snapshot = new ChatLocalState(chatDrafts.Where(p => p.Value.Length > 0).ToDictionary(), chatPending.Values.ToArray(), chatUnread.States.TakeLast(500).ToDictionary());
        var save = store.SaveAsync(snapshot, preferences.ChatSaveDrafts);
        Run(async () =>
        {
            try { await save; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { if (!disposed && chatLocalStore == store) chatThreadStatus.Value = "Не удалось сохранить черновик или исходящее сообщение."; }
        });
    }
    private void RefreshChatComposer()
    {
        var available = !demo && me != null && api.Session != null && chatRecipient != null && !chatSending;
        var pending = chatRecipient != null && chatPending.ContainsKey(chatRecipient.Id);
        if(chatVoiceBar!=null)chatVoiceBar.IsEnabled=available&&!pending&&!chatVoiceBusy;
        chatComposer.IsVisible = chatRecipient != null && !chatFavoritesOpen;
        chatOptions.IsVisible = !chatFavoritesOpen; chatOptions.IsEnabled = chatRecipient != null;
        if (chatFavoritesOpen) chatComposerLayout.SetVoiceMode(false, snap:true);
        var recording=chatVoiceRecorder is {Recording:true};
        var empty=chatInput.Text.Trim().Length==0&&chatSelectedFiles.Length==0&&chatSelectedGif==null&&chatVoiceDraft==null;
        chatInput.IsEnabled = available && !pending && !recording && !chatVoiceBusy;
        chatAttach.IsEnabled = chatGif.IsEnabled = available && !pending && !chatImporting && !recording && !chatVoiceBusy;
        if (!chatGif.IsVisible) { chatGif.IsVisible = true; chatComposerLayout.InvalidateMeasure(); }
        chatSend.ToolTip = new TextBlock().Text(preferences.ChatSendOnEnter ? "Отправить · Enter (Shift+Enter — новая строка)" : "Отправить · Ctrl+Enter");
        if(recording)chatSend.ToolTip=new TextBlock().Text("Завершить запись");
        else if(empty)chatSend.ToolTip=new TextBlock().Text("Записать голосовое · до 5 минут");
        if (!chatVoiceBusy || recording) chatSendGlyph.Show(recording?"check":empty?"microphone":"paper-plane-tilt");
        chatSend.IsEnabled = available && !pending && !chatImporting && !(chatThreadBusy && chatMessages.Count == 0) &&
            chatInput.Text.Length <= 20000 && !chatVoiceBusy;
        RefreshChatComposerSurface();
        RefreshChatMusicComposer();
    }

    private void DisposeChats()
    {
        chatEmojiEditor?.Dispose();
        chatEmojiSearchTimer?.Stop(); CloseChatMessageMenu(true); chatEmojiPopup?.Close(true); ChatEmojiArtwork.Clear();
        SaveChatDraft(); CleanupChatVoiceFiles(); RememberChatThread(); if(chatHistory!=null){_=chatHistory.DisposeAsync();chatHistory=null;} chatHistorySearch?.Cancel(); chatTimer.Dispose(); chatSearchTimer.Dispose(); chatMessageSearchTimer.Dispose(); chatDraftTimer.Dispose(); CancelChatReads();
        chatMusicRecognizeTimer.Dispose(); chatMusicEditor?.Dispose();
        chatAttachInteraction.Dispose(); chatMusicSearchTimer.Dispose(); chatVideoTimer.Dispose(); chatVideoExpansionClock.Stop(); chatVideoSpeedClock.Stop();
        Window.FrameRendered -= ArrangeChatScroll; CloseChatGifs();
        Window.FrameRendered -= QueueChatLibraryPreviews; ClearChatLibraryPreviews();
        if (chatDateViewer != null) chatDateViewer.ScrollChanged -= OnChatDateScroll;
        chatDateLayer.Dispose(); chatVoiceTimer.Dispose(); ++chatVoiceGeneration; if(chatVoiceRecorder!=null)_=chatVoiceRecorder.DisposeAsync(); if(chatVoicePlayer!=null)_=chatVoicePlayer.DisposeAsync();
        chatAttachmentPreviewLoad?.Cancel(); chatAttachmentPreviewLoad?.Dispose();
        chatAttachmentStaging.Dispose();
        foreach (var row in ChatRows) row.Clear();
        CancelChatPolling();
        chatAccountLifetime?.Cancel(); chatAccountLifetime?.Dispose();
        chatInboxLoading.Dispose(); chatThreadLoading.Dispose(); ClearChatMediaCache(clearSummaries: true); ClearChatVisualCache(); chatMedia.Dispose(); klipy.Dispose();
    }
}
