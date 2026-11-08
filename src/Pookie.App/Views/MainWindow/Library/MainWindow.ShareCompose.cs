using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBox shareRecipientSearch = null!;
    private MultiLineTextBox shareComposeText = null!;
    private StackPanel shareRecipientChoices = null!, shareRecipientPicker = null!;
    private FrameworkElement shareComposeFields = null!, shareRecipientEmpty = null!;
    private ScrollViewer shareRecipientResults = null!;
    private TextBlock shareRecipientName = null!, shareRecipientDetails = null!, shareRecipientEmptyTitle = null!, shareRecipientEmptyText = null!;
    private Image shareRecipientEmptyArt = null!;
    private Image shareSelectedAvatar = null!, shareSearchIcon = null!;
    private Button shareSearchClear = null!;
    private LoadingSpinner shareSearchBusy = null!;
    private Button sharePrepare = null!;
    private SoundCloudUser? shareRecipient;
    private CancellationTokenSource? shareSearch;
    private long shareSearchRevision;
    private readonly DispatcherTimer shareSearchTimer = new(TimeSpan.FromMilliseconds(350));

    private FrameworkElement ShareComposeView()
    {
        shareRecipientSearch = new TextBox().FontSize(14)
            .Height(28).Padding(0).Background(Color.Transparent).Foreground(TrackButtons.OnSurface).BorderThickness(0);
        var placeholder = new TextBlock().Text("Имя или профиль SoundCloud").FontSize(13).Foreground(Muted)
            .IsHitTestVisible(false).CenterVertical().Left();
        shareSearchClear = TrackButtons.Icon(Icons.View("x", 16, MutedIcon), () => { shareRecipientSearch.Text = ""; shareRecipientSearch.Focus(); }, TrackButtons.Text, 28)
            .Hint("Очистить поиск").IsVisible(false);
        shareSearchIcon = Icons.View("magnifying-glass", 20, MutedIcon).Center();
        shareSearchBusy = new LoadingSpinner().Width(18).Height(18).IsActive(false).IsVisible(false).Center();
        var search = new Border().Height(48).CornerRadius(24).Padding(16, 0).BorderThickness(0).Background(TrackButtons.TonalSurface).Child(
            new Grid().Columns("20,*,Auto").Rows("*").Spacing(10).Children(
                new Grid().Columns("*").Rows("*").Children(shareSearchIcon, shareSearchBusy),
                new Grid().Columns("*").Rows("*").Column(1).Children(shareRecipientSearch.CenterVertical(), placeholder), shareSearchClear.Column(2).CenterVertical()));
        search.Transitions = [Transition.Create(Control.BackgroundProperty, 150)];
        void SearchFocus() => search.Background = TrackButtons.StateColor(TrackButtons.TonalSurface, TrackButtons.OnSurface, shareRecipientSearch.IsFocused ? .1 : 0);
        shareRecipientSearch.GotFocus += SearchFocus; shareRecipientSearch.LostFocus += SearchFocus;
        search.MouseDown += e => { if (!e.Handled && e.Button == MouseButton.Left) shareRecipientSearch.Focus(); };
        shareRecipientSearch.OnTextChanged(_ => { placeholder.IsVisible = shareRecipientSearch.Text.Length == 0; shareSearchClear.IsVisible = !placeholder.IsVisible; StopShareSearch(); if (shareTab.Value == 1 && shareRecipient == null) shareSearchTimer.Start(); });
        shareRecipientSearch.OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; shareSearchTimer.Stop(); Run(SearchShareRecipientsAsync); } });
        shareSearchTimer.Tick += () => { shareSearchTimer.Stop(); Run(SearchShareRecipientsAsync); };
        shareRecipientChoices = new StackPanel().Vertical().Spacing(2);
        shareRecipientResults = new ScrollViewer().MaxHeight(204).Background(Color.Transparent).BorderThickness(0).Padding(0).Content(shareRecipientChoices);
        shareRecipientEmptyArt = new Image().Source(ImageSource.FromResource<MainWindow>("Pookie.App.Assets.Stickle.route.png"))
            .Width(104).Height(100).StretchMode(Stretch.Uniform).CenterHorizontal();
        shareRecipientEmptyTitle = new TextBlock().FontSize(19).SemiBold().CenterHorizontal();
        shareRecipientEmptyText = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap)
            .TextAlignment(TextAlignment.Center).MaxWidth(300).CenterHorizontal();
        shareRecipientEmpty = new StackPanel().Vertical().Spacing(10).Margin(12, 12).Children(
            shareRecipientEmptyArt, shareRecipientEmptyTitle, shareRecipientEmptyText).IsVisible(false);
        shareRecipientPicker = new StackPanel().Vertical().Spacing(12).Children(search, shareRecipientResults, shareRecipientEmpty);
        shareRecipientName = new TextBlock().FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).Left();
        shareRecipientDetails = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis).Left();
        shareSelectedAvatar = ShareAvatar();
        var selected = new Grid().Columns("40,*,Auto").Rows("56").Spacing(12).Children(ShareAvatarFrame(shareSelectedAvatar),
            new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(shareRecipientName, shareRecipientDetails),
            TrackButtons.Action(new TextBlock().Text("Изменить").FontSize(12), () => { SetShareRecipient(null); Run(SearchShareRecipientsAsync); }, TrackButtons.Tonal, 36).Column(2).CenterVertical());
        shareComposeText = new MultiLineTextBox().Placeholder("Добавить пару слов к треку").Wrap(true).MaxLength(2000).Height(76).FontSize(14)
            .BorderThickness(0).CornerRadius(24).Padding(16, 12).Background(TrackButtons.TonalSurface).Foreground(TrackButtons.OnSurface);
        shareComposeText.Transitions = [Transition.Create(Control.BackgroundProperty, 150)];
        void ComposeFocus() => shareComposeText.Background = TrackButtons.StateColor(TrackButtons.TonalSurface, TrackButtons.OnSurface,
            shareComposeText.IsFocused ? .1 : 0);
        shareComposeText.GotFocus += ComposeFocus; shareComposeText.LostFocus += ComposeFocus;
        shareComposeText.OnTextChanged(_ => RefreshShareCompose());
        sharePrepare = TrackButtons.Action(new TextBlock().Text("Подготовить сообщение").FontSize(13).SemiBold(), () => Run(PrepareShareMessageAsync), TrackButtons.Glass, 44);
        ((MaterialTrackButton)sharePrepare).UseLightGlassFinish();
        shareComposeFields = new StackPanel().Vertical().Spacing(12).Children(selected, shareComposeText, sharePrepare);
        return new StackPanel().Vertical().Spacing(12).Children(shareRecipientPicker, shareComposeFields);
    }

    private void StopShareSearch()
    { ShareSearchLoading(false); ++shareSearchRevision; shareSearchTimer.Stop(); shareSearch?.Cancel(); shareSearch?.Dispose(); shareSearch = null; }

    private void ResetShareCompose()
    { shareRecipient = null; shareRecipientSearch.Text = ""; shareComposeText.Text = ""; ClearShareRecipientChoices(); RefreshShareCompose(); }

    private void SetShareRecipient(SoundCloudUser? user)
    {
        StopShareSearch(); shareRecipient = user; if (user != null) LoadShareAvatar(shareSelectedAvatar, user); RefreshShareCompose(); SetShareMessage(""); FitShareContent();
        if (shareDialog.IsOpen) { if (user == null) shareRecipientSearch.Focus(); else shareComposeText.Focus(); }
    }

    private void RefreshShareCompose()
    {
        if (sharePrepare == null) return;
        shareRecipientPicker.IsVisible = shareRecipient == null;
        shareComposeFields.IsVisible = shareRecipient != null;
        shareRecipientName.Text = shareRecipient?.Username ?? "";
        shareRecipientDetails.Text = string.IsNullOrWhiteSpace(shareRecipient?.FullName) ? "Получатель сообщения" : shareRecipient.FullName;

        sharePrepare.IsEnabled = shareRecipient is { Id: > 0 } recipient && recipient.Id != me?.Id &&
            me != null && api.Session != null && shareLink.Text.Length > 0 &&
            SharedMessageDraft("", shareComposeText.Text, shareLink.Text).Length <= ChatRequest.MaxBodyLength;
    }

    private async Task SearchShareRecipientsAsync()
    {
        if (!shareDialog.IsOpen || shareClosing || shareTab.Value != 1 || shareRecipient != null) return;
        StopShareSearch();
        var revision = shareSearchRevision; var generation = shareGeneration; var session = api.Session; var account = me?.Id;
        ClearShareRecipientChoices();
        if (session == null || account == null) { SetShareMessage("Войди в SoundCloud, чтобы выбрать получателя."); return; }
        shareSearch = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = shareSearch.Token;
        var query = shareRecipientSearch.Text.Trim();
        var local = chatConversations.Select(c => c.Recipient).Where(u => u.Id != account &&
            (query.Length == 0 || u.Username.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        void ShowUsers(IEnumerable<SoundCloudUser> users)
        {
            ClearShareRecipientChoices();
            shareRecipientEmpty.IsVisible = false;
            foreach (var user in users.Where(u => u.Id > 0 && u.Id != account).DistinctBy(u => u.Id).Take(12))
            {
                var avatar = ShareAvatar();
                var subtitle = !string.IsNullOrWhiteSpace(user.FullName) && user.FullName != user.Username ? user.FullName
                    : chatConversations.Any(c => c.Recipient.Id == user.Id) ? "Недавний диалог" : "Профиль SoundCloud";
                var row = new Grid().Columns("40,*,20").Rows("*").Spacing(12).Children(ShareAvatarFrame(avatar),
                    new StackPanel().Vertical().Spacing(4).CenterVertical().Column(1).Children(
                        new TextBlock().Text(user.Username).FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).Left(),
                        new TextBlock().Text(subtitle).FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis).Left()),
                    Icons.View("arrow-right", 16, MutedIcon).CenterVertical().Column(2));
                var button = TrackButtons.Action(row, () => SetShareRecipient(user), TrackButtons.Text, 64).Padding(10, 4).CornerRadius(16);
                row.HorizontalAlignment = HorizontalAlignment.Stretch;
                shareRecipientChoices.Add(button); LoadShareAvatar(avatar, user);
            }
            shareRecipientResults.IsVisible = shareRecipientChoices.Children.Count > 0;
            FitShareContent();
        }
        void Empty(bool noMatches)
        {
            if (shareRecipientChoices.Children.Count == 0)
            {
                shareRecipientResults.IsVisible = false; shareRecipientEmpty.IsVisible = true;
                shareRecipientEmptyArt.IsVisible = noMatches;
                shareRecipientEmptyTitle.Text = noMatches ? "А тут тупик" : "Кому отправим?";
                shareRecipientEmptyText.Text = noMatches ? "Я искал вдоль и поперек, но никого не нашел..." : "Найди получателя по имени — введи хотя бы два символа.";
            }
            FitShareContent();
        }
        ShowUsers(local);
        SetShareMessage("");
        if (query.Length < 2) { Empty(false); return; }
        ShareSearchLoading(true);
        try
        {
            var result = await api.SearchResultsAsync(query, SearchSection.People, token);
            if (disposed || revision != shareSearchRevision || generation != shareGeneration || api.Session != session || me?.Id != account || shareTab.Value != 1 || shareRecipient != null) return;
            ShowUsers(local.Concat(result.Items.Select(i => i.User).OfType<SoundCloudUser>()));
            Empty(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed && revision == shareSearchRevision && generation == shareGeneration) SetShareMessage(FriendlyError(error)); }
        finally { if (!disposed && revision == shareSearchRevision && generation == shareGeneration) ShareSearchLoading(false); }
    }

    private void ShareSearchLoading(bool loading)
    {
        if (shareSearchBusy == null) return;
        shareSearchBusy.IsActive = shareSearchBusy.IsVisible = loading; shareSearchIcon.IsVisible = !loading;
    }

    private void ClearShareRecipientChoices()
    {
        var previous = shareRecipientChoices.Children.ToArray(); shareRecipientChoices.Clear();
        foreach (var child in previous.OfType<IDisposable>()) child.Dispose();
    }

    private Image ShareAvatar() => new Image().Width(40).Height(40).StretchMode(Stretch.UniformToFill).Source(Icons.Source("user", MutedIcon));
    private static Border ShareAvatarFrame(Image avatar) => new Border().Width(40).Height(40).CornerRadius(20)
        .Background(TrackButtons.TonalSurface).ClipToBounds().Child(avatar).CenterVertical();

    private void LoadShareAvatar(Image image, SoundCloudUser user)
    {
        var generation = shareGeneration; var session = api.Session;
        image.Source = Icons.Source("user", MutedIcon);
        if (string.IsNullOrWhiteSpace(user.AvatarUrl)) return;
        Run(async () =>
        {
            var source = await SharedArtworkAsync(user.AvatarUrl);
            if (!disposed && generation == shareGeneration && api.Session == session && shareDialog.IsOpen &&
                shareTab.Value == 1 && image.FindVisualRoot() == Window && source != null) image.Source = source;
        });
    }

    private static string SharedMessageDraft(string existing, string comment, string link)
    {
        var addition = string.Join("\n", new[] { comment.Trim(), link }.Where(s => s.Length > 0));
        if (addition.Length == 0) return existing;
        return existing.TrimEnd() is { Length: > 0 } text ? text == addition ? existing : text + "\n" + addition : addition;
    }

    private async Task PrepareShareMessageAsync()
    {
        if (!sharePrepare.IsEnabled || shareRecipient is not { } recipient || me == null || api.Session == null) return;
        var account = me.Id; var session = api.Session; var comment = shareComposeText.Text; var link = shareLink.Text; var item = shareTarget;
        CloseShare(true);
        await NavigateAsync(Page.Chats);
        if (me?.Id != account || api.Session != session) return;
        await SelectChatAsync(recipient);
        if (me?.Id != account || api.Session != session || chatRecipient?.Id != recipient.Id) return;
        var draft = SharedMessageDraft(chatInput.Text, comment, link);
        if (draft.Length > ChatRequest.MaxBodyLength) { chatThreadStatus.Value = "В черновике недостаточно места для ссылки. Сократи сообщение и добавь трек ещё раз."; return; }
        if (item != null) CacheChatMusic(link, item);
        chatInput.Text = draft; chatInput.CaretPosition = draft.Length; chatInput.Focus(); SaveChatDraft();
    }
}
