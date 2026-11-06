using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlaylistDialogAsync(SoundCloudTrack track, CollectionFixtureTransport transport)
    {
        Window.Activate(); MoveActionPointer(new(2, 2));
        await VerifyPlaylistMoreFocusAsync();
        collectionFilter.Focus();
        const string cover = "https://i1.sndcdn.com/playlist-dialog-cover.jpg";
        await imageDiskCache.WriteAsync(cover, ArtworkFixtureBitmap(128), lifetime.Token);
        track = track with { ArtworkUrl = cover };
        var opening = new HashSet<double>(); var closing = new HashSet<double>(); var heights = new HashSet<double>();
        void Sample()
        {
            if (playlistDialog.Reveal is > 0 and < 1)
                (playlistDialog.IsOpen ? opening : closing).Add(playlistDialog.Reveal);
            if (playlistDialogContent.Resizing) heights.Add(playlistDialogContent.BodyBounds.Height);
        }
        Window.FrameRendered += Sample;
        try
        {
            transport.PickerHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = OpenPlaylistPickerAsync(track);
            if (!playlistDialog.IsFocusWithin) throw new InvalidOperationException("Dialog did not receive focus immediately.");
            await WaitForLikedLayoutAsync(() => playlistDialog.Reveal is > .15 and < .85 && playlistDialog.HasSnapshots);
            if (playlistDialogContent.TrackReveal != 0) throw new InvalidOperationException("The track preview appeared before its dialog.");
            CaptureUiPreview("dialog-opening");
            await WaitForLikedLayoutAsync(() => !playlistDialog.Running && playlistDialog.Reveal == 1);
            var loadingBounds = playlistDialogContent.BodyBounds;
            CaptureUiPreview("dialog-loading-rows");
            if (!playlistPickerLoading.Root.ClipToBounds || playlistPickerLoading.Skeleton.Bounds.Right > loadingBounds.Right ||
                playlistPickerLoading.Skeleton.Bounds.Bottom > loadingBounds.Bottom)
                throw new InvalidOperationException("Playlist skeleton escaped the dialog list viewport.");
            await WaitForLikedLayoutAsync(() => playlistDialogContent.TrackReveal == 1 && !playlistDialogContent.Animating);
            if (playlistPickerClose.StyleName != TrackButtons.Glass || playlistDialogContent.TrackBounds.Bottom >= loadingBounds.Y)
                throw new InvalidOperationException("The track preview did not emerge above the dialog or its close button lost the glass style.");
            transport.PickerHold.SetResult(); await loading;
            await WaitForLikedLayoutAsync(() => !playlistPickerLoading.Skeleton.IsVisible && !playlistDialogContent.Resizing && playlistPickerPreview.Palette != PlayerPalette.Neutral);
            var compactHeight = playlistDialogContent.BodyBounds.Height;
            if (compactHeight >= loadingBounds.Height || heights.Count < 3 || playlistPickerTrack.Text != track.Title || playlistPickerArtist.Text != track.Author)
                throw new InvalidOperationException("The loaded playlist dialog did not shrink smoothly or lost the track preview.");
            if (playlistDialogContent.Children.OfType<Border>().Single().BorderThickness != 0)
                throw new InvalidOperationException("The main dialog retained its outline.");
            if (playlistPickerMessage.IsVisible || playlistPickerClose.Background.R != 255 || playlistPickerClose.Background.A >= 100)
                throw new InvalidOperationException("The normal picker retained its subtitle or a dark close-button tint.");
            var privateRow = playlistChoices.Children.OfType<Button>().Last();
            Image? privacy = null;
            VisualTree.Visit(privateRow, element =>
            {
                if (element is TextBlock text && text.Text.Contains("Приватный")) throw new InvalidOperationException("Playlist privacy remained a text label.");
                if (element is Image image && ReferenceEquals(image.Source, Icons.Source("lock-simple", Muted))) privacy = image;
            });
            if (privacy?.IsVisible != true || privateRow.CornerRadius != 10 || playlistDialogContent.BodyBounds.Right - playlistPickerLoading.Root.Bounds.Right > 10)
                throw new InvalidOperationException("Playlist row lost its lock, compact highlight shape or edge-aligned scrollbar.");
            MoveActionPointer(SettingsCenter(privateRow));
            await WaitForLikedLayoutAsync(() => privateRow.Background == Color.FromArgb(22, 255, 255, 255));
            CaptureUiPreview("dialog-playlist-hover");
            MoveActionPointer(new(2, 2));
            VerifyDialogScrim();
            if (opening.Count < 3 || playlistDialog.Pose != 1 || playlistDialog.BackdropReveal != 1)
                throw new InvalidOperationException("Dialog skipped its progressive backdrop/content entrance.");
            var close = Window.FocusManager.FocusedElement;
            var tab = new KeyEventArgs(Key.Tab, 0);
            if (!playlistDialog.HandleKey(tab) || !tab.Handled || !playlistDialog.IsFocusWithin || Window.FocusManager.FocusedElement == close)
                throw new InvalidOperationException("Tab did not move within the dialog.");
            for (var index = 0; index < 6; index++) playlistDialog.HandleKey(new(Key.Tab, 0));
            if (!playlistDialog.IsFocusWithin) throw new InvalidOperationException("Tab escaped the modal focus trap.");
            CaptureUiPreview("dialog-open");
            await VerifyPlaylistCreationAsync(track, transport);
            await VerifyPlaylistDialogPolishAsync(track, transport);
            foreach (var count in new[] { 0, 30, 2 })
            {
                transport.PickerCount = count; await OpenPlaylistPickerAsync(track);
                await WaitForLikedLayoutAsync(() => playlistChoices.Children.Count == count && !playlistPickerLoading.Skeleton.IsVisible && !playlistDialogContent.Resizing);
                var expected = count == 0 ? 168 : count == 2 ? compactHeight : PlaylistDialogContent.MaximumBodyHeight;
                if (Math.Abs(playlistDialogContent.BodyBounds.Height - expected) > .5)
                    throw new InvalidOperationException("Playlist count did not determine the dialog height.");
                CaptureUiPreview("dialog-count-" + count);
            }
            transport.PickerFails = true; await OpenPlaylistPickerAsync(track); transport.PickerFails = false;
            await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing);
            if (Math.Abs(playlistDialogContent.BodyBounds.Height - 168) > .5) throw new InvalidOperationException("The error state did not fit its message.");
            await OpenPlaylistPickerAsync(track);
            await WaitForLikedLayoutAsync(() => playlistDialogContent.TrackReveal == 1 && !playlistDialogContent.Animating);
            RouteChatEscape();
            if (!playlistDialog.IsOpen || playlistDialog.Reveal != 1 || !playlistPickerClosing)
                throw new InvalidOperationException("The dialog started closing before its track preview hid.");
            await WaitForLikedLayoutAsync(() => playlistDialogContent.TrackReveal is > .15 and < .85);
            CaptureUiPreview("dialog-track-hiding");
            var trackAmount = playlistDialogContent.TrackReveal;
            await OpenPlaylistPickerAsync(track);
            if (Math.Abs(playlistDialogContent.TrackReveal - trackAmount) > .001 || playlistDialog.Reveal != 1)
                throw new InvalidOperationException("Reopening interrupted the staged track animation discontinuously.");
            await WaitForLikedLayoutAsync(() => playlistDialogContent.TrackReveal == 1 && !playlistDialogContent.Animating);
            ClosePlaylistPicker();
            await WaitForLikedLayoutAsync(() => !playlistDialog.IsOpen);
            if (playlistDialogContent.TrackReveal != 0)
                throw new InvalidOperationException("The modal closed while its track preview was still visible.");
            CaptureUiPreview("dialog-close-start");
            if (playlistDialog.IsOpen || !playlistDialog.IsVisible || playlistDialog.HitTest(new(10, 100)) != null)
                throw new InvalidOperationException("Closing hid the dialog before exit or kept blocking pointer input.");
            await WaitForLikedLayoutAsync(() => playlistDialog.Reveal is > .05 and < .9);
            CaptureUiPreview("dialog-closing");
            var amount = playlistDialog.Reveal; var pose = playlistDialog.Pose;
            await OpenPlaylistPickerAsync(track);
            if (Math.Abs(playlistDialog.Reveal - amount) > .001 || Math.Abs(playlistDialog.Pose - pose) > .001)
                throw new InvalidOperationException("Reopening during exit reset the dialog's presentation.");
            await WaitForLikedLayoutAsync(() => !playlistDialog.Running);
            ClosePlaylistPicker();
            await WaitForLikedLayoutAsync(() => playlistDialog.Reveal == 0 && playlistDialog.BackdropReveal > 0);
            if (!playlistDialog.IsVisible) throw new InvalidOperationException("Overlay left before its slower fade completed.");
            await WaitForLikedLayoutAsync(() => !playlistDialog.IsVisible);
            if (closing.Count < 3 || playlistDialog.Running || playlistDialog.HasSnapshots || Window.FocusManager.FocusedElement != collectionFilter)
                throw new InvalidOperationException($"Dialog exit retained rendering resources or lost its trigger focus: frames={closing.Count}, running={playlistDialog.Running}, snapshots={playlistDialog.HasSnapshots}, active={Window.IsActive}, expectedFocus={Window.FocusManager.FocusedElement == collectionFilter}.");

            await OpenPlaylistPickerAsync(track);
            await WaitForLikedLayoutAsync(() => !playlistDialog.Running);
            RouteWaveformClick(new(10, 100));
            await WaitForLikedLayoutAsync(() => !playlistDialog.IsOpen);
            if (playlistDialog.IsOpen) throw new InvalidOperationException("Clicking outside did not dismiss the dialog.");
            collectionRepost.Focus();
            await WaitForLikedLayoutAsync(() => !playlistDialog.IsVisible);
            if (Window.FocusManager.FocusedElement != collectionRepost)
                throw new InvalidOperationException("Dialog exit stole focus after the user moved elsewhere.");
            await OpenPlaylistPickerAsync(track);
            ClosePlaylistPicker(); playlistDialog.Close(true);
            if (playlistDialog.Running || playlistDialog.HasSnapshots || playlistDialog.IsVisible)
                throw new InvalidOperationException("Immediate dialog teardown retained clocks or surfaces.");
            Console.WriteLine("MODAL_DIALOG_UI_OK: shared video scrim without background capture/blur, perspective entrance, quicker content exit, retained overlay, continuous reversal, immediate focus, Tab trap, Escape/outside dismissal, click-through exit, conditional focus restoration and snapshot/clock cleanup");
        }
        finally { Window.FrameRendered -= Sample; ClosePlaylistPicker(); playlistDialog.Close(true); }
    }

    private async Task VerifyPlaylistCreationAsync(SoundCloudTrack track, CollectionFixtureTransport transport)
    {
        var bounds = playlistDialog.Bounds;
        var expectedClose = LayoutRounding.SnapRectEdgesToPixels(new Rect(bounds.Right - 64, bounds.Y + 12, 40, 40), Window.DpiScale);
        if (playlistPickerClose.Bounds != expectedClose ||
            playlistDialogContent.Bounds.Contains(playlistPickerClose.Bounds.Center) || chatVideoClose.StyleName != TrackButtons.Glass)
            throw new InvalidOperationException($"Dialog/media close button is not glass or aligned to the overlay corner: actual={playlistPickerClose.Bounds}, expected={expectedClose}, overlaps={playlistDialogContent.Bounds.Contains(playlistPickerClose.Bounds.Center)}, mediaStyle={chatVideoClose.StyleName}.");
        RouteWaveformClick(SettingsCenter(playlistPickerCreate));
        if (!playlistCreateName.IsFocused || !playlistDialogPages.Creating || playlistCreateSave.IsEnabled)
            throw new InvalidOperationException("Creation did not focus its empty title or allowed a blank write.");
        await WaitForLikedLayoutAsync(() => playlistDialogPages.Animating && playlistDialogContent.Resizing);
        CaptureUiPreview("dialog-create-transition");
        await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating && !playlistDialogContent.Resizing);
        playlistCreateName.Text = "Новый 🎵"; SetPlaylistPrivacy(true);
        if (!playlistCreateSave.IsEnabled) throw new InvalidOperationException("A valid playlist title did not enable creation.");
        CaptureUiPreview("dialog-create-form");
        ShowPlaylistCreation(false);
        await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating);
        ShowPlaylistCreation(true);
        await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating);
        if (playlistCreateName.Text != "Новый 🎵" || !playlistCreateIsPrivate)
            throw new InvalidOperationException("Returning to creation lost the title or privacy.");
        var count = playlistChoices.Count;
        var writes = transport.CreateWrites;
        transport.CreateHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = CreatePickerPlaylistAsync();
        await CreatePickerPlaylistAsync();
        if (transport.CreateWrites != writes + 1 || playlistCreateName.IsEnabled || playlistCreateSave.IsEnabled)
            throw new InvalidOperationException("Playlist creation allowed a duplicate or editable pending request.");
        transport.CreateHold.SetResult(); await saving; transport.CreateHold = null;
        await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating && !playlistDialogContent.Resizing);
        if (playlistDialogPages.Creating || playlistChoices.Count != count + 1 || transport.LastCreate is not { TrackId: > 0, Sharing: "private", Title: "Новый 🎵" } ||
            transport.LastCreate.TrackId != track.Id || playlistChoices.Children.OfType<Button>().Last().IsEnabled)
            throw new InvalidOperationException("Created playlist was not added once as a confirmed seeded row.");
        CaptureUiPreview("dialog-created");
        var created = playlistChoices.Children.OfType<Button>().Last();
        if (created.StyleName != TrackButtons.Text) throw new InvalidOperationException("Created playlist retained a green confirmation surface.");
        VisualTree.Visit(created, child =>
        { if (child is TextBlock { Text: "Добавлено" }) throw new InvalidOperationException("Created playlist retained an added label."); });
        ShowPlaylistCreation(true); playlistCreateName.Text = "Следующий"; transport.CreateFails = true;
        await CreatePickerPlaylistAsync();
        if (!playlistCreateUnconfirmed || playlistCreateSave.IsEnabled || playlistCreateMessage.Text.Length == 0 || transport.CreateWrites != writes + 2)
            throw new InvalidOperationException("An uncertain creation was automatically retried or allowed another POST.");
        await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing);
        if (Math.Abs(playlistDialogContent.BodyBounds.Bottom - playlistCreateSave.Bounds.Bottom - 24) > 1)
            throw new InvalidOperationException("The creation error message left a fixed empty footer.");
        await CreatePickerPlaylistAsync();
        if (transport.CreateWrites != writes + 2) throw new InvalidOperationException("An unconfirmed write was replayed.");
        transport.CreateFails = false;
        await OpenPlaylistPickerAsync(track);
        await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing);
        Console.WriteLine("PLAYLIST_CREATE_UI_OK: overlay glass close, animated form/height, retained draft/privacy, single seeded write, confirmed row and no replay after lost response");
    }

    private async Task VerifyPlaylistMoreFocusAsync()
    {
        var row = compactTrackRows.First(row => row.CollectionRow && row.Track != null && row.Root.ActualWidth > 450 &&
            Window.HitTest(row.Cover.Bounds.Center) is { } hit && row.Root.IsAncestorOf(hit));
        MoveActionPointer(row.Cover.Bounds.Center);
        await WaitForLikedLayoutAsync(() => row.More != null && row.Actions.Strength > .999);
        var origin = row.More!.TranslatePoint(default, Window);
        RouteWaveformClick(new(origin.X + row.More.ActualWidth / 2, origin.Y + row.More.ActualHeight / 2));
        var menu = PopupAppearance.MenuFor(row.Root);
        await WaitForLikedLayoutAsync(() => menu.IsOpen && menu.Reveal == 1);
        var command = menu.Buttons.Single(button =>
        {
            var match = false;
            VisualTree.Visit(button, element => { if (element is TextBlock { Text: "Добавить в плейлист" }) match = true; });
            return match;
        });
        RouteWaveformClick(SettingsCenter(command));
        await WaitForLikedLayoutAsync(() => playlistDialog.IsOpen && !playlistDialog.Running && playlistDialogContent.TrackReveal == 1);
        MoveActionPointer(new(2, 2)); ClosePlaylistPicker();
        await WaitForLikedLayoutAsync(() => !playlistDialog.IsVisible);
        MoveActionPointer(row.Cover.Bounds.Center);
        await WaitForLikedLayoutAsync(() => row.Actions.Strength > .999);
        MoveActionPointer(new(2, 2));
        await WaitForLikedLayoutAsync(() => row.Actions.Strength < .001 && !row.Actions.IsHitTestVisible);
        if (KeyboardFocusFeedback.IsVisible(row.More)) throw new InvalidOperationException("Returning from the playlist dialog invented keyboard focus on More.");
        row.More.Focus(); MoveActionPointer(new(2, 2));
        await WaitForLikedLayoutAsync(() => row.Actions.Strength > .999);
        Window.FocusManager.ClearFocus();
        await WaitForLikedLayoutAsync(() => row.Actions.Strength < .001);
        Console.WriteLine("PLAYLIST_MORE_FOCUS_OK: real More/menu/dialog round trip does not latch hover actions; deliberate keyboard focus still reveals them");
    }

    private void VerifyDialogScrim()
    {
        VerifyChatVideoDirtyClip();
        using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(8, 8, 1));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(Color.FromRgb(180, 140, 100));
        using var viewport = WindowPresentation.CaptureViewport(surface);
        context.Translate(-8, -96); playlistDialog.Render(context); context.EndFrame();
        var pixels = ((ICpuPixelSurface)surface).CopyPixels();
        var factor = (255 - ChatVideoSurface.NormalBackdropAlpha) / 255d;
        for (var channel = 0; channel < 3; channel++)
            if (Math.Abs(pixels[channel] - new[] { 100, 140, 180 }[channel] * factor) > 2)
                throw new InvalidOperationException("Dialog replaced or blurred the page instead of using the video preview's black scrim.");
    }
}
