using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
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
    private TextBlock shareLink = null!;
    private IClipboardService? shareClipboard;
    private Border shareFocusAnchor = null!;
    private CopyFeedbackGlyph shareCopyIcon = null!;
    private Grid shareLinkRow = null!;
    private GlassCard shareLinkGlass = null!;
    private Button shareCopy = null!;
    private StackPanel shareForm = null!;
    private DialogPageDeck sharePages = null!;
    private StackPanel shareLinksPanel = null!;
    private FrameworkElement shareComposePanel = null!, shareEmbedPanel = null!;
    private readonly ObservableValue<int> shareTab = new(0);
    private Button playerShareButton = null!, expandedShareButton = null!;
    private readonly List<Button> shareDestinations = [];
    private LibraryItem? shareTarget;
    private Uri? shareUri;
    private CancellationTokenSource? shareRequest;
    private long shareGeneration;
    private bool shareClosing;
    private bool sharePreparingClose;
    private Button[] shareSectionTabs = [];

    private void UpdatePageCacheForOverlay()
    {
        // Reuse the page and its header glass sample while a foreground layer moves.
        // A change to the underlying page invalidates the bitmap normally.
        if (shareDialog.IsVisible || playlistDialog.IsVisible)
            contentSurface.CacheMode ??= new BitmapCache();
        else
            contentSurface.CacheMode = null;
    }

    private void ShareTrack(SoundCloudTrack track) => OpenShare(LibraryItem.FromTrack(track));

    private Button ShareTrackButton(Func<SoundCloudTrack?> track, string style = TrackButtons.Glass, double size = 44) =>
        TrackButtons.Icon(Icons.View("arrow-square-out", 20), () => { if (track() is { } value) OpenShare(LibraryItem.FromTrack(value)); }, style, size).Hint(UiText.Get("common.share"));

    private FrameworkElement ShareView()
    {
        shareTitle = new TextBlock().FontSize(15).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        shareAuthor = new TextBlock().FontSize(13).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        shareCover = new Image().Width(56).Height(56).StretchMode(Stretch.UniformToFill);
        shareTrackDuration = new TextBlock().FontSize(13).Foreground(Muted).Right();
        shareTrackPlays = new TextBlock().FontSize(12).Foreground(Muted).Right();
        shareTrackStats = new StackPanel().Vertical().Spacing(5).Children(shareTrackDuration, PlayCountLine(shareTrackPlays, 12, MutedIcon));
        sharePreview = TrackDialogChrome.Preview(ArtworkLayer(shareCover), shareTitle, shareAuthor, shareTrackStats);
        shareLink = new TextBlock().FontSize(13).TextTrimming(TextTrimming.CharacterEllipsis).Height(26)
            .VerticalTextAlignment(TextAlignment.Center).IsHitTestVisible(false);
        mixLinkPlaceholder = new TextBlock().FontSize(13).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis)
            .VerticalTextAlignment(TextAlignment.Center).Height(26).IsHitTestVisible(false).IsVisible(false);
        shareCopyIcon = new CopyFeedbackGlyph { Reverted = () => shareCopy.Hint(UiText.Get("share.link.copy")) };
        shareCopy = TrackButtons.Icon(shareCopyIcon, CopyShareLink, TrackButtons.Glass, 48).Hint(UiText.Get("share.link.copy"));
        ((MaterialTrackButton)shareCopy).GlassAccent(TrackButtons.BlueGlassAccent, Color.White);
        ((MaterialTrackButton)shareCopy).UseGlassAccentStateLayer();
        var time = ShareTimeButton();
        shareLinkGlass = new GlassCard { HoverEnabled = false, ShadowEnabled = false }.CornerRadius(24)
            .Background(GlassSheen.LightControlTint(TrackButtons.GlassSurface)).Padding(0).Child(
                new Grid().Columns("*,Auto").Rows("48").Children(
                    shareLink.Column(0).CenterVertical().Margin(16, 0, 8, 0), mixLinkPlaceholder.Column(0).CenterVertical().Margin(16, 0, 8, 0),
                    time.Column(1), ShareAccessButton().Column(1)));
        shareMessage = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        var destinations = new ShareDestinationsRow();
        foreach (var (label, service, icon, color) in new[]
        {
            ("Telegram", "telegram", "SimpleIcons.social-telegram", Color.FromRgb(38, 165, 228)),
            (UiText.Get("share.platform.vk"), "vk", "SimpleIcons.social-vk", Color.FromRgb(0, 119, 255)),
            ("X", "x", "SimpleIcons.social-x", Color.Black),
            ("Snapchat", "snapchat", "snapchat", Color.FromRgb(255, 252, 0)),
            (UiText.Get("share.platform.email"), "email", "envelope", Color.FromRgb(227, 113, 53))
        })
        {
            var foreground = service == "snapchat" ? Color.Black : Color.White;
            var button = TrackButtons.Icon(Icons.View(icon, service == "snapchat" ? 32 : 24, foreground), () => OpenShareDestination(service),
                TrackButtons.Tonal, 56).CenterHorizontal();
            ((MaterialTrackButton)button).Palette(color, foreground);
            destinations.Add(new StackPanel().Vertical().Spacing(8).CenterHorizontal().Children(button,
                new TextBlock().Text(label).FontSize(12).Foreground(TrackButtons.OnSurface).CenterHorizontal()));
            shareDestinations.Add(button);
        }
        shareLinksPanel = new StackPanel().Vertical().Spacing(16).Children(
            shareLinkRow = new Grid().Columns("*,Auto").Rows("48").Spacing(12).Children(
                shareLinkGlass, shareCopy.Column(1)), SharePrivateView(), shareDestinationsPanel = destinations);
        shareComposePanel = ShareComposeView().IsVisible(false);
        shareEmbedPanel = ShareEmbedView().IsVisible(false).Margin(0, 0, 18, 0);
        shareLinksPanel.Margin(0, 0, 18, 0);
        // These tabs reset while their modal is hidden. Framework property
        // transitions can otherwise retain the previous tab's animated value.
        shareSectionTabs = [SectionTab(UiText.Get("share.tabs.link"), shareTab, 0, () => SelectShareTab(0), animateSelection: false),
            SectionTab(UiText.Get("share.tabs.message"), shareTab, 1, () => SelectShareTab(1), animateSelection: false),
            SectionTab(UiText.Get("playlist.edit.widget"), shareTab, 2, () => SelectShareTab(2), animateSelection: false)];
        var tabs = new StackPanel().Horizontal().Spacing(24).Children(shareSectionTabs).Margin(0, 0, 18, 0);
        shareFocusAnchor = new Border { Focusable = true, IsTabStop = false }.Background(Color.Transparent).BorderThickness(0).Child(tabs);
        sharePages = new DialogPageDeck(shareLinksPanel, shareComposePanel, shareEmbedPanel);
        shareForm = new StackPanel().Vertical().Spacing(16).Margin(24, 24, 6, 24).Children(
            shareFocusAnchor, sharePages, shareMessage.Margin(0, 0, 18, 0));
        var card = TrackDialogChrome.MatteBody(shareForm, () => shareContent?.Resizing == true);
        shareContent = new PlaylistDialogContent(card, sharePreview);
        var close = TrackButtons.Icon(Icons.View("x", 20), () => CloseShare(), TrackButtons.Glass).Top();
        ((MaterialTrackButton)close).UseLightGlassFinish();
        shareDialog = TrackDialogChrome.MatteDialog(shareContent, card, shareFocusAnchor, close, () => sharePages.Animating, tall: true);
        shareDialog.DismissRequested = () => CloseShare();
        // Animate the plaque on the live tree, after the perspective transition.
        // Otherwise its motion recaptures and blurs the whole dialog each frame.
        shareDialog.Opened = shareContent.RevealTrack;
        shareDialog.Closed = () =>
        {
            UpdatePageCacheForOverlay();
            if (!shareSending) { shareSendBusyHeld = false; RefreshShareCompose(snap: true); }
            shareWidgetPreview.Close();
            shareClosing = false; shareContent.FreezeSize(); shareContent.ResetTrack(); sharePreview.ResetPalette();
            StopCardArtwork(shareCover); shareCover.Source = null;
        };
        return shareDialog;
    }

    private async Task WarmShareDialogAsync()
    {
        shareContent.ShowTrackImmediately();
        try
        {
            // Prime each hidden tab's editors, glyphs and graphics before a
            // user can start a height transition. No recipient lookup is made
            // while the dialog is closed; the iframe is plain editor text.
            foreach (var tab in new[] { 1, 2, 0 })
            {
                SelectShareTab(tab);
                if (tab == 1) { shareRecipientEmpty.IsVisible = true; shareRecipientResults.IsVisible = false; }
                if (tab == 2) shareEmbedCode.Text = BuildShareEmbed(new("https://soundcloud.com/"), false, false, "#ff5500");
                FitShareContent(); shareDialog.InvalidateTransitionSnapshot();
                await shareDialog.WarmTransitionAsync(lifetime.Token);
            }
        }
        finally
        {
            shareRecipientEmpty.IsVisible = false; shareRecipientResults.IsVisible = true;
            shareEmbedCode.Text = ""; SelectShareTab(0); shareContent.ResetTrack(); shareDialog.InvalidateTransitionSnapshot();
        }
        // Cache the normal opening state, with the plaque still hidden.
        await shareDialog.WarmTransitionAsync(lifetime.Token);
    }

    private void OpenShare(LibraryItem item)
    {
        if (item.Track == null && item.Playlist == null) return;
        tooltipLayer.ResetDismissal(shareForm);
        ResetShareRequest();
        var generation = shareGeneration;
        var reopening = shareDialog.IsOpen;
        if (!shareClosing) { shareContent.ResetTrack(); sharePreview.ResetPalette(); }
        shareClosing = sharePreparingClose = false; shareWidgetPreview.ResumeFromPopup(); shareContent.IsHitTestVisible = true;
        shareSendBusyHeld = false;
        shareTarget = item;
        shareAccessAction = ShareAccessAction.None;
        var mix = IsSystemCollection(item);
        mixShareLoading = mix && !demo;
        sharePrivateLoading = !mix && !demo && item.Playlist is { Id: > 0, IsPrivate: true };
        shareUri = sharePrivateLoading || mixShareLoading ? null : item.Playlist is { } playlist ? PlaylistSharing.ShareUri(playlist) : TrackLink(item.Track?.PermalinkUrl);
        RefreshPrivateShare(opening: true);
        shareTitle.Text = item.Title; shareAuthor.Text = item.Track?.Author ?? item.Playlist?.MadeFor?.Username ?? item.Playlist?.User?.Username ?? item.Subtitle;
        RefreshShareTrackStats();
        shareStartSeconds = null; UpdateShareTimeLabel(false);
        shareTimeAction.IsVisible = item.Track != null;
        ResetShareCompose(); ResetShareEmbed(); SelectShareTab(0, animate: false);
        RefreshShareLink();
        SetCollectionArtwork(shareCover, item);
        shareRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = shareRequest.Token;
        FitShareContent();
        shareDialog.InvalidateTransitionSnapshot();
        shareDialog.Show();
        UpdatePageCacheForOverlay();
        if (reopening) shareContent.RevealTrack();
        Run(() => LoadShareArtworkAsync(item, generation, token));
        if (mixShareLoading) Run(() => LoadMixSharingAsync(item.Playlist!.Urn, generation, token));
        if (sharePrivateLoading && item.Playlist is { } privatePlaylist) Run(() => ResolveSharePlaylistAsync(privatePlaylist, generation, token));
        if (shareUri == null && item.Track is { Id: > 0 } track) Run(() => ResolveShareTrackAsync(track, generation, token));
    }

    private void ResetShareRequest()
    {
        shareCopyIcon.Reset();
        shareActionsFade.Stop();
        shareTimePopup?.Close(true, false); shareAccessPopup?.Close(true, false);
        ++shareGeneration; StopShareSearch(); shareRequest?.Cancel(); shareRequest?.Dispose(); shareRequest = null;
    }

    private void CloseShare(bool immediate = false)
    {
        if (immediate)
        {
            shareWidgetColorPopup?.Close(true, false); shareWidgetPreview?.Deactivate();
            ResetShareRequest(); shareTarget = null; shareUri = null;
            sharePreparingClose = false; shareDialog?.Close(true); return;
        }
        if (shareDialog?.IsOpen != true || shareClosing) return;
        shareClosing = sharePreparingClose = true; shareContent.FreezeSize(); shareContent.IsHitTestVisible = false;
        ResetShareRequest(); shareTarget = null; shareUri = null;
        var generation = shareGeneration;
        Run(async () =>
        {
            await shareWidgetPreview.FreezeForPopupAsync();
            if (disposed || generation != shareGeneration || !shareClosing)
            { shareWidgetPreview.ResumeFromPopup(); return; }
            sharePreparingClose = false;
            shareWidgetColorPopup?.Close(); shareWidgetPreview.Deactivate();
            shareContent.HideTrack(() => { shareClosing = false; shareDialog.Close(); });
        });
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
        var image = CachedCollectionArtwork(item) ?? await FetchImageAsync(item.ArtworkUrl, token, maximumSide: 256);
        if (disposed || generation != shareGeneration || !shareDialog.IsOpen) return;
        SetCardArtwork(shareCover, image, item.ArtworkUrl, item.Track != null ? Icons.MissingTrackArtwork : "music-notes", finished: true);
        if (image == null) return;
        var palette = await playerPalettes.GetValue(image, source => Task.Run(() => PlayerPalette.FromArtwork(source), lifetime.Token));
        var colors = await Task.Run(() => ArtworkSwatches.FromArtwork(image), lifetime.Token);
        if (!disposed && generation == shareGeneration && shareDialog.IsOpen) { sharePreview.SetPalette(palette); SetShareWidgetSwatches(colors); }
    }

    private void RefreshShareLink()
    {
        shareDialog?.InvalidateTransitionSnapshot();
        var link = shareUri == null ? "" : new UriBuilder(shareUri) { Fragment = shareStartSeconds is { } seconds && shareTarget?.Track != null ? "t=" + FormatTime(seconds) : "" }.Uri.AbsoluteUri;
        shareLink.Text = link; shareCopyIcon.Reset(shareDialog?.IsOpen == true); shareCopy.Hint(UiText.Get("share.link.copy"));
        shareCopy.IsEnabled = link.Length > 0;
        shareTimeAction.IsEnabled = shareCopy.IsEnabled && shareTarget?.Track is { DurationSeconds: > 0 } track && double.IsFinite(track.DurationSeconds);
        foreach (var button in shareDestinations) button.IsEnabled = shareCopy.IsEnabled;
        if (shareTab.Value != 2)
            SetShareMessage(IsSystemCollection(shareTarget) ? "" :
                sharePrivateLoading ? UiText.Get("share.link.loadingSecret") : shareUri == null ?
                shareTarget?.Playlist?.IsPrivate == true ? UiText.Get("share.link.reopenAfterFailure") : UiText.Get("share.item.missingLink") : "");
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
        var clipboard = shareClipboard ?? Application.Current.PlatformServices.Clipboard;
        var copied = clipboard?.TrySetText(shareLink.Text) == true;
        status.Value = copied ? UiText.Get("clipboard.copiedStatus") : UiText.Get("clipboard.copyFailed");
        if (copied) shareCopyIcon.ShowCopied(); else shareCopyIcon.Reset(true);
        shareCopy.Hint(copied ? UiText.Get("clipboard.copied") : UiText.Get("clipboard.copyRetry"));
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
                catch (Exception error) { if (!disposed) SetShareMessage(UiText.Get("share.email.launchFailed")); Pookie.Logging.AppLog.Failure("Pookie.Share", "Не удалось открыть почту", error); }
            });
            return;
        }
        var link = Uri.EscapeDataString(shareLink.Text); var text = Uri.EscapeDataString(shareTarget.Title);
        var url = service switch
        {
            "telegram" => $"https://t.me/share/url?url={link}&text={text}",
            "vk" => $"https://vk.com/share.php?url={link}&title={text}",
            "x" => $"https://twitter.com/intent/tweet?url={link}&text={text}",
            "snapchat" => $"https://www.snapchat.com/share?link={link}",
            _ => null
        };
        if (url != null) OpenExternalUrl(url, () => SetShareMessage(UiText.Get("browser.openFailed")));
    }

    private void SelectShareTab(int tab, bool animate = true)
    {
        if (IsSystemCollection(shareTarget) && (tab == 2 || tab == 1 && !shareSectionTabs[1].IsEnabled)) return;
        shareTimePopup?.Close(true, false); shareAccessPopup?.Close(true, false);
        if (tab != 1) StopShareSearch();
        if (tab != 2) shareWidgetColorPopup?.Close(true, false);
        shareTab.Value = tab;
        sharePages.Show(tab, animate && shareDialog?.IsOpen == true && !shareClosing);
        if (tab != 2) shareWidgetPreview.Deactivate();
        SetShareMessage("");
        if (tab == 1) { RefreshShareCompose(); Run(SearchShareRecipientsAsync); }
        if (tab == 2) RefreshShareEmbed();
        FitShareContent();
        if (shareDialog?.IsOpen == true && Window.FocusManager.FocusedElement is TextBox or MultiLineTextBox)
            shareFocusAnchor.Focus();
    }

    private void SetShareMessage(string text)
    { shareMessage.Text = text; shareMessage.IsVisible = text.Length > 0; FitShareContent(); }

    private void FitShareContent()
    {
        if (shareContent == null || shareForm == null || shareClosing) return;
        shareContent.BodyHeightLimit = shareTab.Value == 2
            ? Math.Max(PlaylistDialogContent.MaximumBodyHeight, Window.ClientSize.Height - 140)
            : PlaylistDialogContent.MaximumBodyHeight;
        if (shareTab.Value == 2) FitShareWidgetPreview();
        var width = shareContent.BodyBounds.Width > 0 ? shareContent.BodyBounds.Width : PlaylistDialogContent.PreferredWidth;
        var selected = sharePages.PageContent(shareTab.Value);
        selected.Measure(new(Math.Max(0, width - shareForm.Margin.Left - shareForm.Margin.Right), double.PositiveInfinity));
        sharePages.SetPageHeight(shareTab.Value, selected.DesiredSize.Height);
        shareForm.Measure(new(width, double.PositiveInfinity));
        // Opening already animates the whole dialog. Settle its initial size
        // before that capture; only tab/content changes resize an open dialog.
        var height = shareForm.DesiredSize.Height;
        // Keep painting the outgoing row in its full page viewport while the card
        // moves toward the already-collapsed layout. Detaching it then costs no height.
        if (selected == shareLinksPanel && !shareActionsShown && shareDestinationsPanel.IsVisible)
            height -= shareDestinationsPanel.DesiredSize.Height + shareLinksPanel.Spacing;
        shareContent.FitCreation(height, animate: shareDialog?.IsOpen == true);
    }
}
