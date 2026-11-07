using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> playlistPickerOpen = new(false);
    private StackPanel playlistChoices = null!;
    private TextBlock playlistPickerTrack = null!, playlistPickerMessage = null!;
    private TextBlock playlistPickerArtist = null!;
    private Image playlistPickerCover = null!;
    private PaletteGlassCard playlistPickerPreview = null!;
    private PlaylistDialogContent playlistDialogContent = null!;
    private Button playlistPickerClose = null!;
    private Button playlistPickerCreate = null!;
    private PlaylistDialogPages playlistDialogPages = null!;
    private LibraryLoadingView playlistPickerLoading = null!;
    private ModalDialog playlistDialog = null!;
    private bool playlistPickerClosing;
    private SoundCloudTrack? playlistPickerTarget;
    private CancellationTokenSource? playlistPickerRequest;
    private long playlistPickerGeneration;
    private readonly HashSet<(WebSession Session, long Playlist, long Track)> pendingPlaylistAdds = [];
    private readonly HashSet<long> playlistPickerIds = [];

    private Button AddToPlaylistButton(Func<SoundCloudTrack?> track, string style = TrackButtons.Tonal, double size = 40) =>
        TrackButtons.Icon(Icons.View("playlist-plus", 20, TrackButtons.OnSurface),
            () => { if (track() is { } item) Run(() => OpenPlaylistPickerAsync(item)); }, style, size).Hint("Добавить в плейлист");

    private FrameworkElement PlaylistPickerView()
    {
        playlistChoices = new StackPanel().Vertical().Spacing(8);
        playlistPickerTrack = new TextBlock().FontSize(15).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        playlistPickerArtist = new TextBlock().FontSize(13).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        playlistPickerCover = new Image().Width(56).Height(56).StretchMode(Stretch.UniformToFill);
        playlistPickerPreview = TrackDialogChrome.Preview(ArtworkLayer(playlistPickerCover), playlistPickerTrack, playlistPickerArtist);
        playlistPickerMessage = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(40).Margin(24, 0, 24, 0).IsVisible(false);
        var scroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(playlistChoices);
        playlistPickerLoading = CreateLibraryLoadingView(scroll);
        playlistPickerLoading.Skeleton.PlaylistRows = true;
        playlistPickerLoading.Root.ClipToBounds = true;
        playlistPickerLoading.Root.Margin = new Thickness(16, 0, 8, 20);
        playlistPickerClose = TrackButtons.Icon(Icons.View("x", 20), ClosePlaylistPicker, TrackButtons.Glass).Top();
        ((MaterialTrackButton)playlistPickerClose).UseLightGlassFinish();
        playlistPickerCreate = TrackButtons.Icon(Icons.View("plus", 20), () => ShowPlaylistCreation(true), TrackButtons.Glass);
        ((MaterialTrackButton)playlistPickerCreate).UseLightGlassFinish();
        var list = new DockPanel().LastChildFill().Spacing(12).Children(
            new Grid().Columns("*,40").Rows("40").Spacing(16).Margin(24, 24, 24, 0).DockTop().Children(
                TrackDialogChrome.Heading("Добавить в плейлист").Column(0),
                playlistPickerCreate.Column(1)),
            playlistPickerMessage.DockTop(), playlistPickerLoading.Root);
        playlistDialogPages = new PlaylistDialogPages(list, PlaylistCreationView());
        var card = new Border().Background(PopupAppearance.Surface).BorderBrush(Color.Transparent)
            .BorderThickness(0).CornerRadius(24).Padding(0).ClipToBounds()
            .MaxWidth(PlaylistDialogContent.MaximumWidth).MaxHeight(PlaylistDialogContent.MaximumBodyHeight)
            .Child(playlistDialogPages);
        playlistDialogContent = new PlaylistDialogContent(card, playlistPickerPreview);
        playlistDialog = new ModalDialog(playlistDialogContent, playlistPickerClose, playlistPickerClose)
        {
            DismissRequested = ClosePlaylistPicker,
            Presented = playlistDialogContent.RevealTrack,
            Closed = () =>
            {
                playlistPickerClosing = false;
                playlistDialogContent.FreezeSize();
                playlistDialogContent.ResetTrack(); playlistPickerPreview.ResetPalette();
                StopCardArtwork(playlistPickerCover); playlistPickerCover.Source = null;
            }
        };
        ((MaterialTrackButton)playlistPickerClose).UseOverlayBackdrop(playlistDialog, playlistDialog.RenderGlassBackdrop);
        return playlistDialog;
    }

    private void ClosePlaylistPicker()
    {
        if (playlistPickerClosing && !disposed) return;
        playlistPickerOpen.Value = false;
        playlistDialogContent.FreezeSize();
        ResetPlaylistPickerRequest();
        if (disposed) { playlistDialog?.Close(immediate: true); return; }
        if (playlistDialog?.IsOpen != true) return;
        playlistPickerClosing = true;
        playlistDialogContent.IsHitTestVisible = false;
        playlistDialogContent.HideTrack(() =>
        {
            playlistPickerClosing = false;
            playlistDialog.Close();
        });
    }

    private void SetPlaylistPickerMessage(string text)
    { playlistPickerMessage.Text = text; playlistPickerMessage.IsVisible = text.Length > 0; }

    private void ResetPlaylistPickerRequest()
    {
        ++playlistPickerGeneration;
        playlistPickerRequest?.Cancel(); playlistPickerRequest?.Dispose(); playlistPickerRequest = null;
        playlistPickerLoading.SetLoading(false); playlistPickerTarget = null;
    }

    private async Task OpenPlaylistPickerAsync(SoundCloudTrack track)
    {
        ResetPlaylistPickerRequest();
        var generation = playlistPickerGeneration;
        var alreadyOpen = playlistDialog.IsOpen;
        var reversingTrack = playlistPickerClosing;
        playlistPickerClosing = false; playlistDialogContent.IsHitTestVisible = true;
        if (!reversingTrack) playlistDialogContent.ResetTrack();
        if (!reversingTrack) playlistPickerPreview.ResetPalette();
        playlistDialogContent.SetLoadingSize(playlistDialog.IsVisible);
        playlistDialogPages.ListHeight = PlaylistDialogContent.PreferredBodyHeight;
        ResetPlaylistCreation();
        playlistPickerTarget = track; playlistPickerOpen.Value = true;
        playlistPickerTrack.Text = track.Title; playlistPickerArtist.Text = track.Author;
        playlistChoices.Clear(); playlistPickerIds.Clear(); SetPlaylistPickerMessage("");
        var artwork = track.ArtworkUrl ?? track.User?.AvatarUrl;
        SetCardArtwork(playlistPickerCover, null, artwork);
        playlistPickerRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = playlistPickerRequest.Token;
        playlistDialog.Show();
        if (alreadyOpen) playlistDialogContent.RevealTrack();
        Run(() => LoadPlaylistPickerArtworkAsync(artwork, generation, token));
        if (demo || me is not { } user || api.Session is not { } session)
        { SetPlaylistPickerMessage("Войди в SoundCloud, чтобы добавить трек в свой плейлист."); FitPlaylistList(); return; }
        playlistPickerLoading.SetLoading(true);
        try
        {
            var playlists = await api.GetPlaylistChoicesForTrackAsync(user.Id, track.Id, token);
            if (disposed || generation != playlistPickerGeneration || api.Session != session) return;
            SetPlaylistPickerMessage(playlists.Length == 0 ? "Пока нет плейлистов. Создай первый кнопкой +." : "");
            foreach (var item in playlists)
                if (playlistPickerIds.Add(item.Playlist!.Id)) playlistChoices.Add(PlaylistChoice(item, user.Id, track, session, generation));
            FitPlaylistList();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        { if (!disposed && generation == playlistPickerGeneration) { SetPlaylistPickerMessage(FriendlyError(error)); FitPlaylistList(); } }
        finally { if (!disposed && generation == playlistPickerGeneration) playlistPickerLoading.SetLoading(false); }
    }

    private void FitPlaylistList()
    {
        playlistDialogPages.ListHeight = PlaylistDialogContent.PlaylistHeight(playlistChoices.Count, playlistPickerMessage.IsVisible);
        playlistDialogPages.InvalidateMeasure();
        if (!playlistDialogPages.Creating) playlistDialogContent.FitPlaylists(playlistChoices.Count, playlistPickerMessage.IsVisible);
    }

    private Button PlaylistChoice(LibraryItem item, long userId, SoundCloudTrack track, WebSession session, long generation, bool confirmed = false)
    {
        var playlist = item.Playlist!;
        var cover = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
        SetCollectionArtwork(cover, item);
        Button button = null!;
        var title = new DockPanel().LastChildFill().Spacing(6).Children(
            Icons.View("lock-simple", 14, Muted).DockRight().CenterVertical().IsVisible(playlist.Sharing == "private"),
            new TextBlock().Text(item.Title).FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis));
        title.HorizontalAlignment = HorizontalAlignment.Left;
        button = TrackButtons.Action(new Grid().Columns("44,*").Rows("Auto").Spacing(12).Children(
            new Border().CornerRadius(6).ClipToBounds().Child(ArtworkLayer(cover)).Column(0),
            new StackPanel().Vertical().Spacing(4).Column(1).CenterVertical().Children(title,
                new TextBlock().Text(playlist.TrackCount + " треков").FontSize(12).Foreground(Muted))),
            () => Run(() => AddPickerTrackAsync(userId, playlist.Id, track, session, generation, button)), TrackButtons.Text, 64);
        button.CornerRadius = 10; button.Padding = new Thickness(8, 0);
        ((MaterialTrackButton)button).UseListStateLayer();
        ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
        if (confirmed || playlist.Tracks.Any(item => item.Id == track.Id)) TrackButtons.SetAvailability(button, false, false);
        Run(async () =>
        {
            var source = await GetCollectionArtworkAsync(item);
            if (!disposed && generation == playlistPickerGeneration) SetCollectionArtwork(cover, item, source, finished: true);
        });
        return button;
    }

    private async Task LoadPlaylistPickerArtworkAsync(string? url, long generation, CancellationToken token)
    {
        var image = await FetchImageAsync(url, token, maximumSide: 256);
        if (disposed || generation != playlistPickerGeneration || !playlistDialog.IsOpen) return;
        SetCardArtwork(playlistPickerCover, image, url, finished: true);
        if (image == null) return;
        var palette = await playerPalettes.GetValue(image, source => Task.Run(() => PlayerPalette.FromArtwork(source), lifetime.Token));
        if (!disposed && generation == playlistPickerGeneration && playlistDialog.IsOpen) playlistPickerPreview.SetPalette(palette);
    }

    private async Task AddPickerTrackAsync(long userId, long playlistId, SoundCloudTrack track, WebSession session, long generation, Button button)
    {
        if (api.Session != session || !pendingPlaylistAdds.Add((session, playlistId, track.Id))) return;
        TrackButtons.SetAvailability(button, false, true);
        var succeeded = false;
        try
        {
            await api.AddToPlaylistAsync(userId, playlistId, track.Id, lifetime.Token);
            if (api.Session != session || disposed) return;
            succeeded = true;
            InvalidatePlaylistLibrary(playlistId);
            status.Value = "Трек добавлен в плейлист.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch
        {
            if (!disposed && generation == playlistPickerGeneration && api.Session == session)
                status.Value = "Не удалось добавить трек в плейлист. Попробуй ещё раз.";
        }
        finally
        {
            pendingPlaylistAdds.Remove((session, playlistId, track.Id));
            if (!disposed && generation == playlistPickerGeneration)
            {
                TrackButtons.SetAvailability(button, !succeeded, false);
                if (succeeded) button.Opacity = 1;
            }
        }
    }
}
