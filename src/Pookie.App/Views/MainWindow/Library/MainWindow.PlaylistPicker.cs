using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> playlistPickerOpen = new(false);
    private StackPanel playlistChoices = null!;
    private TextBlock playlistPickerTrack = null!, playlistPickerMessage = null!;
    private LibraryLoadingView playlistPickerLoading = null!;
    private SoundCloudTrack? playlistPickerTarget;
    private CancellationTokenSource? playlistPickerRequest;
    private long playlistPickerGeneration;
    private readonly HashSet<(WebSession Session, long Playlist, long Track)> pendingPlaylistAdds = [];

    private Button AddToPlaylistButton(Func<SoundCloudTrack?> track, string style = TrackButtons.Tonal, double size = 40) =>
        TrackButtons.Icon(Icons.View("playlist-plus", 20, TrackButtons.OnSurface),
            () => { if (track() is { } item) Run(() => OpenPlaylistPickerAsync(item)); }, style, size).ToolTip("Добавить в плейлист");

    private FrameworkElement PlaylistPickerView()
    {
        playlistChoices = new StackPanel().Vertical().Spacing(8);
        playlistPickerTrack = new TextBlock().FontSize(13).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
        playlistPickerMessage = new TextBlock().FontSize(14).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        var scroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(playlistChoices);
        playlistPickerLoading = CreateLibraryLoadingView(scroll);
        playlistPickerLoading.Skeleton.CompactList = true;
        var dismiss = new Button().Background(Color.FromArgb(150, 0, 0, 0)).BorderThickness(0).Padding(0)
            .OnClick(ClosePlaylistPicker);
        var card = new Border().Background(Raised).CornerRadius(24).Padding(24).Width(480).MaxHeight(560).Center()
            .Child(new DockPanel().LastChildFill().Spacing(18).Children(
                new Grid().Columns("*,40").Rows("Auto").Spacing(16).DockTop().Children(
                    new StackPanel().Vertical().Spacing(8).Column(0).Children(
                        new TextBlock().Text("Добавить в плейлист").FontSize(22).Bold(), playlistPickerTrack),
                    TrackButtons.Icon(Icons.View("x", 20), ClosePlaylistPicker, TrackButtons.Text).Column(1).Top()),
                playlistPickerMessage.DockTop(), playlistPickerLoading.Root.Height(360)));
        return new Grid().Columns("*").Rows("*").BindIsVisible(playlistPickerOpen).Children(dismiss, card);
    }

    private void ClosePlaylistPicker()
    {
        playlistPickerOpen.Value = false; ++playlistPickerGeneration;
        playlistPickerRequest?.Cancel(); playlistPickerRequest?.Dispose(); playlistPickerRequest = null;
        playlistPickerLoading.SetLoading(false); playlistPickerTarget = null;
    }

    private async Task OpenPlaylistPickerAsync(SoundCloudTrack track)
    {
        ClosePlaylistPicker();
        var generation = playlistPickerGeneration;
        playlistPickerTarget = track; playlistPickerOpen.Value = true;
        playlistPickerTrack.Text = track.Title + " · " + track.Author;
        playlistChoices.Clear(); playlistPickerMessage.Text = ""; playlistPickerLoading.Root.Height = 360;
        if (demo || me is not { } user || api.Session is not { } session)
        { playlistPickerMessage.Text = "Войди в SoundCloud, чтобы добавить трек в свой плейлист."; return; }
        playlistPickerRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = playlistPickerRequest.Token;
        playlistPickerLoading.SetLoading(true);
        try
        {
            var playlists = await api.GetOwnedPlaylistsAsync(user.Id, token);
            if (disposed || generation != playlistPickerGeneration || api.Session != session) return;
            playlistPickerLoading.Root.Height = Math.Clamp(playlists.Length * 72 - 8, 80, 360);
            playlistPickerMessage.Text = playlists.Length == 0 ? "У тебя пока нет плейлистов. Создай первый в SoundCloud." : "Выбери, куда добавить трек";
            foreach (var item in playlists)
            {
                var playlist = item.Playlist!;
                var cover = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
                SetCollectionArtwork(cover, item);
                var added = new TextBlock().FontSize(12).Foreground(TrackButtons.OnReposted).CenterVertical();
                Button button = null!;
                button = TrackButtons.Action(new Grid().Columns("44,*,Auto").Rows("Auto").Spacing(12).Children(
                    new Border().CornerRadius(6).ClipToBounds().Child(ArtworkLayer(cover)).Column(0),
                    new StackPanel().Vertical().Spacing(4).Column(1).CenterVertical().Children(
                        new TextBlock().Text(item.Title).FontSize(14).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        new TextBlock().Text(playlist.TrackCount + " треков" + (playlist.Sharing == "private" ? " · Приватный" : "")).FontSize(12).Foreground(Muted)), added.Column(2)),
                    () => Run(() => AddPickerTrackAsync(user.Id, playlist.Id, track, session, generation, button, added)), TrackButtons.Text, 64);
                ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
                playlistChoices.Add(button);
                Run(async () =>
                {
                    var source = await GetCollectionArtworkAsync(item);
                    if (!disposed && generation == playlistPickerGeneration) SetCollectionArtwork(cover, item, source, finished: true);
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        { if (!disposed && generation == playlistPickerGeneration) playlistPickerMessage.Text = FriendlyError(error); }
        finally { if (!disposed && generation == playlistPickerGeneration) playlistPickerLoading.SetLoading(false); }
    }

    private async Task AddPickerTrackAsync(long userId, long playlistId, SoundCloudTrack track, WebSession session, long generation, Button button, TextBlock added)
    {
        if (api.Session != session || !pendingPlaylistAdds.Add((session, playlistId, track.Id))) return;
        TrackButtons.SetAvailability(button, false, true);
        var succeeded = false;
        try
        {
            await api.AddToPlaylistAsync(userId, playlistId, track.Id, lifetime.Token);
            if (api.Session != session || disposed) return;
            succeeded = true;
            libraryPages.Remove("collections"); libraryLoads.Remove("collections"); libraryLoadedAt.Remove("collections");
            overviewReady.Remove("collections");
            foreach (var key in collectionDetails.Where(pair => pair.Value.Item.Playlist?.Id == playlistId).Select(pair => pair.Key).ToArray())
                collectionDetails.Remove(key);
            if (generation == playlistPickerGeneration)
            { added.Text = "Добавлено"; TrackButtons.SetReposted(button, true, TrackButtons.Text); }
            status.Value = "Трек добавлен в плейлист.";
        }
        catch (Exception error)
        { if (!disposed && generation == playlistPickerGeneration) playlistPickerMessage.Text = FriendlyError(error); }
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
