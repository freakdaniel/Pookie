using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
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
    private static FrameworkElement PlaylistEditField(string label, FrameworkElement input) =>
        new StackPanel().Vertical().Spacing(6).Children(new TextBlock().Text(label).FontSize(12).Foreground(TrackButtons.OnSurface), input);
    private static GlassCard PlaylistEditGlass(TextBox field)
    {
        var glass = new GlassCard { HoverEnabled = false, ShadowEnabled = false }.CornerRadius(14).Padding(14, 8).Height(44)
            .Background(GlassSheen.LightControlTint(TrackButtons.GlassSurface)).Child(field);
        field.GotFocus += () => glass.SetEmphasized(true); field.LostFocus += () => glass.SetEmphasized(false);
        return glass;
    }
    private FrameworkElement PlaylistEditorView()
    {
        playlistEditName = PlaylistEditInput("Название плейлиста");
        playlistEditPermalink = PlaylistEditInput("my-playlist"); playlistEditGenre = PlaylistEditInput("Например, Alternative Rock");
        playlistEditTags = PlaylistEditInput("rock \"indie rock\"", 4000); playlistEditDate = PlaylistEditInput("ДД.ММ.ГГГГ", 10);
        playlistEditBuy = PlaylistEditInput("https://", 2000); playlistEditLabel = PlaylistEditInput("Название лейбла");
        playlistEditDescription = new MultiLineTextBox().Placeholder("Расскажи о плейлисте").MaxLength(4000).Wrap(true).FontSize(14)
            .Background(Color.Transparent).BorderThickness(0).Padding(14, 10, 32, 10);
        playlistEditDescription.OnTextChanged(_ => { if (!playlistEditLoadingFields) RefreshPlaylistEditor(); });
        var description = new ShareMessageField(playlistEditDescription) { Height = 110 }.Margin(0, 0, -18, 0);
        playlistEditPrivacy = new PlaylistPrivacySelector(value => { playlistEditPrivate = value; RefreshPlaylistEditor(); });
        playlistEditType = new PlaylistEditorChoice("Тип релиза", ("playlist", "Плейлист"), ("album", "Альбом"), ("ep", "EP"), ("single", "Сингл"), ("compilation", "Сборник"));
        playlistEditType.Changed = RefreshPlaylistEditor;
        playlistEditLicense = new PlaylistEditorChoice("Лицензия", ("all-rights-reserved", "Все права защищены"),
            ("cc-by", "CC BY — указание авторства"), ("cc-by-sa", "CC BY-SA — на тех же условиях"),
            ("cc-by-nd", "CC BY-ND — без производных"), ("cc-by-nc", "CC BY-NC — некоммерческая"),
            ("cc-by-nc-sa", "CC BY-NC-SA"), ("cc-by-nc-nd", "CC BY-NC-ND"), ("no-rights-reserved", "Общественное достояние"));
        playlistEditLicense.Changed = RefreshPlaylistEditor;
        playlistEditArtwork = new Image().StretchMode(Stretch.UniformToFill);
        playlistEditArtworkButton = TrackButtons.Action(new StackPanel().Horizontal().Spacing(8).Children(Icons.View("image", 18),
            new TextBlock().Text("Обложка").FontSize(12)), () => Run(PickPlaylistEditorArtworkAsync), TrackButtons.Glass, 36).Margin(8).Bottom();
        ((MaterialTrackButton)playlistEditArtworkButton).UseLightGlassFinish();
        var cover = new Border().Width(160).Height(160).CornerRadius(18).BorderThickness(0).ClipToBounds().Child(
            new Grid().Columns("*").Rows("*").Children(ArtworkLayer(playlistEditArtwork), playlistEditArtworkButton));
        var basic = new StackPanel().Vertical().Margin(24, 0, 24, 12).Children(
            new Grid().Columns("160,*").Spacing(20).Children(cover,
                new StackPanel().Vertical().Spacing(16).Column(1).Children(PlaylistEditField("Название", PlaylistEditGlass(playlistEditName)),
                    PlaylistEditField("Описание", description))));
        playlistEditTrackSummary = new TextBlock().FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        playlistEditTrackRows = new StackPanel().Vertical().Spacing(6);
        var tracks = new StackPanel().Vertical().Spacing(16).Margin(24, 0, 24, 12).Children(playlistEditTrackSummary, playlistEditTrackRows);
        var release = new StackPanel().Vertical().Spacing(18).Margin(24, 0, 24, 12).Children(
            new Grid().Columns("*,*").Spacing(16).Children(PlaylistEditField("Тип релиза", playlistEditType), PlaylistEditField("Дата релиза", PlaylistEditGlass(playlistEditDate)).Column(1)),
            new Grid().Columns("*,*").Spacing(16).Children(PlaylistEditField("Жанр", PlaylistEditGlass(playlistEditGenre)), PlaylistEditField("Теги", PlaylistEditGlass(playlistEditTags)).Column(1)),
            new Grid().Columns("*,*").Spacing(16).Children(PlaylistEditField("Лейбл", PlaylistEditGlass(playlistEditLabel)), PlaylistEditField("Ссылка покупки", PlaylistEditGlass(playlistEditBuy)).Column(1)),
            PlaylistEditField("Лицензия", playlistEditLicense));
        var access = new StackPanel().Vertical().Spacing(18).Margin(24, 0, 24, 12).Children(
            PlaylistEditField("Адрес · soundcloud.com/…/sets/", PlaylistEditGlass(playlistEditPermalink)), playlistEditPrivacy);
        FrameworkElement Scroll(FrameworkElement child) => new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0, 0, 4, 0).Content(child);
        playlistEditPageContents = [basic, tracks, release, access];
        playlistEditPages = new DialogPageDeck(Scroll(basic), Scroll(tracks), Scroll(release), Scroll(access));
        playlistEditTabs = new StackPanel().Horizontal().Spacing(24).Children(
            SectionTab("Основное", playlistEditTab, 0, () => SelectPlaylistEditTab(0), animate: false),
            SectionTab("Треки", playlistEditTab, 1, () => SelectPlaylistEditTab(1), animate: false),
            SectionTab("Релиз", playlistEditTab, 2, () => SelectPlaylistEditTab(2), animate: false),
            SectionTab("Доступ", playlistEditTab, 3, () => SelectPlaylistEditTab(3), animate: false));
        playlistEditHeading = TrackDialogChrome.Heading("Удалить плейлист?").IsVisible(false);
        playlistEditFocus = new Border { Focusable = true, IsTabStop = false }.Background(Color.Transparent).BorderThickness(0)
            .Height(32).Margin(24, 24, 24, 16).Child(new Grid().Columns("*").Rows("*").Children(playlistEditTabs, playlistEditHeading));
        playlistEditMessage = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        playlistEditValidation = new TextBlock().FontSize(12).Foreground(TrackButtons.OnSelected).TextWrapping(TextWrapping.Wrap).MaxHeight(44).IsVisible(false);
        playlistEditSave = TrackButtons.Action(new TextBlock().Text("Сохранить").FontSize(14).SemiBold(), () => Run(SavePlaylistEditorAsync), TrackButtons.Glass, 44);
        ((MaterialTrackButton)playlistEditSave).GlassAccent(TrackButtons.BlueGlassAccent, Color.White);
        ((MaterialTrackButton)playlistEditSave).UseGlassAccentStateLayer();
        playlistEditCancel = TrackButtons.Action(new TextBlock().Text("Отмена").FontSize(14), ClosePlaylistEditor, TrackButtons.Glass, 44);
        ((MaterialTrackButton)playlistEditCancel).UseLightGlassFinish();
        playlistEditFooter = new StackPanel().Vertical().Spacing(12).Margin(24, 16, 24, 24).Children(playlistEditValidation, playlistEditMessage,
            new StackPanel().Horizontal().Spacing(12).Right().Children(playlistEditCancel, playlistEditSave));
        playlistEditDeleteText = new TextBlock().FontSize(14).TextWrapping(TextWrapping.Wrap).Margin(24, 0, 24, 0).IsVisible(false);
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
        playlistEditor.Closed = () => { playlistEditFrame.Freeze(); ClearPlaylistEditArtwork(); };
        return playlistEditor;
    }

    private void SelectPlaylistEditTab(int tab)
    {
        playlistEditType.CloseMenu(); playlistEditLicense.CloseMenu();
        playlistEditTab.Value = tab; playlistEditPages.Show(tab); FitPlaylistEditor(); playlistEditFocus.Focus();
    }
    private void FitPlaylistEditor(bool animate = true)
    {
        if (playlistEditFrame == null || playlistEditLoadingFields || playlistEditTarget == null) return;
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
        foreach (var child in playlistEditTrackRows.Children.ToArray()) { playlistEditTrackRows.Remove(child); (child as IDisposable)?.Dispose(); }
        playlistEditTrackSummary.IsVisible = !playlistEditOrderComplete;
        playlistEditTrackSummary.Text = playlistEditOrderComplete ? ""
            : "SoundCloud вернул неполный список. Состав треков пока нельзя менять; остальные настройки доступны.";
        for (var i = 0; i < playlistEditTracks.Count; i++)
        {
            var index = i; var track = playlistEditTracks[i];
            Button Action(string icon, string hint, int direction, bool available)
            {
                var button = TrackButtons.Icon(Icons.View(icon, 18), () => EditPlaylistDraftTrack(index, direction), TrackButtons.Text, 36).Hint(hint);
                button.IsEnabled = available && playlistEditOrderComplete; return button;
            }
            playlistEditTrackRows.Add(new Grid().Columns("24,*,Auto,Auto").Rows("56").Spacing(12).Children(
                new TextBlock().Text((i + 1).ToString()).FontSize(12).Foreground(Muted).CenterVertical(),
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
            StopCardArtwork(playlistEditArtwork); playlistEditArtwork.Source = image;
            playlistEditStagedArtwork?.Dispose(); playlistEditStagedArtwork = image; playlistEditArtworkBytes = bytes;
        }
        catch (Exception error) { SetPlaylistEditMessage(FriendlyError(error)); }
        finally { playlistEditChoosingArtwork = false; RefreshPlaylistEditor(); }
    }
    private void SetPlaylistEditMessage(string message)
    { playlistEditMessage.Text = message; playlistEditMessage.IsVisible = message.Length > 0; FitPlaylistEditor(); }

    private void ValidatePlaylistEdit(PlaylistUpdateRequest? request)
    {
        var message = "";
        if (!playlistEditDeleting && request != null && !request.IsValid())
        {
            message = request.Title.Length == 0 ? "Укажи название плейлиста." :
                request.Metadata is { Permalink: { } slug } && !new PlaylistMetadataUpdate(Permalink: slug).IsValid() ? "В адресе допустимы латинские буквы, цифры, дефис и подчёркивание." :
                request.Metadata is { ReleaseDate: { } date } && !new PlaylistMetadataUpdate(ReleaseDate: date).IsValid() ? "Проверь дату релиза: ДД.ММ.ГГГГ." :
                request.Metadata is { PurchaseUrl: { } url } && !new PlaylistMetadataUpdate(PurchaseUrl: url).IsValid() ? "Укажи полную ссылку покупки с https:// или http://." :
                "Проверь введённые данные: недопустимые символы или слишком длинный текст.";
        }
        playlistEditValidation.Text = message; playlistEditValidation.IsVisible = message.Length > 0;
    }
}
