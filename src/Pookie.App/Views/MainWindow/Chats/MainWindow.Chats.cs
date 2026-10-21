using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<string> chatsStatus = new(""), chatThreadStatus = new("");
    private ItemsControl chatInbox = null!, chatMessagesView = null!;
    private ChatViewport chatViewport = null!;
    private ChatComposerLayout chatComposerLayout = null!;
    private TextBlock chatName = null!, chatAttachmentLabel = null!, chatFloatingDateText = null!;
    private Border chatFloatingDate = null!;
    private ChatDateLayer chatDateLayer = null!;
    private Image chatAttachmentPreview = null!;
    private ChatLoadingView chatInboxLoading = null!, chatThreadLoading = null!;
    private MultiLineTextBox chatInput = null!;
    private TextBox chatSearch = null!;
    private Button chatSend = null!, chatAttach = null!, chatGif = null!, chatOlder = null!, chatMore = null!, chatBack = null!, chatNewMessages = null!;
    private Border chatComposer = null!;
    private ChatColumns chatColumns = null!;
    private DispatcherTimer chatTimer = null!, chatSearchTimer = null!, chatDraftTimer = null!;
    private ChatMediaHosting chatMedia = new();
    private readonly KlipyClient klipy = new();
    private readonly List<ChatConversation> chatConversations = [];
    private readonly List<ChatMessage> chatMessages = [];
    private readonly Dictionary<long, string> chatDrafts = [];
    private readonly Dictionary<long, ChatPending> chatPending = [];
    private ChatLocalStore? chatLocalStore;
    private SoundCloudUser? chatRecipient;
    private string? chatListCursor, chatMessageCursor;
    private long chatAccountId, chatRevision, chatSearchRevision;
    private bool chatsBusy, chatThreadBusy, chatSending, chatRenderingDraft, chatSearchMode;
    private bool chatScrollBottom;
    private string[] chatSelectedFiles = [];
    private KlipyGif? chatSelectedGif;
    private CancellationTokenSource? chatReadCancellation;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, ChatBubbleRow> chatRows = new();
    private IEnumerable<ChatBubbleRow> ChatRows => chatRows.Select(pair => pair.Value);

    private FrameworkElement ChatsPage()
    {
        chatInbox = new ItemsControl().Items(Array.Empty<ChatConversation>(), c => c.Id).ItemHeight(82).FixedHeightPresenter()
            .Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0, 0, 8, 6));
        chatInbox.ItemTemplate = ChatConversationTemplate();
        chatInbox.ItemsSource = ItemsView.Create(chatInboxItems, c => c.Recipient.Username, c => c.Id);
        chatInboxLoading = new(chatInbox, inbox: true);
        chatSearch = new TextBox().Placeholder("Найти человека").Background(Color.Transparent).BorderThickness(0).FontSize(13).OnTextChanged(_ =>
        {
            ++chatSearchRevision;
            chatSearchTimer.Stop(); chatSearchTimer.Start();
        });
        chatSearchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(450));
        chatSearchTimer.Tick += () => { chatSearchTimer.Stop(); Run(SearchChatsAsync); };
        chatMore = TrackButtons.Action(new TextBlock().Text("Ещё диалоги").FontSize(12), () => Run(LoadMoreChatsAsync), TrackButtons.Text, 34).IsVisible(false);
        var inbox = new Grid().Columns("*").Rows("Auto,Auto,Auto,*,Auto").Spacing(16).Children(
            new TextBlock().Text("Чаты").FontSize(28).Bold().Row(0),
            MaterialSearchField.Create(chatSearch).Row(1),
            new TextBlock().BindText(chatsStatus).BindIsVisible(chatsStatus, text => text.Length > 0).FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Row(2),
            chatInboxLoading.Root.Row(3), chatMore.Row(4));

        chatName = new TextBlock().Text("Твои сообщения").FontSize(22).Bold().TextTrimming(TextTrimming.CharacterEllipsis);
        chatBack = TrackButtons.Icon(Icons.View("arrow-left", 20), () => { SaveChatDraft(); chatColumns.Selected = false; }, TrackButtons.Text, 36).IsVisible(false);
        chatOlder = TrackButtons.Action(new TextBlock().Text("Предыдущие сообщения").FontSize(12), () => Run(LoadOlderMessagesAsync), TrackButtons.Text, 30).IsVisible(false).CenterHorizontal();
        chatNewMessages = TrackButtons.Action(new TextBlock().Text("Новые сообщения ↓").FontSize(12), RequestChatBottom, TrackButtons.Tonal, 32).IsVisible(false).CenterHorizontal();
        chatMessagesView = new ItemsControl().Items(Array.Empty<ChatMessageItem>(), m => m.Key).ItemHeight(84).VariableHeightPresenter()
            .Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0, 4, 12, 6));
        chatMessagesView.MouseWheel += _ => { chatScrollBottom = chatWasAtBottom = false; foreach (var row in ChatRows) row.FollowMediaResize = false; };
        chatMessagesView.ItemTemplate = ChatMessageTemplate();
        chatMessagesView.ItemsSource = ItemsView.Create(chatMessageItems, m => m.Body, m => m.Key);
        chatFloatingDateText = new TextBlock().FontSize(12).SemiBold();
        chatFloatingDate = new Border().Background(Color.FromRgb(48, 53, 59)).CornerRadius(14).Padding(12, 5)
            .Child(chatFloatingDateText).CenterHorizontal().IsVisible(false);
        chatFloatingDate.VerticalAlignment = VerticalAlignment.Top; chatFloatingDate.IsHitTestVisible = false;
        chatDateLayer = new(chatFloatingDate);
        chatViewport = new ChatViewport(chatMessagesView).Initialize();
        chatViewport.BeforePaint = () =>
        {
            if (SynchronizeChatScroll()) chatViewport.InvalidateArrange();
        };
        chatThreadLoading = new(new Grid().Columns("*").Rows("*").Children(chatViewport, chatDateLayer), inbox: false);
        chatInput = new MultiLineTextBox().Placeholder("Сообщение").Wrap(true).MaxLength(ChatRequest.MaxBodyLength).Background(Color.Transparent).BorderThickness(0).Padding(10, 4, 8, 4).FontSize(14);
        chatInput.OnTextChanged(text =>
        {
            if (chatRenderingDraft) return;
            if (preferences.ChatSaveDrafts && chatRecipient != null) chatDrafts[chatRecipient.Id] = text.Length <= ChatRequest.MaxBodyLength ? text : text[..ChatRequest.MaxBodyLength];
            chatDraftTimer.Stop(); if (preferences.ChatSaveDrafts) chatDraftTimer.Start(); RefreshChatComposer();
        });
        chatInput.OnKeyDown(e =>
        {
            if (e.Key == Key.Enter && !e.Modifiers.HasFlag(ModifierKeys.Shift) &&
                (preferences.ChatSendOnEnter || e.Modifiers.HasFlag(ModifierKeys.Control)))
            { e.Handled = true; Run(SendChatAsync); }
            if (e.Key == Key.Escape) { e.Handled = true; Window.FocusManager.ClearFocus(); }
        });
        InitializeChatMusicEditor();
        chatAttach = TrackButtons.Icon(Icons.View("paperclip", 22), () => Run(() => PickChatFilesAsync()), TrackButtons.Text, 40);
        chatGif = TrackButtons.Action(new TextBlock().Text("GIF").FontSize(12).Bold(), () => Run(OpenChatGifsAsync), TrackButtons.Text, 40).Padding(8, 0);
        chatSend = TrackButtons.Icon(Icons.View("paper-plane-tilt", 21, TrackButtons.OnPrimary), () => Run(SendChatAsync), TrackButtons.Filled, 40).ToolTip("Отправить · Ctrl+Enter");
        chatAttachmentLabel = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        var clearAttachments = TrackButtons.Icon(Icons.View("x", 16), ClearChatAttachments, TrackButtons.Text, 28);
        chatAttachmentPreview = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
        var attached = new Grid().Columns("44,*,28").Rows("44").Spacing(10).Children(
            new Border().CornerRadius(8).ClipToBounds().Background(Color.FromArgb(18, 255, 255, 255)).Child(chatAttachmentPreview).Column(0),
            chatAttachmentLabel.Column(1).CenterVertical(), clearAttachments.Column(2).CenterVertical());
        attached.IsVisible = false;
        chatAttachmentStrip = attached;
        chatComposerLayout = new(chatInput, attached, chatAttach, chatGif, chatSend,
            () => chatColumns.ActualHeight * .4 - 16, () => chatComposer.Background);
        chatComposerSurface = new(chatComposerLayout);
        chatComposer = new Border().Background(Color.FromRgb(51, 51, 51)).BorderThickness(0).CornerRadius(22).Padding(8).Child(chatComposerSurface);
        var thread = new DockPanel().LastChildFill().Spacing(12).Children(
            new Grid().Columns("Auto,*").Rows("Auto").Spacing(8).DockTop().Children(chatBack.Column(0).CenterVertical(),
                chatName.Column(1)),
            new TextBlock().BindText(chatThreadStatus).BindIsVisible(chatThreadStatus, value => value.Length > 0)
                .FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).DockTop(),
            chatOlder.DockTop(), chatComposer.DockBottom(), chatNewMessages.DockBottom(), chatThreadLoading.Root);
        chatColumns = new ChatColumns(inbox, thread, compact => chatBack.IsVisible = compact).Initialize();
        chatColumns.SizeChanged += _ => chatComposerLayout.InvalidateMeasure();
        chatTimer = new DispatcherTimer(TimeSpan.FromSeconds(1));
        chatTimer.Tick += () =>
        {
            if (disposed || demo || me == null || api.Session == null ||
                !preferences.ChatBackgroundRefresh && (page.Value != Page.Chats || !Window.IsActive) || DateTimeOffset.UtcNow < chatNextPoll) return;
            chatNextPoll = DateTimeOffset.UtcNow.AddSeconds(page.Value == Page.Chats && Window.IsActive ? 2 : 15);
            Run(RefreshChatsAsync);
        };
        chatDraftTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(700));
        chatDraftTimer.Tick += () => { chatDraftTimer.Stop(); SaveChatDraft(); };
        page.Changed += () =>
        {
            if (page.Value == Page.Chats) { chatNextPoll = default; chatTimer.Start(); }
            else
            {
                if (!preferences.ChatBackgroundRefresh) CancelChatPolling();
                chatSearchTimer.Stop(); chatInboxLoading.Pause(); chatThreadLoading.Pause(); CloseChatGifs(); CancelChatReads(); SaveChatDraft();
                SetChatDragging(false);
                foreach (var row in ChatRows) row.Spinner.IsActive = false;
            }
        };
        RefreshChatComposer();
        return chatColumns;
    }

    private Grid chatAttachmentStrip = null!;

    private IDataTemplate ChatConversationTemplate() => new DelegateTemplate<ChatConversation>(context =>
    {
        var avatar = new Image().Width(46).Height(46).StretchMode(Stretch.UniformToFill);
        var name = new TextBlock().FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        var preview = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        var time = new TextBlock().FontSize(11).Foreground(Muted).Right();
        var checks = new TextBlock().FontSize(12).Foreground(ChatAccent).Text("✓");
        var thumbnail = new Image().Width(28).Height(28).StretchMode(Stretch.UniformToFill);
        var previewIcon = new Image().Width(14).Height(14).Margin(0, 0, 5, 0).CenterVertical().IsVisible(false);
        var thumbnailFrame = new Border().Width(28).Height(28).Margin(0, 0, 7, 0).CornerRadius(5).ClipToBounds().Child(thumbnail).IsVisible(false);
        var unreadText = new TextBlock().FontSize(11).Foreground(Color.White).Center();
        var unread = new Border().MinWidth(20).Height(20).Padding(5, 0).CornerRadius(10).Background(Color.FromRgb(59, 134, 192)).CenterVertical().Child(unreadText);
        Button button = null!;
        button = TrackButtons.Action(new Grid().Columns("46,*").Rows("Auto").Spacing(12).Children(
            new Border().Width(46).Height(46).CenterVertical().CornerRadius(23).ClipToBounds().Child(avatar).Column(0),
            new Grid().Columns("*").Rows("Auto,28").Spacing(5).Column(1).CenterVertical().Children(
                new Grid().Columns("*,Auto,Auto").Rows("Auto").Spacing(5).Row(0).Children(name.Column(0), checks.Column(1), time.Column(2)),
                new Grid().Columns("*,Auto").Rows("28").Spacing(7).Row(1).Children(
                    new Grid().Columns("Auto,Auto,*").Rows("28").Children(thumbnailFrame.Column(0), previewIcon.Column(1), preview.Column(2).CenterVertical()).Column(0), unread.Column(1)))),
            () => { if (chatInboxBindings.TryGetValue(button!, out var conversation)) Run(() => SelectChatAsync(conversation.Recipient)); }, TrackButtons.Text, 76).Padding(10, 0).CornerRadius(18);
        ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        context.Register("button", button); context.Register("avatar", avatar); context.Register("name", name); context.Register("preview", preview); context.Register("unread", unread);
        chatInboxVisuals.Add(button, new(time, checks, thumbnail, thumbnailFrame, previewIcon, preview, unread, unreadText));
        return button;
    }, (_, conversation, _, context) =>
    {
        var button = context.Get<Button>("button"); chatInboxBindings[button] = conversation;
        context.Get<TextBlock>("name").Text = conversation.Recipient.Username;
        RefreshChatInboxPreview(button, conversation);
        TrackButtons.SetVariant(button, chatRecipient?.Id == conversation.Recipient.Id ? TrackButtons.Tonal : TrackButtons.Text);
        var avatar = context.Get<Image>("avatar");
        var visual = chatInboxVisuals.GetValue(button, _ => throw new InvalidOperationException());
        if (visual.AvatarPeer == conversation.Recipient.Id && visual.AvatarUrl == conversation.Recipient.AvatarUrl && avatar.Source != null) return;
        visual.AvatarPeer = conversation.Recipient.Id; visual.AvatarUrl = conversation.Recipient.AvatarUrl;
        avatar.Source = Icons.Source("user", Muted);
        var revision = chatRevision; var session = api.Session;
        Run(async () =>
        {
            var source = await SharedArtworkAsync(conversation.Recipient.AvatarUrl);
            if (!disposed && api.Session == session && chatInboxBindings.GetValueOrDefault(button)?.Id == conversation.Id && source != null) avatar.Source = source;
        });
    }, (_, _, _, context) => { var button = context.Get<Button>("button"); chatInboxBindings.Remove(button);  });
    private readonly Dictionary<Button, ChatConversation> chatInboxBindings = [];
    private static string ChatPreview(string body) => ChatMediaReference.Parse(body) != null ? "Вложение" : ChatPresentation.Preview(body);

    private IDataTemplate ChatMessageTemplate() => new DelegateTemplate<ChatMessageItem>(context =>
    {
        var row = new ChatBubbleRow(this); chatRows.Add(row.Root, row); context.Register("row", row.Root); return row.Root;
    }, (_, message, _, context) => { var row = ChatRow(context); row.Bind(message.Message); row.ItemKey = message.Key; },
       (_, _, _, context) => ChatRow(context).Bound = false);

    private ChatBubbleRow ChatRow(TemplateContext context) => chatRows.TryGetValue(context.Get<Border>("row"), out var row) ? row : throw new InvalidOperationException("Missing chat row.");

}
