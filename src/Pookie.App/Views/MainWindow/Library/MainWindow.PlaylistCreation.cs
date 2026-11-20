using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBox playlistCreateName = null!;
    private TextBlock playlistCreateMessage = null!;
    private FrameworkElement playlistCreateForm = null!;
    private PlaylistPrivacySelector playlistCreatePrivacy = null!;
    private PlaylistCreateAction playlistCreateAction = null!;
    private Button playlistCreatePublic = null!, playlistCreatePrivate = null!, playlistCreateSave = null!;
    private bool playlistCreateIsPrivate, playlistCreateUnconfirmed;
    private readonly HashSet<WebSession> pendingPlaylistCreates = [];

    private FrameworkElement PlaylistCreationView()
    {
        playlistCreateName = new TextBox().Placeholder(UiText.Get("playlist.edit.title")).MaxLength(255)
            .Background(Color.Transparent).BorderThickness(0).Padding(0).Height(28).FontSize(14);
        playlistCreateName.OnTextChanged(_ => RefreshPlaylistCreation());
        var name = new GlassCard { HoverEnabled = false }.CornerRadius(14).Padding(16, 10).Height(48).Child(playlistCreateName);
        playlistCreatePrivacy = new PlaylistPrivacySelector(SetPlaylistPrivacy);
        playlistCreatePublic = playlistCreatePrivacy.Public; playlistCreatePrivate = playlistCreatePrivacy.Private;
        playlistCreateSave = TrackButtons.Action(new TextBlock().Text(UiText.Get("playlist.actions.createAndAdd")).FontSize(14).SemiBold(),
            () => Run(CreatePickerPlaylistAsync), TrackButtons.Glass, 44);
        ((MaterialTrackButton)playlistCreateSave).GlassAccent(TrackButtons.BlueGlassAccent, Color.White);
        ((MaterialTrackButton)playlistCreateSave).UseGlassAccentStateLayer();
        playlistCreateAction = new PlaylistCreateAction(playlistCreateSave);
        playlistCreateMessage = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        playlistCreateForm = new StackPanel().Vertical().Margin(24, 0, 24, 24).Children(
            name.Margin(0, 0, 0, 18), playlistCreatePrivacy,
            playlistCreateMessage.Margin(0, 18, 0, 0), playlistCreateAction);
        return playlistCreateForm;
    }
    private void ResetPlaylistCreation()
    {
        playlistDialogPages.Show(false, animate: false);
        playlistPickerSection.Value = false;
        playlistCreateAction.Reveal(false, animate: false);
        playlistCreateUnconfirmed = false;
        playlistCreateName.Text = ""; SetPlaylistCreationMessage("");
        playlistCreateIsPrivate = false;
        RefreshPlaylistCreation();
    }
    private void ShowPlaylistCreation(bool create)
    {
        if (!playlistDialog.IsOpen || playlistPickerClosing || playlistDialogPages.Creating == create) return;
        if (create && playlistCreateName.Text.Length == 0) playlistCreateIsPrivate = playlistPickerTarget?.Sharing == "private";
        playlistDialogPages.Show(create);
        playlistPickerSection.Value = create;
        if (create)
        {
            RefreshPlaylistCreation(); FitPlaylistCreation(); playlistCreateName.Focus();
        }
        else
        {
            if (playlistPickerLoading.Loading) playlistDialogContent.SetLoadingSize(true); else FitPlaylistList();
            playlistPickerFocus.Focus();
        }
    }
    private void SetPlaylistPrivacy(bool isPrivate)
    { playlistCreateIsPrivate = isPrivate; RefreshPlaylistCreation(); }
    private void FitPlaylistCreation()
    {
        var width = playlistDialogContent.BodyBounds.Width > 0 ? playlistDialogContent.BodyBounds.Width : PlaylistDialogContent.PreferredWidth;
        playlistCreateForm.Measure(new(width, double.PositiveInfinity));
        playlistDialogPages.FormHeight = playlistCreateForm.DesiredSize.Height + PlaylistDialogPages.HeaderHeight +
            playlistCreateAction.TargetHeight - playlistCreateAction.CurrentHeight;
        playlistDialogPages.InvalidateMeasure();
        playlistDialogContent.FitCreation(playlistDialogPages.FormHeight, animate: playlistDialog.IsOpen);
    }
    private void SetPlaylistCreationMessage(string text)
    {
        playlistCreateMessage.Text = text; playlistCreateMessage.IsVisible = text.Length > 0;
        if (playlistDialogPages?.Creating == true) FitPlaylistCreation();
    }
    private void RefreshPlaylistCreation()
    {
        if (playlistCreateSave == null) return;
        var busy = api.Session is { } session && pendingPlaylistCreates.Contains(session);
        playlistCreateName.IsEnabled = playlistCreatePublic.IsEnabled = playlistCreatePrivate.IsEnabled = !busy && !playlistCreateUnconfirmed;
        playlistCreatePrivacy.Select(playlistCreateIsPrivate);
        var valid = playlistCreateName.Text.Trim() is { Length: > 0 and <= 255 } title && !title.Any(char.IsControl);
        playlistCreateAction.Reveal(playlistCreateName.Text.Length > 0 || busy || playlistCreateUnconfirmed,
            animate: playlistDialog?.IsOpen == true && playlistDialogPages.Creating);
        TrackButtons.SetAvailability(playlistCreateSave, valid && !demo && me != null && api.Session != null && !playlistCreateUnconfirmed && !busy, busy);
        if (playlistDialogPages?.Creating == true) FitPlaylistCreation();
    }
    private async Task CreatePickerPlaylistAsync()
    {
        if (playlistPickerTarget is not { } track || me is not { } user || api.Session is not { } session ||
            !playlistDialogPages.Creating || playlistCreateUnconfirmed) return;
        var title = playlistCreateName.Text.Trim();
        var sharing = playlistCreateIsPrivate ? "private" : "public";
        if (!new PlaylistCreateRequest(user.Id, track.Id, title, sharing).IsValid() || !pendingPlaylistCreates.Add(session)) return;
        var generation = playlistPickerGeneration;
        SetPlaylistCreationMessage(""); RefreshPlaylistCreation();
        try
        {
            // Seed the playlist in the same POST: a second add request would risk
            // an empty playlist after interruption and duplicate the first track.
            var playlist = await api.CreatePlaylistAsync(user.Id, title, sharing, track.Id, lifetime.Token);
            if (disposed || api.Session != session) return;
            InvalidatePlaylistLibrary(playlist.Id);
            if (generation != playlistPickerGeneration) return;
            if (playlistPickerIds.Add(playlist.Id)) playlistChoices.Add(PlaylistChoice(LibraryData.FromPlaylist(playlist), user.Id, track, session, generation, confirmed: true));
            SetPlaylistPickerMessage("");
            playlistCreateName.Text = "";
            ShowPlaylistCreation(false);
            FitPlaylistList();
            status.Value = UiText.Get("playlist.status.createdWithTrack");
        }
        catch (Exception error)
        {
            if (disposed || generation != playlistPickerGeneration || api.Session != session) return;
            // A lost response is not proof of a failed write. Never replay POST.
            playlistCreateUnconfirmed = error is not SoundCloudException { StatusCode: >= 400 and < 500 };
            SetPlaylistCreationMessage(playlistCreateUnconfirmed
                ? UiText.Get("playlist.create.checkBeforeRetry")
                : FriendlyError(error));
        }
        finally
        {
            pendingPlaylistCreates.Remove(session);
            if (!disposed) RefreshPlaylistCreation();
        }
    }
    private void InvalidatePlaylistLibrary(long playlistId, bool details = true)
    {
        InvalidateLibrarySource("collections");
        if (details) InvalidateProfilePlaylist(playlistId);
        if (details)
            foreach (var key in collectionDetails.Where(pair => pair.Value.Item.Playlist?.Id == playlistId).Select(pair => pair.Key).ToArray())
                collectionDetails.Remove(key);
    }
}
