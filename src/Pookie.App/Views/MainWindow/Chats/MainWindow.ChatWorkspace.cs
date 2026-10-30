using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private FrameworkElement chatFoldersSurface = null!;
    private StackPanel chatFolderTabs = null!, chatDetailsBody = null!, chatSelectionBar = null!;
    private TextBlock chatDetailsTitle = null!, chatSelectionLabel = null!;
    private GlassCard chatDetailsCard = null!;
    private ChatProfileBackdrop chatProfileBackdrop = null!;
    private ChatPanelTransition chatDetailsTransition = null!, chatMediaTransition = null!;
    private ChatTabs? chatMediaPill;
    private long? chatInfoPeer;
    private StackPanel chatInfoBody = null!;
    private Image chatInfoAvatar = null!;
    private TextBlock chatInfoName = null!, chatInfoProgress = null!;
    private ChatTabs chatInfoTabs = null!;
    private ChatPanelTransition chatInfoMediaTransition = null!;
    private SoundCloudUser? chatInfoUser;
    private long chatInfoRevision;
    private ScrollViewer chatDetailsScroll = null!;
    private StackPanel? chatMediaLibrary;
    private FrameworkElement? chatMediaEmpty, chatMediaMore;
    private TextBlock? chatHistoryProgress;
    private bool chatHistoryLoading;
    private TextBox chatMessageSearch = null!;
    private string? chatFolder;
    private long chatDetailsGeneration;
    private bool chatFindMessages;
    private string? chatMediaLibraryKind;
    private long? chatFindPeer;
    private CancellationTokenSource? chatHistorySearch;
    private readonly HashSet<string> chatSelected = [];
    private ChatReplyReference? chatReply;
    private long chatAvatarGeneration;
    private FrameworkElement ChatFolderView()
    {
        chatFolderTabs = new StackPanel().Horizontal().Spacing(5);
        RefreshChatFolders();
        return chatFoldersSurface = new ScrollViewer().HorizontalScroll(ScrollMode.Auto).VerticalScroll(ScrollMode.Disabled).Background(Color.Transparent).BorderThickness(0).Padding(0).Content(chatFolderTabs);
    }
    private void RefreshChatFolders()
    {
        if (chatFolderTabs == null) return;
        chatFolderTabs.Clear();
        void Tab(string text, string? id) => chatFolderTabs.Add(TrackButtons.Action(new TextBlock().Text(text).FontSize(12), () => { chatFolder = id; RefreshChatFolders(); RenderChatInbox(); }, chatFolder == id ? TrackButtons.Tonal : TrackButtons.Text, 30).Padding(10, 0));
        Tab("Все", null);
        foreach (var folder in chatWorkspace.Folders)
        {
            Tab(folder.Name, folder.Id);
            var tab = (Button)chatFolderTabs.Children.Last();
            var edit = new Command("chat.folder.edit", "Изменить папку");
            tab.Commands.Register(edit, () => OpenChatFolderEditor(folder));
            tab.ContextMenu = new ContextMenu().Item(edit);
        }
        chatFolderTabs.Add(TrackButtons.Icon(Icons.View("plus", 16), () => OpenChatFolderEditor(null), TrackButtons.Text, 30).ToolTip("Создать папку"));
    }

    private FrameworkElement CreateChatDetails()
    {
        // Build reusable controls with the chat workspace, before a header
        // click needs to start the drawer animation.
        chatInfoBody = CreateChatInfoBody();
        chatDetailsTitle = new TextBlock().FontSize(17).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        chatDetailsBody = new StackPanel().Vertical().Spacing(10);
        chatDetailsTransition = new ChatPanelTransition(); chatDetailsTransition.Show(chatDetailsBody);
        var close = TrackButtons.Icon(Icons.View("x-mark", 20), CloseChatDetails, TrackButtons.Text, 36);
        // A drawer is a surface, not a hoverable card. Repainting its full-size
        // glass reflection while the close button leaves the pointer competes
        // with the slide animation, especially at high DPI.
        chatProfileBackdrop = new ChatProfileBackdrop(ChatGlassSurface) { HoverEnabled = false };
        var scroll = chatDetailsScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(12, 0, 12, 12).Content(chatDetailsTransition);
        scroll.ScrollChanged += () => chatProfileBackdrop.ScrollOffset = scroll.VerticalOffset;
        chatDetailsCard = chatProfileBackdrop.CornerRadius(24).Padding(0).Margin(0, 0, 0, 16).Child(
            new Border().CornerRadius(24).ClipToBounds().BorderThickness(0).Padding(0).Background(Color.Transparent).Child(
            new DockPanel().LastChildFill().Spacing(0).Children(
            new Grid().Columns("36,*").Rows("52").Spacing(8).Padding(12, 0).Children(close.Column(0).CenterVertical(), chatDetailsTitle.Column(1).CenterVertical()).DockTop(),
            scroll)));
        chatDetailsCard.Transitions = [];
        return chatDetailsCard;
    }
    private void ResetChatDetailsContent()
    {
        chatInfoPeer = null; ++chatInfoRevision; chatInfoUser = null;
        chatInfoAvatar.Source = null; chatInfoName.Text = "";
        if (!ReferenceEquals(chatMediaTransition,chatInfoMediaTransition)) chatInfoMediaTransition.Reset();
        ClearChatLibraryPreviews();
        chatProfileBackdrop.Reset();
        if (chatMediaLibrary is { } library) compactTrackRows.RemoveAll(row => library.Children.Contains(row.Root));
        chatMediaLibrary = null; chatMediaLibraryKind = null; chatMediaPill = null;
        chatHistoryProgress = null;
        chatMediaTransition?.Reset(); chatDetailsTransition.Reset();
        chatDetailsBody = new StackPanel().Vertical().Spacing(12); chatDetailsTransition.Show(chatDetailsBody);
        chatDetailsTitle.Text = "";
    }
    private void CaptureChatPanelAnchor()
    {
        if (page.Value == Page.Chats && (chatWasAtBottom || chatScrollBottom || ChatIsAtBottom())) chatFollowLayout = true;
        if (page.Value != Page.Chats || chatScrollBottom || chatRestoreOffset != null || chatMessagesView.FindVisualChild<ScrollViewer>() is not ScrollViewer viewer) return;
        var row = ChatRows.Where(r => r.Bound && r.Message != null && r.Root.Bounds.Bottom > viewer.Bounds.Y + 1 && r.Root.Bounds.Y < viewer.Bounds.Bottom).OrderBy(r => r.Root.Bounds.Y).FirstOrDefault();
        if (row == null) return;
        chatRestoreBubbleAnchor = false;
        chatRestoreAnchor = row.Message!.Id; chatRestoreAnchorY = row.Root.Bounds.Y - viewer.Bounds.Y; chatRestoreOffset = viewer.VerticalOffset; chatRestorePasses = 0;
    }
    private void OpenChatDetails(string title, StackPanel? body = null)
    {
        chatInfoPeer = null; ++chatInfoRevision; ClearChatLibraryPreviews();
        chatProfileBackdrop.Reset();
        CaptureChatPanelAnchor();
        // A hidden outgoing body has no transition to show. The drawer already
        // owns the opening slide/fade; keep section transitions for open panels.
        if (!chatColumns.DetailsOpen && chatColumns.Reveal <= 0) chatDetailsTransition.Reset();
        ++chatDetailsGeneration; chatHistorySearch?.Cancel(); if (chatMediaLibrary is { } oldLibrary) compactTrackRows.RemoveAll(row => oldLibrary.Children.Contains(row.Root)); chatMediaLibrary = null; chatMediaPill = null; chatHistoryProgress = null; chatDetailsTitle.Text = title; chatDetailsBody = body ?? new StackPanel().Vertical().Spacing(12); chatDetailsTransition.Show(chatDetailsBody);
        if (chatDetailsCard.FindVisualChild<ScrollViewer>() is ScrollViewer scroll) scroll.SetScrollOffsets(0, 0);
        chatColumns.DetailsOpen = true;
    }
    private void CloseChatDetails()
    { chatInfoPeer = null; ++chatInfoRevision; ClearChatLibraryPreviews(); ++chatDetailsGeneration; var open = chatColumns.DetailsOpen; if (open) chatHistorySearch?.Cancel(); if (open) CaptureChatPanelAnchor(); chatColumns.DetailsOpen = false; chatFindMessages = chatThreadSearchOpen || chatGlobalMessages && chatSearch.Text.Length > 0; if (open) { if (chatRecipient != null) chatInput.Focus(); else chatSearch.Focus(); } }
    private void OpenChatFolderEditor(ChatFolder? folder)
    {
        OpenChatDetails(folder == null ? "Новая папка" : "Изменить папку");
        var name = new TextBox().Text(folder?.Name ?? "").Placeholder("Название папки").MaxLength(40);
        chatDetailsBody.Add(MaterialSearchField.CreateGlass(name));
        var members = folder?.Peers.ToHashSet() ?? [];
        var choices = new StackPanel().Vertical().Spacing(4);
        foreach (var dialog in chatConversations)
        {
            var peer = dialog.Recipient.Id; Button button = null!;
            button = TrackButtons.Action(new TextBlock().Text(dialog.Recipient.Username).FontSize(13).TextTrimming(TextTrimming.CharacterEllipsis), () =>
            { if (!members.Add(peer)) members.Remove(peer); TrackButtons.SetVariant(button, members.Contains(peer) ? TrackButtons.Tonal : TrackButtons.Text); }, members.Contains(peer) ? TrackButtons.Tonal : TrackButtons.Text, 38).Padding(10, 0);
            choices.Add(button);
        }
        chatDetailsBody.Add(TrackButtons.Action(new TextBlock().Text("Сохранить"), () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            var next = new ChatFolder(folder?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), members.ToArray());
            chatWorkspace = chatWorkspace with { Folders = chatWorkspace.Folders.Where(f => f.Id != next.Id).Append(next).Take(30).ToArray() };
            SaveChatWorkspace(); RefreshChatFolders(); RenderChatInbox(); CloseChatDetails();
        }, TrackButtons.Tonal, 36));
        if (folder != null) chatDetailsBody.Add(TrackButtons.Action(new TextBlock().Text("Удалить папку"), () =>
        { chatWorkspace = chatWorkspace with { Folders = chatWorkspace.Folders.Where(f => f.Id != folder.Id).ToArray() }; if (chatFolder == folder.Id) chatFolder = null; SaveChatWorkspace(); RefreshChatFolders(); RenderChatInbox(); CloseChatDetails(); }, TrackButtons.Text, 36));
        chatDetailsBody.Add(choices); name.Focus();
    }
    private void ToggleChatPin(long peer)
    {
        chatWorkspace = chatWorkspace with { Pins = chatWorkspace.Pins.Contains(peer) ? chatWorkspace.Pins.Where(p => p != peer).ToArray() : chatWorkspace.Pins.Append(peer).ToArray() };
        SaveChatWorkspace(); RenderChatInbox();
    }
    private void ChatFavoriteMessage(ChatMessage message)
    {
        if (chatRecipient == null || ChatOutgoing(message) != null) return;
        var peer = chatRecipient.Id;
        var exists = chatWorkspace.Favorites.Any(f => f.PeerId == peer && f.Message.Id == message.Id);
        chatWorkspace = chatWorkspace with { Favorites = exists ? chatWorkspace.Favorites.Where(f => f.PeerId != peer || f.Message.Id != message.Id).ToArray() : chatWorkspace.Favorites.Append(new(peer, message, DateTimeOffset.UtcNow)).ToArray() };
        SaveChatWorkspace(); RefreshChatFavoriteMedia(); RefreshChatSaved();
    }
    private static TextBlock ChatDetailText(string text) => new TextBlock().Text(text).FontSize(13).TextWrapping(TextWrapping.Wrap).MaxHeight(40).TextTrimming(TextTrimming.CharacterEllipsis);
    private string ChatCopyText(ChatMessage message)
    {
        if (ChatMediaReference.Parse(message.Body) != null)
        {
            var summary = ChatMediaSummaryFor(message, chatRecipient?.Id ?? 0);
            return !string.IsNullOrEmpty(summary?.Caption) ? summary.Caption : summary?.Label ?? "Вложение";
        }
        return ChatProtocol.Project(message, chatAccountId, chatRecipient?.Id ?? 0).Text;
    }
    private FrameworkElement ChatSelectionView()
    {
        chatSelectionLabel = new TextBlock().FontSize(13).CenterVertical();
        chatSelectionBar = new StackPanel().Horizontal().Spacing(6).Children(chatSelectionLabel,
            TrackButtons.Icon(Icons.View("copy", 18), () => CopyText(string.Join(Environment.NewLine, chatMessageItems.Where(m => chatSelected.Contains(m.Id)).Select(m => ChatCopyText(m.Message)))), TrackButtons.Glass, 34).ToolTip("Копировать по порядку"),
            TrackButtons.Icon(Icons.View("heart", 18), () => { foreach (var item in chatMessageItems.Where(m => chatSelected.Contains(m.Id))) if (!chatWorkspace.Favorites.Any(f => f.PeerId == chatRecipient?.Id && f.Message.Id == item.Id)) ChatFavoriteMessage(item.Message); ClearChatSelection(); }, TrackButtons.Glass, 34).ToolTip("В избранное"),
            TrackButtons.Icon(Icons.View("x-mark", 18), ClearChatSelection, TrackButtons.Text, 34));
        chatSelectionBar.IsVisible = false; return chatSelectionBar;
    }
    private void ToggleChatSelection(ChatMessage message)
    { if (!chatSelected.Add(message.Id)) chatSelected.Remove(message.Id); RefreshChatSelection(); }
    private void ClearChatSelection() { chatSelected.Clear(); RefreshChatSelection(); }
    private void RefreshChatSelection()
    {
        if (chatSelectionBar == null) return;
        chatSelectionBar.IsVisible = chatSelected.Count > 0; chatSelectionLabel.Text = $"Выбрано: {chatSelected.Count}";
        foreach (var row in ChatRows) row.Root.Background = chatSelected.Contains(row.Message?.Id ?? "") ? Color.FromArgb(35, 128, 196, 249) : Color.Transparent;
    }
    private async Task DownloadChatSearchHistoryAsync()
    {
        if (chatHistory == null || me == null || api.Session == null) return;
        chatHistorySearch?.Cancel(); chatHistorySearch?.Dispose(); chatHistorySearch = CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken());
        var token = chatHistorySearch.Token; var store = chatHistory; var account = chatAccountId; var total = 0;var incomplete=false;
        var progressControl = chatHistoryProgress;
        chatHistoryLoading = true;
        RefreshChatSearchActivity(chatFindMessages);
        if (chatHistoryProgress != null) { chatHistoryProgress.Text = chatFindMessages ? "Поиск…" : "Загрузка вложений…"; chatHistoryProgress.IsVisible = true; }
        try
        {
            var scope = chatFindPeer;
            var peers = chatConversations.Select(c => c.Recipient.Id).Where(p => scope == null || p == scope).ToHashSet();
            if (scope != null) peers.Add(scope.Value);
            if (scope == null)
            {
                var listCursor = chatListCursor; var listed = new HashSet<string>();
                while (listCursor != null && listed.Add(listCursor))
                {
                    var dialogs = await GetChatConversationsAsync(account, listCursor, token);
                    await store.SaveDialogsAsync(dialogs.Conversations, token);
                    foreach (var dialog in dialogs.Conversations)
                    {
                        peers.Add(dialog.Recipient.Id);
                        if (!chatConversations.Any(c => c.Id == dialog.Id)) chatConversations.Add(dialog);
                    }
                    listCursor = dialogs.NextHref;
                }
            }
            foreach (var peer in peers)
            {
                var cached = await store.LoadWindowAsync(peer, token: token);
                if(!cached.Complete)
                {
                string? cursor = cached.Cursor; var visited = new HashSet<string>();
                do
                {
                    token.ThrowIfCancellationRequested();
                    var page = await GetChatMessagesAsync(account, peer, cursor, token);
                    await store.SavePageAsync(account, peer, page, cursor, token); total += page.Messages.Length;
                    if (chatHistory != store) return;
                    if (chatHistoryProgress != null) chatHistoryProgress.Text = $"Проверено сообщений: {total}";
                    if (chatFindMessages) await SearchChatMessagesAsync(scanning: true); else if (chatMediaLibraryKind is { } kind) await RenderChatMediaLibraryAsync(kind);
                    cursor = page.NextHref;
                } while (cursor != null && visited.Add(cursor));
                }
                incomplete |= !await ResolveStoredChatEnvelopesAsync(store,account,peer,token);
                if(chatFindMessages)await SearchChatMessagesAsync(scanning:true);
            }
            if (chatHistory == store && chatHistorySearch?.Token == token)
            {
                if(progressControl!=null)progressControl.Text=incomplete ? "Часть сообщений недоступна" : "Поиск завершён";
                if(incomplete)
                {
                    const string detail="Часть сообщений Pookie не загрузилась. Повтори поиск позже.";
                    if(chatThreadSearchOpen)chatThreadStatus.Value=detail;else chatsStatus.Value=detail;
                }
            }
        }
        catch (OperationCanceledException) { if (chatHistory == store && chatHistorySearch?.Token == token) { if (chatHistoryProgress != null) chatHistoryProgress.Text = "Поиск остановлен"; } }
        catch (Exception error) { if (chatHistory == store && chatHistorySearch?.Token == token) { if (chatThreadSearchOpen) chatThreadStatus.Value = ChatActionError(error); else chatsStatus.Value = ChatActionError(error); chatScanKey = null; } }
        finally
        {
            if (chatHistory == store && chatAccountId == account && chatHistorySearch?.Token == token)
            {
                chatHistoryLoading = false;
                RefreshChatSearchActivity(false);
                if (chatHistoryProgress != null) chatHistoryProgress.IsVisible = false;
            }
        }
    }
    private async Task JumpChatMessageAsync(long peer, string id, bool keepDetails = false)
    {
        var account = chatAccountId;
        var user = chatConversations.FirstOrDefault(c => c.Recipient.Id == peer)?.Recipient;
        if (user == null)
        {
            try { user = await api.GetUserAsync(peer, ChatReadToken()); }
            catch (Exception error) when (error is HttpRequestException or SoundCloudException) { chatThreadStatus.Value = "Собеседник пока недоступен. Избранное сохранено на устройстве."; return; }
        }
        if (account != chatAccountId) return;
        await SelectChatAsync(user);
        if (chatHistory == null || account != chatAccountId || chatRecipient?.Id != peer) return;
        if (!chatMessages.Any(m => m.Id == id))
        {
            var cached = await chatHistory.LoadWindowAsync(peer, id, token: ChatReadToken());
            if (!cached.Messages.Any(m => m.Id == id))
            {
                chatThreadStatus.Value = "Загружаем оригинал сообщения…";
                var cursor = (string?)null; var visited = new HashSet<string>();
                do
                {
                    var result = await GetChatMessagesAsync(chatAccountId, peer, cursor, ChatReadToken());
                    await CacheChatPageAsync(chatAccountId, peer, result, cursor, ChatReadToken());
                    if (chatRecipient?.Id != peer) return;
                    if (result.Messages.Any(m => m.Id == id)) break;
                    cursor = result.NextHref;
                } while (cursor != null && visited.Add(cursor));
                cached = await chatHistory.LoadWindowAsync(peer, id, token: ChatReadToken());
            }
            if (chatRecipient?.Id != peer) return;
            if (!cached.Messages.Any(m => m.Id == id)) { chatThreadStatus.Value = "Оригинал пока недоступен. Сохранённая цитата остаётся в ответе."; return; }
            chatMessages.Clear(); chatMessages.AddRange(cached.Messages); RefreshChatTimeline();
        }
        if (!keepDetails) CloseChatDetails();
        chatScrollBottom = chatFollowLayout = chatWasAtBottom = false; chatRestoreOffset = null;
        var index = chatMessageItems.ToList().FindIndex(m => m.Id == id);
        if (index >= 0) { chatMessagesView.ScrollIntoView(index); Window.PerformLayout(); foreach (var row in ChatRows.Where(r => r.Message?.Id == id)) row.Highlight(); }
        chatThreadStatus.Value = "";
    }
    private StackPanel CreateChatInfoBody()
    {
        chatInfoAvatar = new Image().Width(96).Height(96).StretchMode(Stretch.UniformToFill);
        chatInfoName = new TextBlock().FontSize(20).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).CenterHorizontal();
        chatInfoTabs = new ChatTabs();
        foreach (var kind in new[] { "Музыка", "Фото", "Видео", "Файлы", "Ссылки" })
            chatInfoTabs.AddTab(kind, () => Run(() => RenderChatMediaLibraryAsync(kind)));
        chatInfoMediaTransition = new ChatPanelTransition();
        chatInfoProgress = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        return new StackPanel().Vertical().Spacing(12).Children(
            new StackPanel().Vertical().Spacing(10).Margin(0, 8, 0, 16).Children(
                new Border().Width(96).Height(96).CornerRadius(48).ClipToBounds().Child(chatInfoAvatar).CenterHorizontal(),
                chatInfoName,
                TrackButtons.Action(new TextBlock().Text("Открыть профиль").FontSize(12).Foreground(ChatAccent),
                    () => { if (chatInfoUser is { } user) Run(() => OpenUserProfileAsync(user)); }, TrackButtons.Text, 30).CenterHorizontal()),
            chatInfoTabs, chatInfoMediaTransition, chatInfoProgress);
    }
    private void OpenChatInfo()
    {
        if (chatColumns.DetailsOpen && chatRecipient != null && chatInfoPeer == chatRecipient.Id) return;
        CloseChatSearch(); chatFindMessages = false; OpenChatDetails("Информация",chatInfoBody);
        if (chatRecipient is not { } peer) return;
        chatInfoPeer = peer.Id; chatInfoUser = peer;
        chatInfoAvatar.Source = chatHeaderAvatar.Source; chatInfoName.Text = peer.Username;
        chatInfoTabs.Select(0,animate:false); chatMediaPill = chatInfoTabs;
        chatMediaLibrary = new StackPanel().Vertical().Spacing(4);
        chatMediaTransition = chatInfoMediaTransition; chatMediaTransition.Reset();
        chatHistoryProgress = chatInfoProgress; chatInfoProgress.Text = ""; chatInfoProgress.IsVisible = false;
        // Body identity is intentionally shared across openings. A request
        // revision must reject late artwork/palettes from a previous binding.
        var account = chatAccountId; var revision = chatInfoRevision;
        Run(async () =>
        {
            var source = await SharedArtworkAsync(peer.AvatarUrl);
            if (source == null || disposed || account != chatAccountId || revision != chatInfoRevision || chatInfoPeer != peer.Id) return;
            chatInfoAvatar.Source = source;
            var palette = await playerPalettes.GetValue(source, image => Task.Run(() => PlayerPalette.FromArtwork(image), lifetime.Token));
            if (!disposed && account == chatAccountId && revision == chatInfoRevision && chatInfoPeer == peer.Id) chatProfileBackdrop.SetPalette(palette);
        });
        chatFindPeer = peer.Id;
        Run(async () => { await RenderChatMediaLibraryAsync("Музыка"); await DownloadChatSearchHistoryAsync(); });
    }

    private async Task RenderChatMediaLibraryAsync(string kind, int offset = 0)
    {
        if (chatRecipient == null || chatHistory == null || chatMediaLibrary is not { } library) return;
        var kinds = new[] { "Музыка", "Фото", "Видео", "Файлы", "Ссылки" };
        var oldKind = chatMediaLibraryKind; chatMediaLibraryKind = kind;
        chatMediaPill?.Select(Array.IndexOf(kinds, kind));
        if (offset == 0)
        {
            ClearChatLibraryPreviews();
            compactTrackRows.RemoveAll(row => library.Children.Contains(row.Root));
            library = chatMediaLibrary = new StackPanel().Vertical().Spacing(4);
            chatMediaTransition.Show(library, Array.IndexOf(kinds, kind) >= Array.IndexOf(kinds, oldKind) ? 1 : -1);
        }
        var peer = chatRecipient.Id; var revision = chatRevision; var generation = ++chatDetailsGeneration;
        if (offset == 0) { compactTrackRows.RemoveAll(row => library.Children.Contains(row.Root)); library.Clear(); }
        else
        {
            if (chatMediaEmpty != null) library.Remove(chatMediaEmpty);
            if (chatMediaMore != null) library.Remove(chatMediaMore);
        }
        chatMediaEmpty = chatMediaMore = null;
        var loading = new ProgressRing().Width(24).Height(24).IsActive(true).CenterHorizontal().Margin(0, 24);
        library.Add(loading);
        ChatMessage[] messages; Dictionary<string, MediaFile[]> metadata;
        var store = chatHistory; var token = ChatReadToken();
        try
        {
            messages = await store.MediaMessagesAsync(peer, token, offset);
            metadata = await store.MediaMetadataAsync(peer, token);
        }
        finally { loading.IsActive = false; library.Remove(loading); }
        if (revision != chatRevision || generation != chatDetailsGeneration || chatMediaLibrary != library) return;
        var tiles = new List<FrameworkElement>();
        foreach (var message in messages)
        {
            var text = ChatProtocol.Project(message, chatAccountId, peer).Text;
            if (kind == "Музыка")
            {
                foreach (var url in ChatMusicLink.Find(text).Distinct())
                {
                    LibraryItem item;
                    try { item = await ResolveChatMusicAsync(url); }
                    catch (Exception error) when (error is HttpRequestException or SoundCloudException) { continue; }
                    if (revision != chatRevision || generation != chatDetailsGeneration) return;
                    if (item.Track is { Id: > 0 } track)
                    {
                        var row = CreateCompactTrackRow(_ => Run(() => PlayChatLibraryMusicAsync(url, messages)), open: () => Run(() => OpenChatMusicUrlAsync(url)), dense: true);
                        BindCompactTrackRow(row, track, loadArtwork: true); library.Add(row.Root);
                    }
                    else if (item.Playlist != null)
                    {
                        library.Add(TrackButtons.Action(new Grid().Columns("32,*").Rows("*").Spacing(10).Children(
                            Icons.View("queue", 25, Muted).Center().Column(0), new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(
                                new TextBlock().Text(item.Title).FontSize(13).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                                new TextBlock().Text("Плейлист").FontSize(11).Foreground(Muted))),
                            () => Run(() => OpenChatMusicUrlAsync(url)), TrackButtons.Text, 58).Padding(8, 0));
                    }
                }
            }
            else if (kind == "Ссылки")
            {
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"https?://[^\s<>]+"))
                {
                    var link = match.Value;
                    library.Add(TrackButtons.Action(ChatDetailText(link), () => OpenExternalUrl(link, () => chatThreadStatus.Value = "Не удалось открыть ссылку"), TrackButtons.Text, 48).Padding(8, 0));
                }
            }
            else if (ChatMediaReference.Parse(message.Body) != null)
            {
                var summary = ChatMediaSummaryFor(message, peer);
                var files = summary?.Files ?? metadata.GetValueOrDefault(message.Id);
                if (files == null)
                {
                    tiles.Add(CreateChatLibraryPreview(message,peer,-1,null,"Вложение",library));
                    continue;
                }
                for (var index = 0; index < files.Length; index++)
                {
                    var file = files[index];
                    if (!(kind == "Фото" ? !file.AsFile && file.Type.StartsWith("image/") : kind == "Видео" ? !file.AsFile && file.Type.StartsWith("video/") : file.AsFile || !file.Type.StartsWith("image/") && !file.Type.StartsWith("video/"))) continue;
                    if (kind is "Фото" or "Видео")
                    {
                        var photo = ChatDecoded(ChatMediaCacheId(message, peer), index)?.Source ?? summary?.Photos?.ElementAtOrDefault(index)?.Source;
                        tiles.Add(CreateChatLibraryPreview(message,peer,index,photo,file.Name,library)); continue;
                    }
                    library.Add(TrackButtons.Action(new Grid().Columns("32,*").Rows("*").Spacing(10).Children(
                        Icons.View(kind == "Фото" ? "image" : kind == "Видео" ? "play" : "file", 24, Muted).Center().Column(0),
                        ChatDetailText(file.Name).Column(1).CenterVertical()), () => Run(() => JumpChatMessageAsync(peer, message.Id)), TrackButtons.Text, 56).Padding(8, 0));
                }
            }
        }
        if (tiles.Count > 0)
        {
            var gallery = new Grid().Columns("*,*,*").Rows(string.Join(",", Enumerable.Repeat("94", (tiles.Count + 2) / 3))).Spacing(4);
            for (var i = 0; i < tiles.Count; i++) gallery.Add(tiles[i].Row(i / 3).Column(i % 3));
            library.Add(gallery);
        }
        if (library.Children.Count == 0)
            library.Add(chatMediaEmpty = new StackPanel().Vertical().Spacing(12).Margin(8, 30).Children(
                Icons.View(kind == "Музыка" ? "music-notes" : kind == "Фото" ? "image" : "file", 36, Muted).CenterHorizontal(),
                new TextBlock().Text("Пока пусто").FontSize(15).SemiBold().CenterHorizontal(),
                new TextBlock().Text("Здесь появятся " + (kind == "Музыка" ? "треки и плейлисты" : kind.ToLowerInvariant()) + " из переписки").FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).CenterHorizontal()));
        if (messages.Length == 50) library.Add(chatMediaMore = TrackButtons.Action(new TextBlock().Text("Показать ещё").FontSize(12), () => Run(() => RenderChatMediaLibraryAsync(kind, offset + 50)), TrackButtons.Text, 34));

    }
    private async Task PlayChatLibraryMusicAsync(string selected, IEnumerable<ChatMessage> messages)
    {
        var peer = chatRecipient; var revision = chatRevision; if (peer == null) return;
        var urls = messages.SelectMany(m => ChatMusicLink.Find(ChatProtocol.Project(m, chatAccountId, peer.Id).Text)).Distinct().ToArray();
        var items = new List<(string Url, SoundCloudTrack Track)>();
        foreach (var url in urls)
        {
            var item = await ResolveChatMusicAsync(url);
            if (revision != chatRevision) return;
            if (item.Track is { } track) items.Add((url, track));
            else if (item.Playlist != null)
            { var detail = await api.GetCollectionDetailAsync(item, ChatReadToken()); items.AddRange(detail.Tracks.Tracks.Select(t => (url, t))); }
        }
        if (revision != chatRevision) return;
        var index = items.FindIndex(p => p.Url == selected); if (index < 0) return;
        StartQueueContext(new($"chat-library:{peer.Id}", "Музыка из чата с " + peer.Username, "Вложения переписки"), items.Select(p => p.Track).ToArray(), items[index].Track, index);
        await PlayAsync(items[index].Track);
    }
    private async Task UpdateChatAvatarAsync(SoundCloudUser peer)
    {
        var generation = ++chatAvatarGeneration; var revision = chatRevision;
        chatHeaderAvatar.Source = Icons.Source("user", Muted);
        var avatar = await SharedArtworkAsync(peer.AvatarUrl);
        if (!disposed && chatRevision == revision && generation == chatAvatarGeneration && chatRecipient?.Id == peer.Id)
            chatHeaderAvatar.Source = avatar is null ? Icons.Source("user", Muted) : avatar;
    }
}
