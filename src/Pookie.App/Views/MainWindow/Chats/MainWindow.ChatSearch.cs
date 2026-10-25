using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ChatTabs chatGlobalSearchModes = null!;
    private StackPanel chatGlobalSearchResults = null!, chatThreadSearchResults = null!;
    private FrameworkElement chatGlobalSearchFrame = null!, chatNormalIdentity = null!, chatSearchIdentity = null!;
    private ChatSearchPopup chatThreadSearchPopup = null!;
    private ChatPanelTransition chatSidebarTransition = null!, chatSearchResultsTransition = null!, chatGlobalResultsTransition = null!;
    private Border chatSearchLanding = null!;
    private string? chatSearchLandingKey;
    private FrameworkElement chatSidebarNormal = null!, chatSidebarSearch = null!, chatPeopleSearch = null!;
    private Button chatSidebarBack = null!;
    private bool chatSidebarSearching;
    private string? chatScanKey;
    private string? chatResultKey, chatGlobalResultKey;
    private Image chatGlobalSearchIcon = null!;
    private ProgressRing chatGlobalSearchBusy = null!, chatThreadSearchBusy = null!;
    private TextBlock chatSearchCount = null!;
    private Button chatSearchPrevious = null!, chatSearchNext = null!, chatGlobalSearchClear = null!;
    private DispatcherTimer chatMessageSearchTimer = null!;
    private bool chatGlobalMessages, chatGlobalSearchForced, chatThreadSearchOpen;
    private ChatSearchHit[] chatSearchHits = [];
    private int chatSearchHitIndex = -1;
    private long chatFindRevision;

    private FrameworkElement ChatGlobalSearchModes()
    {
        chatGlobalSearchModes = new ChatTabs();
        chatGlobalSearchModes.AddTab("Диалоги", () => { chatHistorySearch?.Cancel(); chatScanKey = chatGlobalResultKey = null; chatGlobalMessages = false; Run(SearchChatsAsync); });
        chatGlobalSearchModes.AddTab("Сообщения", () => { CloseChatSearch(); chatGlobalMessages = true; Run(SearchChatsAsync); });
        chatGlobalSearchModes.IsVisible = false;
        chatGlobalSearchResults = new StackPanel().Vertical().Spacing(2);
        chatGlobalResultsTransition = new ChatPanelTransition(); chatGlobalResultsTransition.Show(chatGlobalSearchResults);
        chatGlobalSearchFrame = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(chatGlobalResultsTransition).IsVisible(false);
        chatSearch.GotFocus += () => { chatGlobalSearchForced = true; RefreshChatGlobalSearchSurface(); };
        chatSearch.LostFocus += RefreshChatGlobalSearchSurface;
        chatSearch.OnKeyDown(e => { if (e.Key == Key.Escape) { e.Handled = true; CloseChatGlobalSearch(); } });
        return chatGlobalSearchModes;
    }
    private void RefreshChatGlobalSearchSurface()
    {
        var showing = chatGlobalMessages && chatSearch.Text.Trim().Length > 0;
        var searching = chatGlobalSearchForced || chatSearch.Text.Length > 0;
        if (searching)
        {
            chatGlobalSearchFrame.IsVisible = showing;
            var landing = chatSearch.Text.Trim().Length == 0;
            if (chatPeopleSearch != null) chatPeopleSearch.IsVisible = !showing && !landing;
            if (chatSearchLanding != null) { chatSearchLanding.IsVisible = landing; if (landing) RefreshChatSearchLanding(); }
        }
        if (chatSidebarTransition != null && searching != chatSidebarSearching)
        {
            chatSidebarSearching = searching;
            chatSidebarTransition.Show(searching ? chatSidebarSearch : chatSidebarNormal, searching ? 1 : -1);
        }
        if (chatSidebarBack != null) chatSidebarBack.Content = Icons.View(searching ? "arrow-left" : "list", 22, Muted).Center();
        chatGlobalSearchModes.IsVisible = true;
        if (chatGlobalSearchClear != null) chatGlobalSearchClear.IsVisible = chatSearch.Text.Length > 0;
    }

    private void RefreshChatSearchLanding()
    {
        var recent = chatConversations.OrderByDescending(c => chatWorkspace.Positions.ContainsKey(c.Recipient.Id))
            .ThenByDescending(c => c.LastMessage?.SentAt).Take(4).ToArray();
        var key = string.Join("|",recent.Select(c => $"{c.Id}:{c.Recipient.AvatarUrl}:{c.Recipient.Username}"));
        if (key == chatSearchLandingKey) return;
        chatSearchLandingKey = key;
        var avatars = new Grid().Columns(string.Join(",",Enumerable.Repeat("*",Math.Max(1,recent.Length)))).Rows("Auto").Spacing(4);
        for (var i=0; i<recent.Length; i++)
        {
            var dialog = recent[i]; var avatar = new Image().Width(42).Height(42).StretchMode(Stretch.UniformToFill);
            avatar.Source = Icons.Source("user",Muted);
            var content = new StackPanel().Vertical().Spacing(7).Children(
                new Border().Width(42).Height(42).CornerRadius(21).ClipToBounds().Child(avatar).CenterHorizontal(),
                new TextBlock().Text(dialog.Recipient.Username).FontSize(11).TextTrimming(TextTrimming.CharacterEllipsis).CenterHorizontal().MaxWidth(62));
            avatars.Add(TrackButtons.Action(content,()=> { CloseChatGlobalSearch(); Run(()=>SelectChatAsync(dialog.Recipient)); },TrackButtons.Text,80).Padding(2,0).Column(i));
            var account=chatAccountId;
            Run(async()=> { var source=await SharedArtworkAsync(dialog.Recipient.AvatarUrl); if (!disposed && account==chatAccountId && source!=null) avatar.Source=source; });
        }
        var savedContent = new Grid().Columns("36,*").Rows("*").Spacing(10).Children(
            new Border().Width(36).Height(36).CornerRadius(18).Background(ChatOutgoingColor).Child(Icons.View("bookmark-simple",20,ChatAccent).Center()).Column(0),
            new TextBlock().Text("Избранное").FontSize(13).Column(1).CenterVertical());
        var saved = TrackButtons.Action(savedContent, ()=> { CloseChatGlobalSearch(); OpenChatFavorites(); },TrackButtons.Text,48).Padding(0);
        savedContent.HorizontalAlignment = HorizontalAlignment.Stretch;
        chatSearchLanding.Child = new StackPanel().Vertical().Spacing(12).Children(
            new Border().CornerRadius(20).Background(Color.FromRgb(36,36,40)).Padding(8).Child(avatars).IsVisible(recent.Length>0),
            new Border().CornerRadius(20).Background(Color.FromRgb(36,36,40)).Padding(12).Child(
                new StackPanel().Vertical().Spacing(8).Children(
                    new TextBlock().Text("Недавние").FontSize(13).SemiBold().Foreground(ChatAccent), saved)));
    }

    private FrameworkElement ChatGlobalSearchField()
    {
        chatGlobalSearchClear = TrackButtons.Icon(Icons.View("x-mark", 16, Muted), () => { chatSearch.Text = ""; chatSearch.Focus(); }, TrackButtons.Text, 28).IsVisible(false).ToolTip("Очистить поиск");
        chatSearch.Padding = new Thickness(0);
        chatGlobalSearchIcon = Icons.View("magnifying-glass",20,Muted).Center();
        chatGlobalSearchBusy = new ProgressRing().Width(16).Height(16).IsActive(false).IsVisible(false).Center();
        var card = new GlassCard().Height(40).CornerRadius(20).Padding(12,0).Child(new Grid().Columns("20,*,Auto").Rows("*").Spacing(10).Children(
            new Grid().Columns("*").Rows("*").Children(chatGlobalSearchIcon,chatGlobalSearchBusy).Column(0), chatSearch.Column(1).CenterVertical(), chatGlobalSearchClear.Column(2).CenterVertical()));
        chatSearch.GotFocus += () => card.SetEmphasized(true); chatSearch.LostFocus += () => card.SetEmphasized(false);
        return card;
    }
    private void CloseChatGlobalSearch()
    {
        chatHistorySearch?.Cancel(); chatScanKey = chatGlobalResultKey = null; chatGlobalMessages = chatGlobalSearchForced = chatSearchMode = false;
        chatGlobalSearchModes.Select(0); chatSearch.Text = ""; chatSearchTimer.Stop(); RefreshChatGlobalSearchSurface(); chatsStatus.Value = ""; RenderChatInbox();
        if (chatRecipient != null) chatInput.Focus(); else Window.FocusManager.ClearFocus();
    }
    private void OpenChatGlobalSearch()
    {
        CloseChatSearch(); chatGlobalSearchForced = chatGlobalMessages = chatFindMessages = true; chatFindPeer = null;
        chatGlobalSearchModes.Select(1); RefreshChatGlobalSearchSurface(); chatSearch.Focus(); Run(() => SearchChatMessagesAsync());
    }
    private FrameworkElement ChatThreadSearchIdentity()
    {
        chatMessageSearch = new TextBox().MaxLength(256).Placeholder("Поиск в переписке").Background(Color.Transparent).BorderThickness(0).Padding(0).FontSize(14);
        chatMessageSearchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(220));
        chatMessageSearchTimer.Tick += () => { chatMessageSearchTimer.Stop(); Run(() => SearchChatMessagesAsync()); };
        chatMessageSearch.OnTextChanged(_ => { chatHistorySearch?.Cancel(); chatScanKey = null; ++chatFindRevision; chatMessageSearchTimer.Stop(); if (chatThreadSearchOpen) chatMessageSearchTimer.Start(); });
        chatMessageSearch.GotFocus += () => { if (chatThreadSearchOpen) chatThreadSearchPopup.Expanded = true; };
        chatMessageSearch.OnKeyDown(e =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseChatSearch(); }
            if (e.Key == Key.Enter) { e.Handled = true; Run(() => NavigateChatSearchAsync(e.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1)); }
        });
        chatThreadSearchBusy = new ProgressRing().Width(12).Height(12).IsActive(false).IsVisible(false).CenterVertical();
        chatSearchCount = new TextBlock().FontSize(11).Foreground(Muted).CenterVertical();
        chatSearchPrevious = TrackButtons.Icon(Icons.View("arrow-left", 17, Muted), () => Run(() => NavigateChatSearchAsync(-1)), TrackButtons.Text, 30).ToolTip("Предыдущий результат · Shift+Enter");
        chatSearchNext = TrackButtons.Icon(Icons.View("arrow-right", 17, Muted), () => Run(() => NavigateChatSearchAsync(1)), TrackButtons.Text, 30).ToolTip("Следующий результат · Enter");
        var close = TrackButtons.Icon(Icons.View("x-mark", 18, Muted), () => { if (chatMessageSearch.Text.Length > 0) chatMessageSearch.Text = ""; else CloseChatSearch(); }, TrackButtons.Text, 30).ToolTip("Очистить / закрыть поиск");
        chatSearchIdentity = new Grid().Columns("20,*,Auto,30,30,30").Rows("*").Spacing(4).Children(Icons.View("magnifying-glass", 20, Muted).Center().Column(0), chatMessageSearch.Column(1).CenterVertical(), new StackPanel().Horizontal().Spacing(5).Children(chatThreadSearchBusy,chatSearchCount).Column(2).CenterVertical(), chatSearchPrevious.Column(3).CenterVertical(), chatSearchNext.Column(4).CenterVertical(), close.Column(5).CenterVertical()).IsVisible(false);
        return chatSearchIdentity;
    }
    private FrameworkElement ChatThreadSearchResults()
    {
        chatThreadSearchResults = new StackPanel().Vertical().Spacing(2);
        chatSearchResultsTransition = new ChatPanelTransition(); chatSearchResultsTransition.Show(chatThreadSearchResults);
        chatThreadSearchPopup = new ChatSearchPopup(ChatThreadSearchIdentity(),
            new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).MaxHeight(277).Content(chatSearchResultsTransition)).Margin(54,4,44,0);
        return chatThreadSearchPopup;
    }

    private void OpenChatThreadSearch()
    {
        if (chatRecipient == null && !chatFavoritesOpen) { OpenChatGlobalSearch(); return; }
        chatFindMessages = true; chatFindPeer = chatRecipient?.Id;
        chatThreadSearchOpen = true; chatNormalIdentity.IsVisible = true; chatSearchIdentity.IsVisible = true; chatThreadSearchPopup.Searching = true;
        chatThreadSearchPopup.Expanded = true; chatMessageSearch.Focus(); Run(() => SearchChatMessagesAsync());
    }
    private void CloseChatSearch()
    {
        ++chatFindRevision; chatScanKey = null; chatResultKey = null;
        if (chatThreadSearchOpen) chatHistorySearch?.Cancel();
        if (chatMessageSearchTimer != null) chatMessageSearchTimer.Stop();
        chatThreadSearchOpen = false;
        if (chatNormalIdentity != null) { chatNormalIdentity.IsVisible = true; chatThreadSearchPopup.Searching = false; }
        chatSearchHits = []; chatSearchHitIndex = -1;
        chatFindMessages = chatGlobalMessages && chatSearch.Text.Length > 0;
    }
    private async Task SearchChatMessagesAsync(bool scanning = false)
    {
        if (chatHistory == null || !chatThreadSearchOpen && !chatGlobalMessages) return;
        var thread = chatThreadSearchOpen; var query = (thread ? chatMessageSearch.Text : chatSearch.Text).Trim();
        var scope = thread ? chatRecipient?.Id : null; var account = chatAccountId; var revision = ++chatFindRevision; var store = chatHistory;
        ChatSearchHit[] results = [];
        if (query.Length > 0)
            results = thread && chatFavoritesOpen
                ? chatWorkspace.Favorites.Where(f => ChatProtocol.Project(f.Message, account, f.PeerId).Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(f => f.SavedAt).Take(100).Select(f => new ChatSearchHit(f.PeerId, f.Message)).ToArray()
                : await store.SearchAsync(query, scope, ChatReadToken());
        if (!thread && query.Length > 0)
            results = results.Concat(chatWorkspace.Favorites.Where(f => ChatProtocol.Project(f.Message, account, f.PeerId).Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Select(f => new ChatSearchHit(f.PeerId, f.Message)))
                .DistinctBy(hit => (hit.PeerId, hit.Message.Id)).OrderByDescending(hit => hit.Message.SentAt).Take(100).ToArray();
        if (disposed || account != chatAccountId || revision != chatFindRevision || chatHistory != store || thread != chatThreadSearchOpen) return;
        var resultKey = query + ":" + scope + ":" + string.Join("|", results.Select(hit => hit.PeerId + ":" + hit.Message.Id));
        var unchanged = resultKey == (thread ? chatResultKey : chatGlobalResultKey);
        if (unchanged) { if (!scanning) StartChatHistorySearch(query, scope, thread); return; }
        var target = new StackPanel().Vertical().Spacing(2);
        if (thread) { chatThreadSearchResults = target; chatResultKey = resultKey; }
        else { chatGlobalSearchResults = target; chatGlobalResultKey = resultKey; }
        if (thread)
        {
            var current = scanning && chatSearchHitIndex >= 0 ? chatSearchHits.ElementAtOrDefault(chatSearchHitIndex)?.Message.Id : null;
            chatSearchHits = results; chatSearchHitIndex = current == null ? -1 : Array.FindIndex(results, hit => hit.Message.Id == current);
            chatSearchCount.Text = chatSearchHitIndex >= 0 ? $"{chatSearchHitIndex + 1} / {results.Length}" : results.Length > 0 ? results.Length.ToString() : "";
            chatSearchPrevious.IsEnabled = chatSearchNext.IsEnabled = results.Length > 0;
        }
        for (var i = 0; i < results.Length; i++)
        {
            var hit = results[i]; var index = i;
            target.Add(ChatSearchResult(hit, () => Run(thread ? () => OpenChatSearchHitAsync(index) : () => JumpChatMessageAsync(hit.PeerId, hit.Message.Id))));
        }
        if (results.Length == 0)
        {
            var empty = new TextBlock().Text(query.Length == 0 ? "Введи слово или фразу" : chatHistoryLoading ? "Поиск…" : "Ничего не найдено").FontSize(13).Foreground(Muted).Margin(12,14);
            empty.Tag = "chat.search.empty"; target.Add(empty);
        }
        if (thread) { chatSearchResultsTransition.Show(target); if (!scanning) chatThreadSearchPopup.Expanded = true; }
        else { chatGlobalResultsTransition.Show(target); RefreshChatGlobalSearchSurface(); }
        if (!scanning) StartChatHistorySearch(query, scope, thread);
    }
    private void StartChatHistorySearch(string query, long? scope, bool thread)
    {
        if (thread && chatFavoritesOpen || query.Length == 0)
        { chatHistorySearch?.Cancel(); chatScanKey = null; return; }
        var key = $"{chatAccountId}:{scope}:{query}";
        if (key == chatScanKey) return;
        chatScanKey = key; chatFindPeer = scope; chatFindMessages = true;
        Run(DownloadChatSearchHistoryAsync);
    }

    private void RefreshChatSearchActivity(bool busy)
    {
        if (chatGlobalSearchBusy == null || chatThreadSearchBusy == null) return;
        chatThreadSearchBusy.IsActive = chatThreadSearchBusy.IsVisible = busy && chatThreadSearchOpen;
        chatGlobalSearchBusy.IsActive = chatGlobalSearchBusy.IsVisible = busy && !chatThreadSearchOpen && chatGlobalMessages;
        chatGlobalSearchIcon.IsVisible = !chatGlobalSearchBusy.IsVisible;
        foreach (var panel in new[] {chatThreadSearchResults,chatGlobalSearchResults})
            foreach (var text in panel.Children.OfType<TextBlock>().Where(t => t.Tag as string == "chat.search.empty"))
                if ((ReferenceEquals(panel,chatThreadSearchResults) ? chatMessageSearch.Text : chatSearch.Text).Trim().Length > 0)
                    text.Text = busy ? "Поиск…" : "Ничего не найдено";
    }
    private FrameworkElement ChatSearchResult(ChatSearchHit hit, Action activate)
    {
        var dialog = chatConversations.FirstOrDefault(c => c.Recipient.Id == hit.PeerId);
        var author = chatThreadSearchOpen && hit.Message.SenderId == chatAccountId ? me : dialog?.Recipient;
        var name = author?.Username ?? "Переписка";
        var avatar = new Image().Width(32).Height(32).StretchMode(Stretch.UniformToFill); avatar.Source = Icons.Source("user", Muted);
        var content = new Grid().Columns("32,*").Rows("*").Spacing(10).Children(new Border().CornerRadius(16).ClipToBounds().BorderThickness(0).Child(avatar).Width(32).Height(32).Column(0).CenterVertical(),
            new StackPanel().Vertical().Spacing(4).Column(1).CenterVertical().Children(new Grid().Columns("*,Auto").Rows("Auto").Spacing(6).Children(
                new TextBlock().Text(name).FontSize(13).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).Column(0),
                new TextBlock().Text(ChatPresentation.InboxTime(hit.Message.SentAt, DateTimeOffset.UtcNow)).FontSize(10).Foreground(Muted).Column(1)),
                ChatSearchSnippet(ChatProtocol.Project(hit.Message, chatAccountId, hit.PeerId).Text)));
        var button = TrackButtons.Action(content, activate, TrackButtons.Text, 64).Padding(8, 0).CornerRadius(12);
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        var account = chatAccountId;
        Run(async () => { var image = await SharedArtworkAsync(author?.AvatarUrl); if (image != null && !disposed && account == chatAccountId) avatar.Source = image; });
        return button;
    }
    private TextBlock ChatSearchSnippet(string body)
    {
        var text = ChatPreview(body); var query = (chatThreadSearchOpen ? chatMessageSearch.Text : chatSearch.Text).Trim();
        var label = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        var match = query.Length == 0 ? -1 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (match < 0) { label.Text = text; return label; }
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        var start = boundaries.Last(b => b <= match); var end = boundaries.First(b => b >= match + query.Length);
        var excerpt = boundaries.Last(b => b <= Math.Max(0, start - 35));
        if (excerpt > 0) label.Inlines.Add(new Aprillz.MewUI.Controls.Run("…"));
        if (start > excerpt) label.Inlines.Add(new Aprillz.MewUI.Controls.Run(text[excerpt..start]));
        label.Inlines.Add(new Aprillz.MewUI.Controls.Run(text[start..end]) { Foreground = ChatAccent, FontWeight = FontWeight.SemiBold });
        var excerptEnd = boundaries.First(b => b >= Math.Min(text.Length, end + 160));
        if (end < excerptEnd) label.Inlines.Add(new Aprillz.MewUI.Controls.Run(text[end..excerptEnd]));
        if (excerptEnd < text.Length) label.Inlines.Add(new Aprillz.MewUI.Controls.Run("…"));
        return label;
    }
    private Task NavigateChatSearchAsync(int direction) => chatSearchHits.Length == 0 ? Task.CompletedTask : OpenChatSearchHitAsync(chatSearchHitIndex < 0 ? direction < 0 ? chatSearchHits.Length - 1 : 0 : (chatSearchHitIndex + direction + chatSearchHits.Length) % chatSearchHits.Length);
    private async Task OpenChatSearchHitAsync(int index)
    {
        if (index < 0 || index >= chatSearchHits.Length) return;
        var hit = chatSearchHits[index]; chatSearchHitIndex = index;
        await JumpChatMessageAsync(hit.PeerId, hit.Message.Id, keepDetails: true);
        if (chatThreadSearchOpen) { chatSearchCount.Text = $"{index + 1} / {chatSearchHits.Length}"; chatThreadSearchPopup.Expanded = false; }
    }
}
