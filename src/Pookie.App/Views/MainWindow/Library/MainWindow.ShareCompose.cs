using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
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
    private Image shareSelectedAvatar = null!;
    private Button shareSearchClear = null!, shareRecipientEdit = null!;
    private SearchActivityGlyph shareSearchGlyph = null!;
    private GlassCard shareSearchGlass = null!;
    private ShareMessageField shareMessageField = null!;
    private Button shareSend = null!;
    private TextBlock shareSendLabel = null!;
    private BusyButtonContent shareSendBusy = null!;
    private bool shareSendBusyHeld;
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
        shareSearchGlyph = new SearchActivityGlyph(MutedIcon).Center();
        var search = shareSearchGlass = new GlassCard { HoverEnabled = false, ShadowEnabled = false }.Height(48).CornerRadius(24)
            .Padding(16, 0).Background(GlassSheen.LightControlTint(TrackButtons.GlassSurface)).Child(
            new Grid().Columns("20,*,Auto").Rows("*").Spacing(10).Children(
                shareSearchGlyph,
                new Grid().Columns("*").Rows("*").Column(1).Children(shareRecipientSearch.CenterVertical(), placeholder), shareSearchClear.Column(2).CenterVertical()));
        void SearchFocus() => search.SetEmphasized(shareRecipientSearch.IsFocused);
        shareRecipientSearch.GotFocus += SearchFocus; shareRecipientSearch.LostFocus += SearchFocus;
        search.MouseDown += e => { if (!e.Handled && e.Button == MouseButton.Left) shareRecipientSearch.Focus(); };
        shareRecipientSearch.OnTextChanged(_ => { placeholder.IsVisible = shareRecipientSearch.Text.Length == 0; shareSearchClear.IsVisible = !placeholder.IsVisible; shareSearchGlyph.SetInk(placeholder.IsVisible ? MutedIcon : Color.White); StopShareSearch(); if (shareTab.Value == 1 && shareRecipient == null) shareSearchTimer.Start(); });
        shareRecipientSearch.OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; shareSearchTimer.Stop(); Run(SearchShareRecipientsAsync); } });
        shareSearchTimer.Tick += () => { shareSearchTimer.Stop(); Run(SearchShareRecipientsAsync); };
        shareRecipientChoices = new StackPanel().Vertical().Spacing(2);
        shareRecipientResults = new ScrollViewer().MaxHeight(204).Background(Color.Transparent).BorderThickness(0).Padding(0, 0, 18, 0).Content(shareRecipientChoices);
        shareRecipientEmptyArt = new Image().Source(ImageSource.FromResource<MainWindow>("Pookie.App.Assets.Stickle.route.png"))
            .Width(104).Height(100).StretchMode(Stretch.Uniform).CenterHorizontal();
        shareRecipientEmptyTitle = new TextBlock().FontSize(19).SemiBold().CenterHorizontal();
        shareRecipientEmptyText = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap)
            .TextAlignment(TextAlignment.Center).MaxWidth(300).CenterHorizontal();
        shareRecipientEmpty = new StackPanel().Vertical().Spacing(10).Margin(12, 12, 30, 12).Children(
            shareRecipientEmptyArt, shareRecipientEmptyTitle, shareRecipientEmptyText).IsVisible(false);
        shareRecipientPicker = new StackPanel().Vertical().Spacing(12).Children(search.Margin(0, 0, 18, 0), shareRecipientResults, shareRecipientEmpty);
        shareRecipientName = new TextBlock().FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).Left();
        shareRecipientDetails = new TextBlock().FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis).Left();
        shareSelectedAvatar = ShareAvatar();
        shareRecipientEdit = TrackButtons.Icon(Icons.View("pencil-simple", 18), () => { SetShareRecipient(null); Run(SearchShareRecipientsAsync); }, TrackButtons.Glass, 36).Hint("Изменить получателя");
        ((MaterialTrackButton)shareRecipientEdit).UseLightGlassFinish();
        var selected = new Grid().Columns("40,*,Auto").Rows("56").Spacing(12).Margin(0, 0, 18, 0).Children(ShareAvatarFrame(shareSelectedAvatar),
            new StackPanel().Vertical().Spacing(3).CenterVertical().Column(1).Children(shareRecipientName, shareRecipientDetails),
            shareRecipientEdit.Column(2).CenterVertical());
        shareComposeText = new MultiLineTextBox().Placeholder("Добавить пару слов к треку").Wrap(true).MaxLength(2000).FontSize(14)
            .BorderThickness(0).Padding(16, 12, 34, 12).Background(Color.Transparent).Foreground(TrackButtons.OnSurface);
        shareMessageField = new ShareMessageField(shareComposeText);
        shareComposeText.OnTextChanged(_ => RefreshShareCompose());
        shareSendLabel = new TextBlock().Text("Отправить").FontSize(13).SemiBold().Foreground(Color.White);
        shareSendBusy = new BusyButtonContent(shareSendLabel).Initialize();
        shareSend = TrackButtons.Action(shareSendBusy, () => Run(SendShareMessageAsync), TrackButtons.Glass, 44).Margin(0, 0, 18, 0);
        ((MaterialTrackButton)shareSend).GlassAccent(TrackButtons.BlueGlassAccent, Color.White);
        ((MaterialTrackButton)shareSend).UseGlassAccentStateLayer();
        shareComposeFields = new StackPanel().Vertical().Spacing(12).Children(selected, shareMessageField, shareSend);
        return new StackPanel().Vertical().Spacing(12).Children(shareRecipientPicker, shareComposeFields);
    }

    private void StopShareSearch()
    { ShareSearchLoading(false); ++shareSearchRevision; shareSearchTimer.Stop(); shareSearch?.Cancel(); shareSearch?.Dispose(); shareSearch = null; }

    private void ResetShareCompose()
    { shareRecipient = null; shareRecipientSearch.Text = ""; shareComposeText.Text = ""; ClearShareRecipientChoices(); RefreshShareCompose(); }

    private void SetShareRecipient(SoundCloudUser? user)
    {
        StopShareSearch(); shareRecipient = user; if (user != null) LoadShareAvatar(shareSelectedAvatar, user); RefreshShareCompose();
        SetShareMessage(user != null && chatPending.ContainsKey(user.Id) ? "Есть неподтверждённая отправка этому получателю. Проверь её в чатах перед повтором." : ""); FitShareContent();
        if (shareDialog.IsOpen && Window.FocusManager.FocusedElement is TextBox or MultiLineTextBox) shareFocusAnchor.Focus();
    }

    private void RefreshShareCompose(bool snap = false)
    {
        if (shareSend == null) return;
        shareRecipientPicker.IsVisible = shareRecipient == null;
        shareComposeFields.IsVisible = shareRecipient != null;
        shareRecipientName.Text = shareRecipient?.Username ?? "";
        shareRecipientDetails.Text = string.IsNullOrWhiteSpace(shareRecipient?.FullName) ? "Получатель сообщения" : shareRecipient.FullName;

        var busy = shareSending || shareSendBusyHeld;
        shareSendBusy.SetBusy(busy, !snap);
        shareRecipientEdit.IsEnabled = shareComposeText.IsEnabled = !shareSending;
        var available = !busy && shareRecipient is { Id: > 0 } recipient && recipient.Id != me?.Id &&
            !chatPending.ContainsKey(recipient.Id) &&
            me != null && api.Session != null && shareLink.Text.Length > 0 &&
            SharedMessageDraft("", shareComposeText.Text, shareLink.Text).Length <= ChatRequest.MaxBodyLength;
        TrackButtons.SetAvailability(shareSend, available, busy);
        shareSend.Transitions = snap ? [] : [Transition.Create(UIElement.OpacityProperty, 220)];
        shareSend.Opacity = busy ? .72 : 1;
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
        shareSearchGlyph?.SetLoading(loading);
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

}
