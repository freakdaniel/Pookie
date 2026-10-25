using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed record MessageMenuContext(ChatMessage Message, long Account, long Peer, Point Point, string? Copy);
    private MessageMenuContext? chatMenuContext;
    private ChatPopupLayer chatMessageMenu = null!;
    private StackPanel chatMessageMenuBody = null!;
    private Button chatQuickMore = null!;
    private readonly List<(Button Button, string Action)> chatMessageMenuActions = [];
    private readonly List<(Button Button, string Emoji)> chatQuickReactions = [];

    private FrameworkElement ChatMessageMenuView()
    {
        var quick = new StackPanel().Horizontal().Spacing(2);
        foreach (var emoji in ChatProtocol.Reactions)
        {
            var button = TrackButtons.Action(ChatEmojiArtwork.View(emoji, 26), () => ExecuteChatMenuReaction(emoji), TrackButtons.Text, 38).Width(38).Padding(5).CornerRadius(19);
            button.ToolTip = new TextBlock().Text("Реакция " + emoji); quick.Add(button); chatQuickReactions.Add((button, emoji));
        }
        chatQuickMore = TrackButtons.Icon(Icons.View("caret-down", 20), () => Run(OpenChatMenuEmojiAsync), TrackButtons.Text, 38).Width(38).Padding(7).ToolTip("Все реакции"); quick.Add(chatQuickMore);
        var reactionCard = new GlassCard(Surface) { HoverEnabled = false }.Background(ChatGlassSurface).CornerRadius(22).Padding(4, 3).Child(quick);
        chatMessageMenuBody = new StackPanel().Vertical().Spacing(0);
        void Action(string id, string icon, string label)
        {
            var button = MenuChoice(icon, label, () => ExecuteChatMenuAction(id)); button.Height = 36;
            chatMessageMenuActions.Add((button, id)); chatMessageMenuBody.Add(button);
        }
        Action("reply", "arrow-bend-up-left", "Ответить"); Action("copy", "copy", "Копировать");
        Action("favorite", "bookmark-simple", "В избранное"); Action("select", "selection", "Выделить");
        Action("save", "download-simple", "Сохранить вложение"); Action("retry", "arrows-clockwise", "Повторить отправку");
        Action("discard", "trash", "Убрать отправку");
        var actionsCard = new GlassCard(Surface) { HoverEnabled = false }.Background(ChatGlassSurface).CornerRadius(16).Padding(4).Width(224).Left().Child(chatMessageMenuBody);
        var content = new StackPanel().Vertical().Spacing(6).Width(288).Children(reactionCard, actionsCard);
        content.Focusable = true;
        chatMessageMenu = new ChatPopupLayer(content, () => chatMenuContext is { } c ? new Rect(c.Point.X, c.Point.Y, 0, 0) : Rect.Empty, () => chatColumns.Bounds, atPointer: true).Initialize();
        chatMessageMenu.Closed = () => chatMenuContext = null;
        content.OnKeyDown(e =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseChatMessageMenu(); chatInput.Focus(); }
            else if (e.Key is Key.Down or Key.Up)
            {
                var buttons = chatMessageMenuActions.Select(a => a.Button).Where(b => b.IsVisible && b.IsEnabled).ToArray();
                if (buttons.Length == 0) return;
                var index = Array.FindIndex(buttons, b => b.IsFocused);
                buttons[(index + (e.Key == Key.Down ? 1 : buttons.Length - 1) + buttons.Length) % buttons.Length].Focus(); e.Handled = true;
            }
        });
        return chatMessageMenu;
    }
    private void OpenChatMessageMenu(ChatBubbleRow row, Point point)
    {
        if (row.Message is not { } message || !row.Bound || chatRecipient == null || chatFavoritesOpen) return;
        CloseChatGifs(); CloseChatAttachMenu(); CloseShellMenus();
        var copy = row.Body.SelectedText ?? (row.Body.Text.Length > 0 ? row.Body.Text : null);
        chatMenuContext = new(message, chatAccountId, chatRecipient.Id, point, copy);
        var pending = ChatOutgoing(message); var media = ChatMediaReference.Parse(message.Body) != null || pending?.HasMedia == true;
        foreach (var (button, action) in chatMessageMenuActions)
        {
            button.IsVisible = action switch { "reply" or "favorite" => pending == null, "copy" => copy != null,
                "save" => media, "retry" => pending?.State == ChatDelivery.Failed, "discard" => pending != null, _ => true };
            button.IsEnabled = action is "copy" or "select" or "favorite" || !chatSending;
            if (action == "favorite" && button.Content is StackPanel line && line.Children.LastOrDefault() is TextBlock label)
                label.Text = chatWorkspace.Favorites.Any(f => f.PeerId == chatRecipient.Id && f.Message.Id == message.Id) ? "Убрать из избранного" : "В избранное";
        }
        foreach (var (button, emoji) in chatQuickReactions)
        {
            button.IsEnabled = pending == null && !chatSending && !chatPending.ContainsKey(chatRecipient.Id);
            ((MaterialTrackButton)button).Palette(chatReactions.GetValueOrDefault(message.Id)?.GetValueOrDefault(chatAccountId) == emoji ? Color.FromRgb(43, 79, 107) : Color.Transparent, TrackButtons.OnSurface);
        }
        chatQuickMore.IsEnabled = chatQuickReactions[0].Button.IsEnabled;
        chatMessageMenu.Show(); ((FrameworkElement)chatMessageMenu.Children[0]).Focus();
    }
    private bool CurrentChatMenu(MessageMenuContext? context) => context != null && context.Account == chatAccountId && context.Peer == chatRecipient?.Id && page.Value == Page.Chats;
    private void ExecuteChatMenuAction(string action)
    {
        var context = chatMenuContext; CloseChatMessageMenu();
        if (!CurrentChatMenu(context)) return;
        var message = context!.Message;
        switch (action)
        {
            case "reply": ReplyToChat(message); break;
            case "copy": if (context.Copy != null) CopyText(context.Copy); break;
            case "favorite": ChatFavoriteMessage(message); break;
            case "select": if (chatMessageItems.Any(m => m.Id == message.Id)) ToggleChatSelection(message); break;
            case "save": Run(() => SaveChatAttachmentAsync(message.Id, 0)); break;
            case "retry": if (!chatSending && ChatOutgoing(message)?.State == ChatDelivery.Failed) Run(RetryChatOutgoingAsync); break;
            case "discard": if (!chatSending && ChatOutgoing(message) != null) DiscardPendingChat(); break;
        }
    }
    private void ExecuteChatMenuReaction(string emoji)
    {
        var context = chatMenuContext; CloseChatMessageMenu();
        if (CurrentChatMenu(context)) Run(() => ReactToChatAsync(context!.Message, emoji));
    }
    private async Task OpenChatMenuEmojiAsync()
    {
        var context = chatMenuContext;
        if (!CurrentChatMenu(context) || ChatOutgoing(context!.Message) != null || chatSending) return;
        chatEmojiTarget = context.Message;
        chatEmojiAnchor = new Rect(context.Point.X + 24, context.Point.Y + 48, 0, 0);
        await OpenChatEmojiPanelAsync();
    }
    private void CloseChatMessageMenu(bool immediate = false) => chatMessageMenu?.Close(immediate);
    private void ObserveChatPopups()
    {
        if (chatEmojiPopup == null || chatMessageMenu == null) return;
        if (!Window.IsActive || page.Value != Page.Chats || chatFavoritesOpen || expandedOpen)
        { if (chatEmojiPopup.IsOpen) CloseChatGifs(); CloseChatMessageMenu(); return; }
        if (chatEmojiPopup.IsOpen && (chatEmojiAccount != chatAccountId || chatEmojiPeer != chatRecipient?.Id ||
            !chatEmojiPopup.IsFocusWithin && !chatGif.IsFocusWithin && !chatInput.IsFocusWithin)) CloseChatGifs();
        if (chatMessageMenu.IsOpen && (!CurrentChatMenu(chatMenuContext) || !chatMessageMenu.IsFocusWithin)) CloseChatMessageMenu();
        QueueVisibleChatGifPreviews();
    }
}
