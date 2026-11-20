using Pookie.Localization;
using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private TextBox playlistEditPermalink = null!, playlistEditGenre = null!, playlistEditTags = null!, playlistEditDate = null!,
        playlistEditBuy = null!, playlistEditLabel = null!;
    private PlaylistEditorChoice playlistEditType = null!, playlistEditLicense = null!;
    private Image playlistEditArtwork = null!;
    private Button playlistEditArtworkButton = null!, playlistEditClose = null!;
    private Border playlistEditFocus = null!;
    private Border playlistEditCover = null!;
    private TrackArtworkOverlay playlistEditArtworkOverlay = null!;
    private FadingAction playlistEditSaveReveal = null!;
    private PlaylistAddressField playlistEditPermalinkGlass = null!;
    private (double Width, double Scale) playlistEditPreparedAccess;
    private DialogPageDeck playlistEditPages = null!;
    private PlaylistEditorFrame playlistEditFrame = null!;
    private FrameworkElement playlistEditTabs = null!;
    private FrameworkElement playlistEditFooter = null!;
    private FrameworkElement[] playlistEditPageContents = [];
    private readonly ObservableValue<int> playlistEditTab = new(0);
    private StackPanel playlistEditTrackRows = null!;
    private TextBlock playlistEditTrackSummary = null!;
    private TextBlock playlistEditValidation = null!;
    private readonly List<SoundCloudTrack> playlistEditTracks = [];
    private long playlistEditTrackArtworkGeneration;
    private bool playlistEditOrderComplete, playlistEditLoadingFields, playlistEditChoosingArtwork;
    private byte[]? playlistEditArtworkBytes;
    private ImageSource? playlistEditStagedArtwork;
    private string playlistEditOriginalPermalink = "", playlistEditOriginalDate = "", playlistEditOriginalType = "";

    private TextBox PlaylistEditInput(string placeholder, int max = 255)
    {
        var field = new TextBox().Placeholder(placeholder).MaxLength(max).Height(28).FontSize(14)
            .Background(Color.Transparent).BorderThickness(0).Padding(0);
        field.OnTextChanged(_ => { if (!playlistEditLoadingFields) RefreshPlaylistEditor(); });
        return field;
    }
    private static FrameworkElement PlaylistEditRow(string label, FrameworkElement input, bool multiline = false, bool compact = false) =>
        new Grid().Columns(compact ? "80,*" : "124,*").Spacing(12).MinHeight(28).Margin(16, 8).Children(
            new TextBlock().Text(label).FontSize(14).Foreground(TrackButtons.OnSurface)
                .VerticalAlignment(multiline ? VerticalAlignment.Top : VerticalAlignment.Center).Margin(0, multiline ? 4 : 0, 0, 0),
            input.Column(1).VerticalAlignment(multiline ? VerticalAlignment.Top : VerticalAlignment.Center));

    private static FrameworkElement PlaylistEditGroup(params FrameworkElement[] rows)
    {
        var content = new StackPanel().Vertical();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) content.Add(new Border().Height(1).Margin(16, 0).BorderThickness(0).Background(Color.FromArgb(20, 255, 255, 255)));
            content.Add(rows[i]);
        }
        return new Border().CornerRadius(18).BorderThickness(0).Background(Color.FromRgb(41, 41, 45)).Child(content);
    }
    private static FrameworkElement PlaylistEditSection(string title, params FrameworkElement[] rows) =>
        new StackPanel().Vertical().Spacing(6).Children(
            new TextBlock().Text(title).FontSize(12).Foreground(Muted).Margin(16, 0), PlaylistEditGroup(rows));
    private FrameworkElement PlaylistEditorView()
    {
        playlistEditName = PlaylistEditInput(UiText.Get("playlist.edit.title"));
        playlistEditPermalink = PlaylistEditInput(""); playlistEditGenre = PlaylistEditInput(UiText.Get("playlist.edit.genrePlaceholder"));
        playlistEditTags = PlaylistEditInput(UiText.Get("playlist.edit.addTags"), 4000); playlistEditDate = PlaylistEditInput(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern, 20);
        playlistEditBuy = PlaylistEditInput("https://", 2000); playlistEditLabel = PlaylistEditInput(UiText.Get("playlist.edit.labelName"));
        playlistEditDescription = new MultiLineTextBox().Placeholder(UiText.Get("playlist.edit.descriptionPlaceholder")).MaxLength(4000).Wrap(true).FontSize(14)
            .Background(Color.Transparent).BorderThickness(0).Padding(0, 4, 18, 4).Height(64);
        playlistEditDescription.OnTextChanged(_ => { if (!playlistEditLoadingFields) RefreshPlaylistEditor(); });
        playlistEditPrivacy = new PlaylistPrivacySelector(value => { playlistEditPrivate = value; RefreshPlaylistEditor(); });
        playlistEditType = new PlaylistEditorChoice(UiText.Get("playlist.edit.releaseType"), ("playlist", UiText.Get("common.playlist")), ("album", UiText.Get("common.album")), ("ep", "EP"), ("single", UiText.Get("playlist.releaseType.single")), ("compilation", UiText.Get("playlist.releaseType.compilation")));
        playlistEditType.Changed = RefreshPlaylistEditor;
        playlistEditLicense = new PlaylistEditorChoice(UiText.Get("common.license"), ("all-rights-reserved", UiText.Get("license.allRightsReserved")),
            ("cc-by", UiText.Get("license.cc.attribution")), ("cc-by-sa", UiText.Get("license.cc.shareAlike")),
            ("cc-by-nd", UiText.Get("license.cc.noDerivatives")), ("cc-by-nc", UiText.Get("license.cc.nonCommercial")),
            ("cc-by-nc-sa", "CC BY-NC-SA"), ("cc-by-nc-nd", "CC BY-NC-ND"), ("no-rights-reserved", UiText.Get("license.publicDomain")));
        playlistEditLicense.Changed = RefreshPlaylistEditor;
        playlistEditArtwork = new Image().StretchMode(Stretch.UniformToFill);
        playlistEditArtworkOverlay = CreateCoverPlayback(playlistEditArtwork, () => Run(PickPlaylistEditorArtworkAsync),
            size: 44, glyphSize: 22, scrim: Color.FromArgb(100, 0, 0, 0), actionGlyph: "pencil-simple");
        playlistEditArtworkButton = playlistEditArtworkOverlay.PlaybackButton!.Hint(UiText.Get("playlist.actions.changeArtwork"));
        playlistEditCover = new Border().Width(128).Height(128).CornerRadius(18).BorderThickness(0).ClipToBounds().Child(
            ArtworkLayer(playlistEditArtwork).Children(playlistEditArtworkOverlay));
        playlistEditCover.MouseEnter += () => playlistEditArtworkOverlay.SetHovered(true);
        playlistEditCover.MouseLeave += () => playlistEditArtworkOverlay.SetHovered(false);
        var basic = new StackPanel().Vertical().Margin(24, 0, 24, 24).Children(
            new Grid().Columns("128,*").Spacing(16).Children(playlistEditCover.Top(), PlaylistEditGroup(
                PlaylistEditRow(UiText.Get("common.title"), playlistEditName, compact: true),
                PlaylistEditRow(UiText.Get("common.description"), playlistEditDescription, multiline: true, compact: true)).Column(1)));
        playlistEditTrackSummary = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        playlistEditTrackRows = new StackPanel().Vertical().Spacing(6);
        var tracks = new StackPanel().Vertical().Spacing(16).Margin(24, 0, 24, 24).Children(playlistEditTrackSummary, playlistEditTrackRows);
        var release = new StackPanel().Vertical().Spacing(12).Margin(24, 0, 24, 24).Children(
            PlaylistEditSection(UiText.Get("playlist.edit.release"), PlaylistEditRow(UiText.Get("playlist.edit.releaseType"), playlistEditType), PlaylistEditRow(UiText.Get("playlist.edit.releaseDate"), playlistEditDate)),
            PlaylistEditSection(UiText.Get("playlist.edit.categories"), PlaylistEditRow(UiText.Get("common.genre"), playlistEditGenre), PlaylistEditRow(UiText.Get("playlist.edit.tags"), playlistEditTags)),
            PlaylistEditSection(UiText.Get("feed.post.label"), PlaylistEditRow(UiText.Get("playlist.edit.label"), playlistEditLabel), PlaylistEditRow(UiText.Get("playlist.edit.buyLink"), playlistEditBuy),
                PlaylistEditRow(UiText.Get("common.license"), playlistEditLicense)));
        playlistEditPermalinkGlass = new PlaylistAddressField(playlistEditPermalink);
        var access = new StackPanel().Vertical().Spacing(12).Margin(24, 0, 24, 24).Children(
            new StackPanel().Vertical().Spacing(8).Children(playlistEditPermalinkGlass,
                new TextBlock().Text(UiText.Get("playlist.edit.urlHint")).FontSize(12).Foreground(Muted).Margin(16, 0)),
            PlaylistEditGroup(PlaylistEditRow(UiText.Get("playlist.edit.visibility"), playlistEditPrivacy)));
        FrameworkElement Scroll(FrameworkElement child) => new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0, 0, 4, 0).Content(child);
        playlistEditPageContents = [basic, tracks, release, access];
        playlistEditPages = new DialogPageDeck(PlaylistEditorFrame.MotionEasing, Scroll(basic), Scroll(tracks), Scroll(release), Scroll(access));
        playlistEditTabs = new StackPanel().Horizontal().Spacing(24).Children(
            SectionTab(UiText.Get("playlist.edit.basics"), playlistEditTab, 0, () => SelectPlaylistEditTab(0), animateSelection: false),
            SectionTab(UiText.Get("common.tracks"), playlistEditTab, 1, () => SelectPlaylistEditTab(1), animateSelection: false),
            SectionTab(UiText.Get("playlist.releaseType.release"), playlistEditTab, 2, () => SelectPlaylistEditTab(2), animateSelection: false),
            SectionTab(UiText.Get("common.access"), playlistEditTab, 3, () => SelectPlaylistEditTab(3), animateSelection: false));
        playlistEditMessage = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        playlistEditValidation = new TextBlock().FontSize(12).Foreground(TrackButtons.OnSelected).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        playlistEditSave = TrackButtons.Icon(Icons.View("check", 26, Color.White), () => Run(SavePlaylistEditorAsync), TrackButtons.Glass, 44)
            .Hint(UiText.Get("playlist.edit.saveChanges")).IsVisible(false);
        ((MaterialTrackButton)playlistEditSave).GlassAccent(Color.FromArgb(235, 0, 155, 245), Color.White);
        ((MaterialTrackButton)playlistEditSave).UseGlassAccentStateLayer();
        playlistEditSaveReveal = new FadingAction(playlistEditSave).Width(44).Height(44);
        // Reserve the action's column while hidden so editing never moves the tabs or resizes the card.
        playlistEditFocus = new Border { Focusable = true, IsTabStop = false }.Background(Color.Transparent).BorderThickness(0)
            .Height(44).Margin(24, 24, 24, 16).Child(new Grid().Columns("*,44").Spacing(12).Children(
                playlistEditTabs.CenterVertical(), playlistEditSaveReveal.Column(1)));
        playlistEditFooter = new StackPanel().Vertical().Spacing(12).Margin(24, 0, 24, 24).Children(playlistEditValidation, playlistEditMessage);
        playlistEditFields = new StackPanel().Vertical().Children(playlistEditPages);
        var content = new DockPanel().LastChildFill().Children(playlistEditFocus.DockTop(), playlistEditFooter.DockBottom(),
            playlistEditFields);
        var card = new GlassCard { HoverEnabled = false, ResizeInProgress = () => playlistEditFrame?.Resizing == true }.CornerRadius(24).Padding(0).Child(
            new Border().Background(Color.Transparent).BorderThickness(0).CornerRadius(24).ClipToBounds().Child(content));
        playlistEditFrame = new PlaylistEditorFrame(card);
        playlistEditFrame.Initialize();
        playlistEditClose = TrackButtons.Icon(Icons.View("x", 20), ClosePlaylistEditor, TrackButtons.Glass).Top();
        ((MaterialTrackButton)playlistEditClose).UseLightGlassFinish();
        playlistEditor = new ModalDialog(playlistEditFrame, playlistEditFocus, playlistEditClose, staticBackdrop: true, cacheTransition: true,
            backdropTint: TrackDialogChrome.MatteTint, glassSurface: card, refreshTransition: () => playlistEditPages.Animating || playlistEditFrame.Resizing,
            glassMaximumHeight: PlaylistEditorFrame.MaximumHeight, transitionMaximumHeight: PlaylistEditorFrame.MaximumHeight) { DismissRequested = ClosePlaylistEditor };
        playlistEditor.Closed = () => { playlistEditFrame.Freeze(); playlistEditSaveReveal.Show(false, animate: false); playlistEditArtworkOverlay.Reset(); ClearPlaylistEditArtwork(); };
        return playlistEditor;
    }

    private void SelectPlaylistEditTab(int tab)
    {
        playlistEditArtworkOverlay.SetHovered(false);
        playlistEditType.CloseMenu(); playlistEditLicense.CloseMenu();
        playlistEditTab.Value = tab; playlistEditPages.Show(tab); FitPlaylistEditor(); playlistEditFocus.Focus();
    }
    private void PreparePlaylistEditAccess()
    {
        var width = Math.Max(1, Math.Min(PlaylistEditorFrame.PreferredWidth, Window.ClientSize.Width - 32) - 52);
        var scale = Window.DpiScale;
        if (playlistEditPreparedAccess == (width, scale)) return;
        // Prime the small glass rims before the modal starts animating. Hidden
        // pages otherwise pay their first raster/upload cost in a tab transition.
        var graphics = Application.Current.GraphicsFactory;
        using var rendering = graphics.AcquireBackgroundRenderScope();
        using var surface = graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(48 * scale), scale, debugName: "PlaylistFieldPreparation"));
        using var context = graphics.CreateContext(surface);
        context.BeginFrame(surface);
        try
        {
            context.Clear(Color.Transparent);
            playlistEditPermalinkGlass.Measure(new(width, 44));
            playlistEditPermalinkGlass.Arrange(new(0, 0, width, 44));
            playlistEditPermalinkGlass.Render(context);
            playlistEditPrivacy.Measure(new(Math.Max(0, width - 168), 48));
            playlistEditPrivacy.Arrange(new(0, 0, Math.Max(0, width - 168), 48));
            playlistEditPrivacy.Render(context);
        }
        finally { context.EndFrame(); }
        playlistEditPreparedAccess = (width, scale);
    }
    private void FitPlaylistEditor(bool animate = true)
    {
        if (playlistEditFrame == null || playlistEditLoadingFields || playlistEditTarget == null) return;
        playlistEditFooter.IsVisible = playlistEditValidation.IsVisible || playlistEditMessage.IsVisible;
        var width = Math.Min(PlaylistEditorFrame.PreferredWidth, Math.Max(0, Window.ClientSize.Width - 32));
        playlistEditFocus.Measure(new(width, double.PositiveInfinity));
        playlistEditFooter.Measure(new(width, double.PositiveInfinity));
        var chrome = playlistEditFocus.DesiredSize.Height + playlistEditFooter.DesiredSize.Height;
        var selected = playlistEditPageContents[playlistEditTab.Value];
        selected.Measure(new(Math.Max(0, width - 4), double.PositiveInfinity));
        var pageHeight = Math.Min(selected.DesiredSize.Height,
            Math.Max(0, Math.Min(PlaylistEditorFrame.MaximumHeight, Window.ClientSize.Height - 32) - chrome));
        playlistEditPages.SetPageHeight(playlistEditTab.Value, pageHeight);
        playlistEditFrame.Fit(chrome + pageHeight, animate && playlistEditor.IsOpen && !playlistEditor.Running);
    }
    private static string PlaylistEditDateValue(SoundCloudPlaylist playlist)
    {
        if (DateTimeOffset.TryParse(playlist.ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date.ToString("d", CultureInfo.CurrentCulture);
        if (playlist.ReleaseYear is { } year && playlist.ReleaseMonth is { } month && playlist.ReleaseDay is { } day)
            try { return new DateOnly(year, month, day).ToString("d", CultureInfo.CurrentCulture); } catch (ArgumentOutOfRangeException) { }
        return "";
    }
    private PlaylistMetadataUpdate PlaylistEditMetadata(SoundCloudPlaylist playlist)
    {
        static string? Change(string value, string? original) => value == (original ?? "") ? null : value;
        var date = playlistEditDate.Text.Trim();
        if (DateOnly.TryParseExact(date, CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)) date = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new(Change(playlistEditPermalink.Text.Trim(), playlistEditOriginalPermalink), Change(playlistEditGenre.Text.Trim(), playlist.Genre),
            Change(playlistEditTags.Text.Trim(), playlist.TagList), playlistEditDate.Text.Trim() == playlistEditOriginalDate ? null : date,
            Change(playlistEditType.Value, playlistEditOriginalType), Change(playlistEditBuy.Text.Trim(), playlist.PurchaseUrl),
            Change(playlistEditLabel.Text.Trim(), playlist.LabelName), Change(playlistEditLicense.Value, playlist.License ?? "all-rights-reserved"));
    }
    private void RenderPlaylistEditTracks()
    {
        var generation = ++playlistEditTrackArtworkGeneration;
        foreach (var child in playlistEditTrackRows.Children.ToArray()) { playlistEditTrackRows.Remove(child); (child as IDisposable)?.Dispose(); }
        playlistEditTrackSummary.IsVisible = !playlistEditOrderComplete;
        playlistEditTrackSummary.Text = playlistEditOrderComplete ? ""
            : UiText.Get("playlist.edit.partialTrackList");
        for (var i = 0; i < playlistEditTracks.Count; i++)
        {
            var index = i; var track = playlistEditTracks[i];
            var cover = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
            var artwork = new Border().Width(44).Height(44).CornerRadius(6).BorderThickness(0).ClipToBounds()
                .Child(ArtworkLayer(cover)).CenterVertical();
            var url = track.ArtworkUrl ?? track.User?.AvatarUrl;
            SetCardArtwork(cover, null, url);
            Run(async () =>
            {
                var cached = libraryCoverCache.GetValueOrDefault(track.Id) ?? (url == null ? null : collectionImages.GetValueOrDefault(url));
                var source = await CompactArtworkAsync(url, CompactArtworkSide(44), cached);
                if (!disposed && generation == playlistEditTrackArtworkGeneration && playlistEditTarget != null)
                    SetCardArtwork(cover, source, url, finished: true);
            });
            Button Action(string icon, string hint, int direction, bool available)
            {
                var button = TrackButtons.Icon(Icons.View(icon, 18), () => EditPlaylistDraftTrack(index, direction), TrackButtons.Text, 36).Hint(hint);
                button.IsEnabled = available && playlistEditOrderComplete; return button;
            }
            playlistEditTrackRows.Add(new Grid().Columns("44,*,Auto,Auto").Rows("56").Spacing(12).Children(
                artwork,
                new StackPanel().Vertical().Spacing(3).Column(1).CenterVertical().Children(new TextBlock().Text(track.Title.Length == 0 ? UiText.Get("track.unavailable") : track.Title).FontSize(14).TextTrimming(TextTrimming.CharacterEllipsis),
                    new TextBlock().Text(track.Author).FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis)),
                new TextBlock().Text(FormatTime(track.DurationSeconds)).FontSize(12).Foreground(Muted).Column(2).CenterVertical(),
                new StackPanel().Horizontal().Spacing(2).Column(3).CenterVertical().Children(Action("caret-up", UiText.Get("common.moveUp"), -1, i > 0),
                    Action("caret-down", UiText.Get("common.moveDown"), 1, i < playlistEditTracks.Count - 1), Action("x", UiText.Get("playlist.actions.removeFromPlaylist"), 0, true))));
        }
    }
    private void EditPlaylistDraftTrack(int index, int direction)
    {
        if (!playlistEditOrderComplete || playlistEditTarget?.Playlist is not { } playlist || playlistEditSession is not { } session ||
            pendingPlaylistEdits.Contains((session, playlist.Id)) || index < 0 || index >= playlistEditTracks.Count) return;
        if (direction == 0) playlistEditTracks.RemoveAt(index);
        else if (index + direction >= 0 && index + direction < playlistEditTracks.Count)
            (playlistEditTracks[index], playlistEditTracks[index + direction]) = (playlistEditTracks[index + direction], playlistEditTracks[index]);
        RenderPlaylistEditTracks(); RefreshPlaylistEditor();
    }
    private void ClearPlaylistEditArtwork()
    {
        StopCardArtwork(playlistEditArtwork); playlistEditArtwork.Source = null;
        playlistEditStagedArtwork?.Dispose(); playlistEditStagedArtwork = null; playlistEditArtworkBytes = null;
    }
    private async Task PickPlaylistEditorArtworkAsync()
    {
        if (playlistEditChoosingArtwork || playlistEditTarget == null || playlistEditSession is not { } session) return;
        playlistEditChoosingArtwork = true; RefreshPlaylistEditor(); var target = playlistEditTarget;
        try
        {
            var files = await FileDialog.OpenFilesAsync(new OpenFileDialogOptions { Owner = Window, Title = UiText.Get("playlist.artwork.label"), Multiselect = false,
                Filters = [new FileFilter(UiText.Get("artwork.limit.small"), "*.jpg", "*.jpeg")] }, lifetime.Token);
            if (files is not { Length: > 0 } || !playlistEditor.IsOpen || playlistEditTarget != target || api.Session != session) return;
            var file = new FileInfo(files[0]);
            if (file.Length > 1048576) throw new ArgumentException(UiText.Get("artwork.validation.smallJpeg"));
            var bytes = await File.ReadAllBytesAsync(file.FullName, lifetime.Token);
            if (!new PlaylistEditRequest("artwork", me?.Id ?? 0, target.Playlist!.Id, Artwork: Convert.ToBase64String(bytes)).IsValid())
                throw new ArgumentException(UiText.Get("artwork.jpeg.readFailed"));
            var image = await Task.Run(() => Storage.ArtworkImage.Prepare(bytes), lifetime.Token);
            if (!playlistEditor.IsOpen || playlistEditTarget != target || api.Session != session) { image.Dispose(); return; }
            StopCardArtwork(playlistEditArtwork); playlistEditArtwork.Source = image; playlistEditArtworkOverlay.ArtworkChanged();
            playlistEditStagedArtwork?.Dispose(); playlistEditStagedArtwork = image; playlistEditArtworkBytes = bytes;
        }
        catch (Exception error) { SetPlaylistEditMessage(FriendlyError(error)); }
        finally { playlistEditChoosingArtwork = false; RefreshPlaylistEditor(); }
    }
    private void SetPlaylistEditMessage(string message)
    { playlistEditMessage.Text = message; playlistEditMessage.IsVisible = message.Length > 0; FitPlaylistEditor(); }

    private void ValidatePlaylistEdit(PlaylistUpdateRequest? request)
    {
        var invalidAddress = request?.Metadata?.Permalink is { } slug && !new PlaylistMetadataUpdate(Permalink: slug).IsValid();
        playlistEditPermalinkGlass.SetInvalid(invalidAddress, animate: playlistEditor.IsOpen && !playlistEditor.Running);
        playlistEditPermalink.Hint(invalidAddress ? UiText.Get("playlist.edit.permalinkCharacters") : UiText.Get("playlist.edit.url"));
        var message = "";
        if (request != null && !request.IsValid())
        {
            message = request.Title.Length == 0 ? UiText.Get("playlist.edit.titleRequired") :
                invalidAddress ? "" :
                request.Metadata is { ReleaseDate: { } date } && !new PlaylistMetadataUpdate(ReleaseDate: date).IsValid() ? UiText.Format("playlist.edit.invalidReleaseDate", CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern):
                request.Metadata is { PurchaseUrl: { } url } && !new PlaylistMetadataUpdate(PurchaseUrl: url).IsValid() ? UiText.Get("playlist.edit.purchaseUrlInvalid") :
                UiText.Get("playlist.edit.invalidFields");
        }
        playlistEditValidation.Text = message; playlistEditValidation.IsVisible = message.Length > 0;
    }
}
