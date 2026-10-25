using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private bool chatFavoritesOpen;
    private ChatFavorite[]? chatSavedBound;
    private ItemsControl chatSavedMessages = null!;
    private FrameworkElement chatSavedView = null!, chatConversationView = null!, chatSavedEmpty = null!;
    private FrameworkElement ChatSavedView()
    {
        chatSavedMessages = new ItemsControl().Items(Array.Empty<ChatFavorite>(), f => $"{f.PeerId}:{f.Message.Id}").ItemHeight(90).VariableHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0, 4, 0, 4));
        chatSavedMessages.ItemTemplate = new DelegateTemplate<ChatFavorite>(context =>
        {
            var root = new Border().Background(Color.Transparent).BorderThickness(0).Padding(0); context.Register("root", root); return new ChatMessageColumn(root, () => chatColumns.ScrollGutter).Initialize();
        }, (_, favorite, _, context) =>
        {
            var root = context.Get<Border>("root");
            var peer = chatConversations.FirstOrDefault(c => c.Recipient.Id == favorite.PeerId)?.Recipient;
            var projection = ChatProtocol.Project(favorite.Message, chatAccountId, favorite.PeerId);
            var summary = ChatMediaSummaryFor(favorite.Message, favorite.PeerId);
            var text = ChatMediaReference.Parse(favorite.Message.Body) != null ? summary?.Caption ?? "Вложение" : projection.Text;
            var body = new ChatMessageText { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Copy = CopyText };
            body.OpenMusic = url => Run(() => OpenChatMusicUrlAsync(url));
            var title = TrackButtons.Action(new TextBlock().Text(peer?.Username ?? "Переписка").FontSize(12).SemiBold().Foreground(ChatAccent).TextTrimming(TextTrimming.CharacterEllipsis),
                () => Run(() => JumpChatMessageAsync(favorite.PeerId, favorite.Message.Id)), TrackButtons.Text, 26).Padding(0);
            var content = new StackPanel().Vertical().Spacing(5).Children(title, body,
                new TextBlock().Text(ChatPresentation.Day(favorite.SavedAt) + " · " + ChatPresentation.Time(favorite.SavedAt)).FontSize(10).Foreground(Muted).Right());
            var photo = summary?.Photos?.FirstOrDefault(p => p != null);
            if (photo != null) content.Insert(1, new Border().CornerRadius(10).ClipToBounds().BorderThickness(0).Child(new Image().Source(photo.Source).StretchMode(Stretch.UniformToFill).Width(240).Height(160)));
            root.Child = new Border().Background(ChatOutgoingColor).BorderThickness(0).CornerRadius(18).Padding(12,8).MaxWidth(520).Right().Child(content);
            var copy = new Command("chat.saved.copy", "Копировать"); root.Commands.Register(copy, () => CopyText(text));
            var original = new Command("chat.saved.original", "Перейти к сообщению"); root.Commands.Register(original, () => Run(() => JumpChatMessageAsync(favorite.PeerId, favorite.Message.Id)));
            var remove = new Command("chat.saved.remove", "Убрать из избранного"); root.Commands.Register(remove, () =>
            { chatWorkspace = chatWorkspace with { Favorites = chatWorkspace.Favorites.Where(f => f.PeerId != favorite.PeerId || f.Message.Id != favorite.Message.Id).ToArray() }; SaveChatWorkspace(); RefreshChatFavoriteMedia(); RefreshChatSaved(); });
            root.ContextMenu = new ContextMenu().Item(original).Item(copy).Item(remove);
        });
        chatSavedEmpty = new StackPanel().Vertical().Spacing(12).Center().Children(Icons.View("bookmark-simple", 44, ChatAccent).CenterHorizontal(),
            new TextBlock().Text("Избранное").FontSize(21).SemiBold().CenterHorizontal(),
            new TextBlock().Text("Сохраняй сюда сообщения из переписок").FontSize(13).Foreground(Muted).CenterHorizontal());
        chatSavedView = new Grid().Columns("*").Rows("*").Children(chatSavedMessages, chatSavedEmpty).IsVisible(false);
        return chatSavedView;
    }
    private void OpenChatFavorites() => Run(OpenChatFavoritesAsync);
    private async Task OpenChatFavoritesAsync()
    {
        await StopChatVoiceAsync(); await StopChatVoicePlaybackAsync(); SaveChatDraft(); RememberChatThread();
        CloseChatDetails(); CloseChatSearch(); ClearChatSelection(); CancelChatReads(); ++chatRevision; ++chatAvatarGeneration;
        chatRecipient = null; chatFavoritesOpen = true; chatMessageSearch.Text = ""; chatFollowLayout = false; chatColumns.Selected = true;
        chatWorkspace = chatWorkspace with { SavedOpen = true }; SaveChatWorkspace();
        chatName.Text = "Избранное"; chatHeaderAvatar.Source = Icons.Source("bookmark-simple", ChatAccent);
        chatConversationView.IsVisible = false; chatSavedView.IsVisible = true; chatFloatingDate.IsVisible = false; chatThreadStatus.Value = "";
        chatNewMessages.IsVisible = false; chatReply = null; RefreshChatReply(); RefreshChatComposer(); RefreshChatSaved();
    }
    private void OpenChatSavedInfo()
    {
        if (chatColumns.DetailsOpen && chatInfoPeer == 0) return;
        CloseChatSearch(); OpenChatDetails("Избранное");
        chatInfoPeer = 0;
        chatDetailsBody.Add(new TextBlock().Text($"{chatWorkspace.Favorites.Length} сохранённых сообщений").FontSize(13).Foreground(Muted).Margin(8,12));
        foreach (var group in chatWorkspace.Favorites.GroupBy(f => f.PeerId).OrderByDescending(g => g.Max(f => f.SavedAt)))
        {
            var favorite = group.OrderByDescending(f => f.SavedAt).First();
            chatDetailsBody.Add(ChatSearchResult(new(favorite.PeerId, favorite.Message), () => Run(() => JumpChatMessageAsync(favorite.PeerId, favorite.Message.Id))));
        }
    }
    private void RefreshChatSaved()
    {
        if (chatSavedMessages == null) return;
        chatSavedEmpty.IsVisible = chatWorkspace.Favorites.Length == 0;
        chatSavedMessages.IsVisible = chatWorkspace.Favorites.Length > 0;
        if (chatFavoritesOpen && !ReferenceEquals(chatSavedBound, chatWorkspace.Favorites))
        {
            chatSavedBound = chatWorkspace.Favorites;
            chatSavedMessages.ItemsSource = ItemsView.Create(chatWorkspace.Favorites.OrderBy(f => f.SavedAt).ToArray(), f => f.Message.Body, f => $"{f.PeerId}:{f.Message.Id}");
        }
    }
}
