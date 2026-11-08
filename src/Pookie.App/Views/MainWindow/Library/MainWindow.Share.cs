using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ModalDialog shareDialog = null!;
    private PlaylistDialogContent shareContent = null!;
    private PaletteGlassCard sharePreview = null!;
    private Image shareCover = null!;
    private TextBlock shareTitle = null!, shareAuthor = null!, shareMessage = null!;
    private TextBlock shareTrackDuration = null!, shareTrackPlays = null!;
    private StackPanel shareTrackStats = null!;
    private TextBox shareLink = null!;
    private Image shareCopyIcon = null!;
    private Button shareCopy = null!;
    private StackPanel shareForm = null!;
    private FrameworkElement shareLinksPanel = null!, shareComposePanel = null!, shareEmbedPanel = null!;
    private readonly ObservableValue<int> shareTab = new(0);
    private Button playerShareButton = null!, expandedShareButton = null!;
    private readonly List<Button> shareDestinations = [];
    private LibraryItem? shareTarget;
    private Uri? shareUri;
    private CancellationTokenSource? shareRequest;
    private long shareGeneration;
    private bool shareClosing;

    private void ShareTrack(SoundCloudTrack track) => OpenShare(LibraryItem.FromTrack(track));

    private Button ShareTrackButton(Func<SoundCloudTrack?> track, string style = TrackButtons.Glass, double size = 44) =>
        TrackButtons.Icon(Icons.View("arrow-square-out", 20), () => { if (track() is { } value) OpenShare(LibraryItem.FromTrack(value)); }, style, size).Hint("Поделиться");

    private FrameworkElement ShareView()
    {
        shareTitle = new TextBlock().FontSize(15).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        shareAuthor = new TextBlock().FontSize(13).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        shareCover = new Image().Width(56).Height(56).StretchMode(Stretch.UniformToFill);
        shareTrackDuration = new TextBlock().FontSize(13).Foreground(Muted).Right();
        shareTrackPlays = new TextBlock().FontSize(12).Foreground(Muted).Right();
        shareTrackStats = new StackPanel().Vertical().Spacing(5).Children(shareTrackDuration, shareTrackPlays);
        sharePreview = TrackDialogChrome.Preview(ArtworkLayer(shareCover), shareTitle, shareAuthor, shareTrackStats);
        shareLink = new TextBox { IsReadOnly = true }.FontSize(13).Background(Color.Transparent).BorderThickness(0).Padding(0).Height(26);
        shareCopyIcon = Icons.View("copy", 20);
        shareCopy = TrackButtons.Icon(shareCopyIcon, CopyShareLink, TrackButtons.Tonal, 48).Hint("Скопировать ссылку");
        var actions = TrackButtons.Connected(ShareTimeButton(), shareCopy);
        shareMessage = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        var destinations = new Grid().Columns("*,*,*,*").Rows("48").Spacing(8);
        foreach (var (label, service, icon) in new[] { ("Telegram", "telegram", "SimpleIcons.social-telegram"), ("ВКонтакте", "vk", "SimpleIcons.social-vk"), ("X", "x", "SimpleIcons.social-x"), ("Почта", "email", "envelope") })
        {
            var button = TrackButtons.Action(new StackPanel().Horizontal().Spacing(7).Center().Children(Icons.View(icon, 17),
                new TextBlock().Text(label).FontSize(12).SemiBold()), () => OpenShareDestination(service), TrackButtons.Tonal, 48).Padding(4, 0).CornerRadius(14);
            destinations.Add(button.Column(shareDestinations.Count)); shareDestinations.Add(button);
        }
        shareLinksPanel = new StackPanel().Vertical().Spacing(16).Children(
            new Grid().Columns("*,Auto").Rows("48").Spacing(12).Children(
                new Border().Background(TrackButtons.TonalSurface).BorderThickness(0).CornerRadius(24).Padding(16, 0)
                    .Child(shareLink.CenterVertical()), actions.Column(1)), destinations);
        shareComposePanel = ShareComposeView().IsVisible(false);
        shareEmbedPanel = ShareEmbedView().IsVisible(false);
        var tabs = new StackPanel().Horizontal().Spacing(24).Children(
            SectionTab("Ссылка", shareTab, 0, () => SelectShareTab(0)),
            SectionTab("Сообщение", shareTab, 1, () => SelectShareTab(1)),
            SectionTab("Встроить", shareTab, 2, () => SelectShareTab(2)));
        shareForm = new StackPanel().Vertical().Spacing(16).Margin(24).Children(
            tabs, shareLinksPanel, shareComposePanel, shareEmbedPanel, shareMessage);
        var card = new Border().Background(PopupAppearance.Surface).BorderThickness(0).CornerRadius(24).ClipToBounds()
            .MaxWidth(PlaylistDialogContent.MaximumWidth).Child(shareForm);
        shareContent = new PlaylistDialogContent(card, sharePreview);
        var close = TrackButtons.Icon(Icons.View("x", 20), () => CloseShare(), TrackButtons.Glass).Top();
        ((MaterialTrackButton)close).UseLightGlassFinish();
        shareDialog = new ModalDialog(shareContent, shareLink, close)
        {
            DismissRequested = () => CloseShare(), Presented = shareContent.RevealTrack,
            Closed = () =>
            {
                shareClosing = false; shareContent.FreezeSize(); shareContent.ResetTrack(); sharePreview.ResetPalette();
                StopCardArtwork(shareCover); shareCover.Source = null;
            }
        };
        ((MaterialTrackButton)close).UseOverlayBackdrop(shareDialog, shareDialog.RenderGlassBackdrop);
        return shareDialog;
    }

    private void OpenShare(LibraryItem item)
    {
        if (item.Track == null && item.Playlist == null) return;
        ResetShareRequest();
        var generation = shareGeneration;
        var reopening = shareDialog.IsOpen;
        if (!shareClosing) { shareContent.ResetTrack(); sharePreview.ResetPalette(); }
        shareClosing = false; shareContent.IsHitTestVisible = true;
        shareTarget = item; shareUri = TrackLink(item.Track?.PermalinkUrl ?? item.Playlist?.PermalinkUrl);
        shareTitle.Text = item.Title; shareAuthor.Text = item.Track?.Author ?? item.Playlist?.User?.Username ?? item.Subtitle;
        RefreshShareTrackStats();
        shareStartSeconds = null; UpdateShareTimeLabel(false);
        shareTimeAction.IsVisible = item.Track != null;
        if (item.Track != null) ((MaterialTrackButton)shareCopy).Connect(false);
        else ((MaterialTrackButton)shareCopy).Disconnect();
        ResetShareCompose(); ResetShareEmbed(); SelectShareTab(0);
        RefreshShareLink();
        SetCardArtwork(shareCover, null, item.ArtworkUrl, item.Track != null ? Icons.MissingTrackArtwork : "music-notes");
        shareRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = shareRequest.Token;
        FitShareContent();
        shareDialog.Show();
        if (reopening) shareContent.RevealTrack();
        Run(() => LoadShareArtworkAsync(item, generation, token));
        if (shareUri == null && item.Track is { Id: > 0 } track) Run(() => ResolveShareTrackAsync(track, generation, token));
    }

    private void ResetShareRequest()
    {
        shareTimePopup?.Close(true, false);
        ++shareGeneration; StopShareSearch(); shareRequest?.Cancel(); shareRequest?.Dispose(); shareRequest = null;
    }

    private void CloseShare(bool immediate = false)
    {
        ResetShareRequest(); shareTarget = null; shareUri = null;
        if (immediate) { shareDialog?.Close(true); return; }
        if (shareDialog?.IsOpen != true || shareClosing) return;
        shareClosing = true; shareContent.FreezeSize(); shareContent.IsHitTestVisible = false;
        shareContent.HideTrack(() => { shareClosing = false; shareDialog.Close(); });
    }

    private async Task ResolveShareTrackAsync(SoundCloudTrack track, long generation, CancellationToken token)
    {
        try
        {
            var resolved = await api.GetTrackAsync(track.Id, token);
            if (disposed || generation != shareGeneration) return;
            shareUri = TrackLink(resolved.PermalinkUrl); shareTarget = LibraryItem.FromTrack(resolved); RefreshShareTrackStats(); RefreshShareLink();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error)
        { if (!disposed && generation == shareGeneration) SetShareMessage(FriendlyError(error)); }
    }

    private async Task LoadShareArtworkAsync(LibraryItem item, long generation, CancellationToken token)
    {
        var image = await FetchImageAsync(item.ArtworkUrl, token, maximumSide: 256);
        if (disposed || generation != shareGeneration || !shareDialog.IsOpen) return;
        SetCardArtwork(shareCover, image, item.ArtworkUrl, item.Track != null ? Icons.MissingTrackArtwork : "music-notes", finished: true);
        if (image == null) return;
        var palette = await playerPalettes.GetValue(image, source => Task.Run(() => PlayerPalette.FromArtwork(source), lifetime.Token));
        if (!disposed && generation == shareGeneration && shareDialog.IsOpen) sharePreview.SetPalette(palette);
    }

    private void RefreshShareLink()
    {
        var link = shareUri == null ? "" : new UriBuilder(shareUri) { Fragment = shareStartSeconds is { } seconds && shareTarget?.Track != null ? "t=" + FormatTime(seconds) : "" }.Uri.AbsoluteUri;
        shareLink.Text = link; shareCopyIcon.Source = Icons.Source("copy"); shareCopy.Hint("Скопировать ссылку");
        shareCopy.IsEnabled = link.Length > 0;
        shareTimeAction.IsEnabled = shareCopy.IsEnabled && shareTarget?.Track is { DurationSeconds: > 0 } track && double.IsFinite(track.DurationSeconds);
        foreach (var button in shareDestinations) button.IsEnabled = shareCopy.IsEnabled;
        if (shareTab.Value != 2)
            SetShareMessage(shareUri == null ? "У этого трека или плейлиста пока нет ссылки для отправки." : "");
        RefreshShareCompose(); RefreshShareEmbed();
    }

    private void RefreshShareTrackStats()
    {
        var track = shareTarget?.Track;
        shareTrackStats.IsVisible = track != null;
        shareTrackDuration.Text = track == null ? "" : FormatTime(track.DurationSeconds);
        shareTrackPlays.Text = track?.PlaybackCount is { } count ? LikedTrackRow.ShortCount(count) : "—";
    }

    private void CopyShareLink()
    {
        if (!shareCopy.IsEnabled || shareLink.Text.Length == 0) return;
        var clipboard = shareLink.ClipboardService ?? Application.Current.PlatformServices.Clipboard;
        var copied = clipboard?.TrySetText(shareLink.Text) == true;
        status.Value = copied ? "Скопировано." : "Не удалось скопировать.";
        shareCopyIcon.Source = Icons.Source(copied ? "check" : "copy");
        shareCopy.Hint(copied ? "Скопировано" : "Не удалось скопировать. Повторить");
    }

    private void OpenShareDestination(string service)
    {
        if (!shareCopy.IsEnabled || shareTarget == null) return;
        if (service == "email")
        {
            var title = shareTarget.Title; var body = shareLink.Text;
            Run(async () =>
            {
                try { await Platform.ExternalBrowser.OpenMailAsync(title, body, lifetime.Token); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error) { if (!disposed) SetShareMessage("Не удалось открыть почтовое приложение."); Pookie.Logging.AppLog.Failure("Pookie.Share", "Не удалось открыть почту", error); }
            });
            return;
        }
        var link = Uri.EscapeDataString(shareLink.Text); var text = Uri.EscapeDataString(shareTarget.Title);
        var url = service switch
        {
            "telegram" => $"https://t.me/share/url?url={link}&text={text}",
            "vk" => $"https://vk.com/share.php?url={link}&title={text}",
            "x" => $"https://twitter.com/intent/tweet?url={link}&text={text}",
            _ => null
        };
        if (url != null) OpenExternalUrl(url, () => SetShareMessage("Не удалось открыть браузер."));
    }

    private void SelectShareTab(int tab)
    {
        shareTimePopup?.Close(true, false);
        if (tab != 1) StopShareSearch();
        shareTab.Value = tab;
        shareLinksPanel.IsVisible = tab == 0; shareComposePanel.IsVisible = tab == 1; shareEmbedPanel.IsVisible = tab == 2;
        SetShareMessage("");
        if (tab == 1) { RefreshShareCompose(); Run(SearchShareRecipientsAsync); }
        if (tab == 2) RefreshShareEmbed();
        FitShareContent();
        if (shareDialog?.IsOpen == true)
        {
            if (tab == 0) shareLink.Focus();
            else if (tab == 1) { if (shareRecipient == null) shareRecipientSearch.Focus(); else shareComposeText.Focus(); }
            else shareEmbedColor.Focus();
        }
    }

    private void SetShareMessage(string text)
    { shareMessage.Text = text; shareMessage.IsVisible = text.Length > 0; FitShareContent(); }

    private void FitShareContent()
    {
        if (shareContent == null || shareForm == null || shareClosing) return;
        var width = shareContent.BodyBounds.Width > 0 ? shareContent.BodyBounds.Width : PlaylistDialogContent.PreferredWidth;
        shareForm.Measure(new(width, double.PositiveInfinity));
        shareContent.FitCreation(shareForm.DesiredSize.Height);
    }
}
