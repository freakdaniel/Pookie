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
    private FrameworkElement playlistEditTabs = null!, playlistEditDeleteText = null!;
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
        playlistEditName = PlaylistEditInput("Название плейлиста");
        playlistEditPermalink = PlaylistEditInput(""); playlistEditGenre = PlaylistEditInput("Например, Alternative Rock");
        playlistEditTags = PlaylistEditInput("Добавить теги", 4000); playlistEditDate = PlaylistEditInput("ДД.ММ.ГГГГ", 10);
        playlistEditBuy = PlaylistEditInput("https://", 2000); playlistEditLabel = PlaylistEditInput("Название лейбла");
        playlistEditDescription = new MultiLineTextBox().Placeholder("Расскажи о плейлисте").MaxLength(4000).Wrap(true).FontSize(14)
            .Background(Color.Transparent).BorderThickness(0).Padding(0, 4, 18, 4).Height(64);
        playlistEditDescription.OnTextChanged(_ => { if (!playlistEditLoadingFields) RefreshPlaylistEditor(); });
        playlistEditPrivacy = new PlaylistPrivacySelector(value => { playlistEditPrivate = value; RefreshPlaylistEditor(); });
        playlistEditType = new PlaylistEditorChoice("Тип релиза", ("playlist", "Плейлист"), ("album", "Альбом"), ("ep", "EP"), ("single", "Сингл"), ("compilation", "Сборник"));
        playlistEditType.Changed = RefreshPlaylistEditor;
        playlistEditLicense = new PlaylistEditorChoice("Лицензия", ("all-rights-reserved", "Все права защищены"),
            ("cc-by", "CC BY — указание авторства"), ("cc-by-sa", "CC BY-SA — на тех же условиях"),
            ("cc-by-nd", "CC BY-ND — без производных"), ("cc-by-nc", "CC BY-NC — некоммерческая"),
            ("cc-by-nc-sa", "CC BY-NC-SA"), ("cc-by-nc-nd", "CC BY-NC-ND"), ("no-rights-reserved", "Общественное достояние"));
        playlistEditLicense.Changed = RefreshPlaylistEditor;
        playlistEditArtwork = new Image().StretchMode(Stretch.UniformToFill);
        playlistEditArtworkOverlay = CreateCoverPlayback(playlistEditArtwork, () => Run(PickPlaylistEditorArtworkAsync),
            size: 44, glyphSize: 22, scrim: Color.FromArgb(100, 0, 0, 0), actionGlyph: "pencil-simple");
        playlistEditArtworkButton = playlistEditArtworkOverlay.PlaybackButton!.Hint("Изменить обложку");
        playlistEditCover = new Border().Width(128).Height(128).CornerRadius(18).BorderThickness(0).ClipToBounds().Child(
            ArtworkLayer(playlistEditArtwork).Children(playlistEditArtworkOverlay));
        playlistEditCover.MouseEnter += () => playlistEditArtworkOverlay.SetHovered(true);
        playlistEditCover.MouseLeave += () => playlistEditArtworkOverlay.SetHovered(false);
        var basic = new StackPanel().Vertical().Margin(24, 0, 24, 24).Children(
            new Grid().Columns("128,*").Spacing(16).Children(playlistEditCover.Top(), PlaylistEditGroup(
                PlaylistEditRow("Название", playlistEditName, compact: true),
                PlaylistEditRow("Описание", playlistEditDescription, multiline: true, compact: true)).Column(1)));
        playlistEditTrackSummary = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        playlistEditTrackRows = new StackPanel().Vertical().Spacing(6);
        var tracks = new StackPanel().Vertical().Spacing(16).Margin(24, 0, 24, 24).Children(playlistEditTrackSummary, playlistEditTrackRows);
        var release = new StackPanel().Vertical().Spacing(12).Margin(24, 0, 24, 24).Children(
            PlaylistEditSection("Выпуск", PlaylistEditRow("Тип релиза", playlistEditType), PlaylistEditRow("Дата релиза", playlistEditDate)),
            PlaylistEditSection("Категории", PlaylistEditRow("Жанр", playlistEditGenre), PlaylistEditRow("Теги", playlistEditTags)),
            PlaylistEditSection("Публикация", PlaylistEditRow("Лейбл", playlistEditLabel), PlaylistEditRow("Ссылка покупки", playlistEditBuy),
                PlaylistEditRow("Лицензия", playlistEditLicense)));
        playlistEditPermalinkGlass = new PlaylistAddressField(playlistEditPermalink);
        var access = new StackPanel().Vertical().Spacing(12).Margin(24, 0, 24, 24).Children(
            new StackPanel().Vertical().Spacing(8).Children(playlistEditPermalinkGlass,
                new TextBlock().Text("soundcloud.com/…/sets/ · адрес плейлиста").FontSize(12).Foreground(Muted).Margin(16, 0)),
            PlaylistEditGroup(PlaylistEditRow("Видимость", playlistEditPrivacy)));
        FrameworkElement Scroll(FrameworkElement child) => new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0, 0, 4, 0).Content(child);
        playlistEditPageContents = [basic, tracks, release, access];
        playlistEditPages = new DialogPageDeck(PlaylistEditorFrame.MotionEasing, Scroll(basic), Scroll(tracks), Scroll(release), Scroll(access));
        playlistEditTabs = new StackPanel().Horizontal().Spacing(24).Children(
            SectionTab("Основное", playlistEditTab, 0, () => SelectPlaylistEditTab(0), animate: false),
            SectionTab("Треки", playlistEditTab, 1, () => SelectPlaylistEditTab(1), animate: false),
            SectionTab("Релиз", playlistEditTab, 2, () => SelectPlaylistEditTab(2), animate: false),
            SectionTab("Доступ", playlistEditTab, 3, () => SelectPlaylistEditTab(3), animate: false));
        playlistEditHeading = TrackDialogChrome.Heading("Удалить плейлист?").IsVisible(false);
        playlistEditMessage = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        playlistEditValidation = new TextBlock().FontSize(12).Foreground(TrackButtons.OnSelected).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        playlistEditSave = TrackButtons.Icon(Icons.View("check", 26, Color.White), () => Run(SavePlaylistEditorAsync), TrackButtons.Glass, 44)
            .Hint("Сохранить изменения").IsVisible(false);
        ((MaterialTrackButton)playlistEditSave).GlassAccent(Color.FromArgb(235, 0, 155, 245), Color.White);
        ((MaterialTrackButton)playlistEditSave).UseGlassAccentStateLayer();
        playlistEditSaveReveal = new FadingAction(playlistEditSave).Width(44).Height(44);
        // Reserve the action's column while hidden so editing never moves the tabs or resizes the card.
        playlistEditFocus = new Border { Focusable = true, IsTabStop = false }.Background(Color.Transparent).BorderThickness(0)
            .Height(44).Margin(24, 24, 24, 16).Child(new Grid().Columns("*,44").Spacing(12).Children(
                new Grid().Columns("*").Rows("*").CenterVertical().Children(playlistEditTabs, playlistEditHeading), playlistEditSaveReveal.Column(1)));
        playlistEditDeleteConfirm = TrackButtons.Action(new TextBlock().Text("Удалить").FontSize(14).SemiBold(), () => Run(SavePlaylistEditorAsync), TrackButtons.Glass, 44).Right();
        ((MaterialTrackButton)playlistEditDeleteConfirm).GlassAccent(TrackButtons.BlueGlassAccent, Color.White);
        ((MaterialTrackButton)playlistEditDeleteConfirm).UseGlassAccentStateLayer();
        playlistEditFooter = new StackPanel().Vertical().Spacing(12).Margin(24, 0, 24, 24).Children(playlistEditValidation, playlistEditMessage, playlistEditDeleteConfirm);
        playlistEditDeleteText = new TextBlock().FontSize(14).TextWrapping(TextWrapping.Wrap).Margin(24, 0, 24, 24).IsVisible(false);
        playlistEditFields = new StackPanel().Vertical().Children(playlistEditPages);
        var content = new DockPanel().LastChildFill().Children(playlistEditFocus.DockTop(), playlistEditFooter.DockBottom(),
            new Grid().Columns("*").Rows("*").Children(playlistEditFields, playlistEditDeleteText));
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
        playlistEditFooter.IsVisible = playlistEditDeleting || playlistEditValidation.IsVisible || playlistEditMessage.IsVisible;
        var width = Math.Min(PlaylistEditorFrame.PreferredWidth, Math.Max(0, Window.ClientSize.Width - 32));
        playlistEditFocus.Measure(new(width, double.PositiveInfinity));
        playlistEditFooter.Measure(new(width, double.PositiveInfinity));
        var chrome = playlistEditFocus.DesiredSize.Height + playlistEditFooter.DesiredSize.Height;
        var selected = playlistEditDeleting ? playlistEditDeleteText : playlistEditPageContents[playlistEditTab.Value];
        selected.Measure(new(Math.Max(0, width - 4), double.PositiveInfinity));
        var pageHeight = Math.Min(selected.DesiredSize.Height,
            Math.Max(0, Math.Min(PlaylistEditorFrame.MaximumHeight, Window.ClientSize.Height - 32) - chrome));
        if (!playlistEditDeleting) playlistEditPages.SetPageHeight(playlistEditTab.Value, pageHeight);
        playlistEditFrame.Fit(chrome + pageHeight, animate && playlistEditor.IsOpen && !playlistEditor.Running);
    }
    private static string PlaylistEditDateValue(SoundCloudPlaylist playlist)
    {
        if (DateTimeOffset.TryParse(playlist.ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        if (playlist.ReleaseYear is { } year && playlist.ReleaseMonth is { } month && playlist.ReleaseDay is { } day)
            try { return new DateOnly(year, month, day).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture); } catch (ArgumentOutOfRangeException) { }
        return "";
    }
    private PlaylistMetadataUpdate PlaylistEditMetadata(SoundCloudPlaylist playlist)
    {
        static string? Change(string value, string? original) => value == (original ?? "") ? null : value;
        var date = playlistEditDate.Text.Trim();
        if (DateOnly.TryParseExact(date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) date = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
            : "SoundCloud вернул неполный список. Состав треков пока нельзя менять; остальные настройки доступны.";
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
                new StackPanel().Vertical().Spacing(3).Column(1).CenterVertical().Children(new TextBlock().Text(track.Title.Length == 0 ? "Недоступный трек" : track.Title).FontSize(14).TextTrimming(TextTrimming.CharacterEllipsis),
                    new TextBlock().Text(track.Author).FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis)),
                new TextBlock().Text(FormatTime(track.DurationSeconds)).FontSize(12).Foreground(Muted).Column(2).CenterVertical(),
                new StackPanel().Horizontal().Spacing(2).Column(3).CenterVertical().Children(Action("caret-up", "Поднять", -1, i > 0),
                    Action("caret-down", "Опустить", 1, i < playlistEditTracks.Count - 1), Action("x", "Убрать из плейлиста", 0, true))));
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
            var files = await FileDialog.OpenFilesAsync(new OpenFileDialogOptions { Owner = Window, Title = "Обложка плейлиста", Multiselect = false,
                Filters = [new FileFilter("JPEG до 1 МБ", "*.jpg", "*.jpeg")] }, lifetime.Token);
            if (files is not { Length: > 0 } || !playlistEditor.IsOpen || playlistEditTarget != target || api.Session != session) return;
            var file = new FileInfo(files[0]);
            if (file.Length > 1048576) throw new ArgumentException("Нужна обложка JPEG размером до 1 МБ.");
            var bytes = await File.ReadAllBytesAsync(file.FullName, lifetime.Token);
            if (!new PlaylistEditRequest("artwork", me?.Id ?? 0, target.Playlist!.Id, Artwork: Convert.ToBase64String(bytes)).IsValid())
                throw new ArgumentException("Не удалось прочитать JPEG-обложку.");
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
        var invalidAddress = !playlistEditDeleting && request?.Metadata?.Permalink is { } slug && !new PlaylistMetadataUpdate(Permalink: slug).IsValid();
        playlistEditPermalinkGlass.SetInvalid(invalidAddress, animate: playlistEditor.IsOpen && !playlistEditor.Running);
        playlistEditPermalink.Hint(invalidAddress ? "В адресе допустимы латинские буквы, цифры, дефис и подчёркивание." : "Адрес плейлиста");
        var message = "";
        if (!playlistEditDeleting && request != null && !request.IsValid())
        {
            message = request.Title.Length == 0 ? "Укажи название плейлиста." :
                invalidAddress ? "" :
                request.Metadata is { ReleaseDate: { } date } && !new PlaylistMetadataUpdate(ReleaseDate: date).IsValid() ? "Проверь дату релиза: ДД.ММ.ГГГГ." :
                request.Metadata is { PurchaseUrl: { } url } && !new PlaylistMetadataUpdate(PurchaseUrl: url).IsValid() ? "Укажи полную ссылку покупки с https:// или http://." :
                "Проверь введённые данные: недопустимые символы или слишком длинный текст.";
        }
        playlistEditValidation.Text = message; playlistEditValidation.IsVisible = message.Length > 0;
    }
}
