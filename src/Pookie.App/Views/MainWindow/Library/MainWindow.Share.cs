using System.Globalization;
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
    private TextBlock shareTitle = null!, shareAuthor = null!, shareMessage = null!, shareCopyLabel = null!;
    private TextBox shareLink = null!, shareTime = null!;
    private CheckBox shareAt = null!;
    private FrameworkElement shareTimeRow = null!;
    private Button shareCopy = null!;
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
        sharePreview = new PaletteGlassCard { HoverEnabled = false }.CornerRadius(20).Padding(14).Height(84);
        sharePreview.Child(new Grid().Columns("56,*").Rows("*").Spacing(14).Children(
            new Border().CornerRadius(10).ClipToBounds().Child(ArtworkLayer(shareCover)),
            new StackPanel().Vertical().Spacing(5).CenterVertical().Column(1).Children(shareTitle, shareAuthor)));
        shareLink = new TextBox { IsReadOnly = true }.FontSize(13).Background(Color.Transparent).BorderThickness(0).Padding(0).Height(26);
        shareCopyLabel = new TextBlock().Text("Скопировать").FontSize(13).SemiBold();
        shareCopy = TrackButtons.Action(shareCopyLabel, CopyShareLink, TrackButtons.Glass, 44);
        ((MaterialTrackButton)shareCopy).UseLightGlassFinish();
        shareAt = new CheckBox().Content("Начать с").FontSize(13).CenterVertical();
        shareTime = new TextBox().Text("0:00").MaxLength(12).FontSize(13).Width(92).Height(36).Padding(10, 6);
        shareTime.IsEnabled = false;
        shareTime.OnTextChanged(_ => RefreshShareLink());
        shareAt.CheckedChanged += value => { shareTime.IsEnabled = value == true; RefreshShareLink(); };
        shareTimeRow = new StackPanel().Horizontal().Spacing(12).Children(shareAt, shareTime);
        shareMessage = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Height(36);
        var destinations = new Grid().Columns("*,*,*").Rows("44").Spacing(8);
        foreach (var (label, service) in new[] { ("Telegram", "telegram"), ("ВКонтакте", "vk"), ("X", "x") })
        {
            var button = TrackButtons.Action(new TextBlock().Text(label).FontSize(13).SemiBold(), () => OpenShareDestination(service), TrackButtons.Tonal, 44);
            destinations.Add(button.Column(shareDestinations.Count)); shareDestinations.Add(button);
        }
        var open = TrackButtons.Action(new TextBlock().Text("Открыть в SoundCloud").FontSize(13),
            () => OpenSoundCloudPage(shareLink.Text), TrackButtons.Text, 36);
        shareDestinations.Add(open);
        var form = new StackPanel().Vertical().Spacing(16).Margin(24).Children(
            new TextBlock().Text("Поделиться").FontSize(21).Bold(), destinations,
            new Grid().Columns("*,Auto").Rows("44").Spacing(10).Children(
                new GlassCard { HoverEnabled = false }.CornerRadius(14).Padding(14, 9).Child(shareLink), shareCopy.Column(1)),
            shareTimeRow, shareMessage, open);
        var card = new Border().Background(PopupAppearance.Surface).BorderThickness(0).CornerRadius(24).ClipToBounds()
            .MaxWidth(PlaylistDialogContent.MaximumWidth).Child(form);
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
        shareTimeRow.IsVisible = item.Track != null;
        shareAt.IsChecked = false;
        shareTime.Text = FormatTime(item.Track?.Id == current?.Id ? progress.Value : 0);
        shareTime.IsEnabled = false;
        RefreshShareLink();
        SetCardArtwork(shareCover, null, item.ArtworkUrl);
        shareRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = shareRequest.Token;
        shareContent.FitCreation(item.Track != null ? 360 : 308);
        shareDialog.Show();
        if (reopening) shareContent.RevealTrack();
        Run(() => LoadShareArtworkAsync(item, generation, token));
        if (shareUri == null && item.Track is { Id: > 0 } track) Run(() => ResolveShareTrackAsync(track, generation, token));
    }

    private void ResetShareRequest()
    {
        ++shareGeneration; shareRequest?.Cancel(); shareRequest?.Dispose(); shareRequest = null;
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
            shareUri = TrackLink(resolved.PermalinkUrl); shareTarget = LibraryItem.FromTrack(resolved); RefreshShareLink();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error)
        { if (!disposed && generation == shareGeneration) shareMessage.Text = FriendlyError(error); }
    }

    private async Task LoadShareArtworkAsync(LibraryItem item, long generation, CancellationToken token)
    {
        var image = await FetchImageAsync(item.ArtworkUrl, token, maximumSide: 256);
        if (disposed || generation != shareGeneration || !shareDialog.IsOpen) return;
        SetCardArtwork(shareCover, image, item.ArtworkUrl, finished: true);
        if (image == null) return;
        var palette = await playerPalettes.GetValue(image, source => Task.Run(() => PlayerPalette.FromArtwork(source), lifetime.Token));
        if (!disposed && generation == shareGeneration && shareDialog.IsOpen) sharePreview.SetPalette(palette);
    }

    private void RefreshShareLink()
    {
        var seconds = 0;
        var validTime = shareTarget?.Track is not { } track || shareAt.IsChecked != true ||
            TryShareTime(shareTime.Text, track.DurationSeconds, out seconds);
        var link = shareUri == null || !validTime ? "" : new UriBuilder(shareUri) { Fragment = shareAt.IsChecked == true && shareTarget?.Track != null ? "t=" + FormatTime(seconds) : "" }.Uri.AbsoluteUri;
        shareLink.Text = link; shareCopyLabel.Text = "Скопировать";
        shareCopy.IsEnabled = link.Length > 0;
        foreach (var button in shareDestinations) button.IsEnabled = shareCopy.IsEnabled;
        shareMessage.Text = !validTime ? "Укажи время в формате 0:00 в пределах длительности трека." :
            shareUri == null ? "У этого трека или плейлиста пока нет ссылки для отправки." : "";
    }

    private static bool TryShareTime(string text, double duration, out int seconds)
    {
        seconds = 0; var parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3) return false;
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 ||
                (index > 0 && value > 59) || seconds > (int.MaxValue - value) / 60) return false;
            seconds = seconds * 60 + value;
        }
        return duration > 0 && seconds <= duration;
    }

    private void CopyShareLink()
    {
        if (!shareCopy.IsEnabled || shareLink.Text.Length == 0) return;
        var clipboard = shareLink.ClipboardService ?? Application.Current.PlatformServices.Clipboard;
        var copied = clipboard?.TrySetText(shareLink.Text) == true;
        status.Value = copied ? "Скопировано." : "Не удалось скопировать.";
        shareCopyLabel.Text = copied ? "Скопировано" : "Повторить";
    }

    private void OpenShareDestination(string service)
    {
        if (!shareCopy.IsEnabled || shareTarget == null) return;
        var link = Uri.EscapeDataString(shareLink.Text); var text = Uri.EscapeDataString(shareTarget.Title);
        var url = service switch
        {
            "telegram" => $"https://t.me/share/url?url={link}&text={text}",
            "vk" => $"https://vk.com/share.php?url={link}&title={text}",
            "x" => $"https://twitter.com/intent/tweet?url={link}&text={text}",
            _ => null
        };
        if (url != null) OpenExternalUrl(url, () => shareMessage.Text = "Не удалось открыть браузер.");
    }
}
