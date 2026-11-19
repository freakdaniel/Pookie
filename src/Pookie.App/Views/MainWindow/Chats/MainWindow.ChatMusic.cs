using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ConditionalWeakTable<Grid, ChatMusicView> chatMusicViews = new();
    private ModalDialog chatMusicDialog = null!;
    private readonly Dictionary<string, LibraryItem> chatMusicCache = [];
    private readonly Dictionary<string, Task<LibraryItem>> chatMusicLoads = [];
    private readonly HashSet<string> chatMusicAttempted = [];
    private readonly SemaphoreSlim chatMusicGate = new(2, 2);
    private DialogSearchField chatMusicField = null!;
    private TextBox chatMusicSearch => chatMusicField.Input;
    private readonly ObservableValue<SearchSection> chatMusicSection = new(SearchSection.Tracks);
    private LibraryLoadingView chatMusicLoading = null!;
    private ScrollViewer chatMusicScroll = null!;
    private Image chatMusicEmptyArt = null!;
    private TextBlock chatMusicStatus = null!;
    private StackPanel chatMusicChoices = null!;
    private CancellationTokenSource? chatMusicRequest;
    private long chatMusicGeneration;
    private FrameworkElement ChatMusicPickerView()
    {
        chatMusicField = new DialogSearchField("Найти трек", MutedIcon);
        chatMusicSearch.OnTextChanged(_ => { if (chatMusicDialog.IsOpen) Run(() => SearchChatMusicAsync(debounce: true)); });
        chatMusicSearch.OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; Run(() => SearchChatMusicAsync()); } });
        var tabs = new StackPanel().Horizontal().Spacing(24).Children(
            SectionTab("Треки", chatMusicSection, SearchSection.Tracks, () => SelectChatMusicSection(SearchSection.Tracks), animate: false),
            SectionTab("Плейлисты", chatMusicSection, SearchSection.Playlists, () => SelectChatMusicSection(SearchSection.Playlists), animate: false));
        chatMusicEmptyArt = new Image().Source(ImageSource.FromResource<MainWindow>("Pookie.App.Assets.Stickle.route.png"))
            .Width(104).Height(100).StretchMode(Stretch.Uniform).CenterHorizontal().IsVisible(false);
        chatMusicStatus = new TextBlock().FontSize(13).Foreground(Muted).CenterHorizontal()
            .TextWrapping(TextWrapping.Wrap).TextAlignment(TextAlignment.Center);
        var empty = new StackPanel().Vertical().Spacing(10).CenterVertical().IsHitTestVisible(false)
            .Children(chatMusicEmptyArt, chatMusicStatus);
        chatMusicChoices = new StackPanel().Vertical().Spacing(8);
        chatMusicScroll = new ScrollViewer().Content(chatMusicChoices).Background(Color.Transparent).BorderThickness(0).Padding(0);
        chatMusicLoading = CreateLibraryLoadingView(new Grid().Columns("*").Rows("*").Children(chatMusicScroll, empty));
        chatMusicLoading.FadeInSkeleton = true;
        chatMusicLoading.Skeleton.PlaylistRows = true;
        var content = new Grid().Columns("*").Rows("Auto,Auto,*").Spacing(16).Margin(24).Children(
            tabs, chatMusicField.Row(1), chatMusicLoading.Root.ClipToBounds().Row(2));
        var card = TrackDialogChrome.MatteBody(content).Width(PlaylistDialogContent.PreferredWidth).Height(PlaylistDialogContent.MaximumBodyHeight);
        var close = TrackButtons.Icon(Icons.View("x", 20), CloseChatMusicPicker, TrackButtons.Glass).Top().Hint("Закрыть");
        ((MaterialTrackButton)close).UseLightGlassFinish();
        chatMusicDialog = new ModalDialog(card, chatMusicSearch, close, staticBackdrop: true, cacheTransition: true,
            backdropTint: TrackDialogChrome.MatteTint) { DismissRequested = CloseChatMusicPicker };
        return chatMusicDialog;
    }
    private Task OpenChatMusicPickerAsync()
    {
        if (!CanAttachChat() || chatMusicDialog.IsOpen) return Task.CompletedTask;
        CloseChatAttachMenu(true);
        chatInput.Focus();
        chatMusicSection.Value = SearchSection.Tracks;
        chatMusicField.PlaceholderLabel.Text = "Найти трек";
        chatMusicSearch.Text = "";
        chatMusicDialog.Show();
        return SearchChatMusicAsync();
    }
    private void SelectChatMusicSection(SearchSection section)
    {
        chatMusicSection.Value = section;
        chatMusicField.PlaceholderLabel.Text = section == SearchSection.Tracks ? "Найти трек" : "Найти плейлист";
        Run(() => SearchChatMusicAsync());
    }

    private void CancelChatMusicSearch()
    {
        ++chatMusicGeneration;
        chatMusicRequest?.Cancel();
        chatMusicRequest?.Dispose();
        chatMusicRequest = null;
    }

    private void CloseChatMusicPicker()
    {
        chatMusicDialog?.Close(immediate: disposed);
        CancelChatMusicSearch();
        chatMusicChoices?.Clear();
        chatMusicLoading?.SetLoading(false);
        chatMusicField?.Glyph.SetLoading(false);
    }
    private async Task SearchChatMusicAsync(bool debounce = false)
    {
        if (!chatMusicDialog.IsOpen || me is not { } account || chatRecipient == null) return;
        CancelChatMusicSearch();
        chatMusicScroll.SetScrollOffsets(0, 0);
        chatMusicChoices.Clear();
        chatMusicChoices.IsEnabled = true;
        chatMusicStatus.Text = "";
        chatMusicEmptyArt.IsVisible = false;
        chatMusicLoading.SetLoading(true);
        chatMusicField.Glyph.SetLoading(chatMusicSearch.Text.Trim().Length > 0);
        chatMusicDialog.InvalidateTransitionSnapshot();
        chatMusicRequest = CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken());
        var token = chatMusicRequest.Token;
        var generation = chatMusicGeneration;
        var peer = chatRecipient.Id;
        var section = chatMusicSection.Value;
        var tracks = section == SearchSection.Tracks;
        try
        {
            var query = chatMusicSearch.Text.Trim();
            if (debounce) await Task.Delay(350, token);
            IReadOnlyList<LibraryItem> items;
            var emptyMessage = "Ничего не найдено";
            if (query.Length == 0)
            {
                emptyMessage = tracks ? "Пока нет понравившихся треков" : "Пока нет своих плейлистов";
                items = tracks
                    ? (await api.GetLikesAsync(account.Id, token)).Tracks.Take(20).Select(LibraryItem.FromTrack).ToArray()
                    : (await api.GetOwnedPlaylistsAsync(account.Id, token)).Take(20).ToArray();
            }
            else if (ChatMusicLink.IsMusicUrl(query))
            {
                var resolved = await api.ResolveChatMusicAsync(query, token);
                items = [resolved];
                if (tracks && resolved.Playlist != null) emptyMessage = "Открой ссылку во вкладке «Плейлисты»";
                if (!tracks && resolved.Track != null) emptyMessage = "Открой ссылку во вкладке «Треки»";
            }
            else items = (await api.SearchResultsAsync(query, section, token)).Items;
            if (disposed || generation != chatMusicGeneration || chatRecipient?.Id != peer) return;
            foreach (var item in items)
            {
                var url = tracks ? item.Track?.PermalinkUrl : item.Playlist is { IsSystemMix: true } mix
                    ? PlaylistSharing.ShareUri(mix)?.AbsoluteUri : item.Playlist?.PermalinkUrl;
                var isPrivate = !tracks && item.Playlist is { IsSystemMix: false, IsPrivate: true, Id: > 0 };
                if (!isPrivate && (url == null || !ChatMusicLink.IsMusicUrl(url))) continue;
                var cover = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
                SetCollectionArtwork(cover, item);
                FrameworkElement title = new TextBlock().Text(item.Title).FontSize(14).TextTrimming(TextTrimming.CharacterEllipsis);
                if (isPrivate)
                    title = new DockPanel().LastChildFill().Spacing(6).Left().Children(
                        Icons.View("lock-simple", 14, MutedIcon).DockRight().CenterVertical(), title);
                var subtitle = new TextBlock().Text(item.Playlist is { } list
                    ? $"Плейлист · {list.TrackCount} {RussianPlural(list.TrackCount, "трек", "трека", "треков")}" : item.Subtitle)
                    .FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis);
                var button = TrackButtons.Action(new Grid().Columns("44,*").Rows("52").Spacing(12).Children(
                    new Border().CornerRadius(7).ClipToBounds().Child(ArtworkLayer(cover)).CenterVertical(),
                    new StackPanel().Vertical().Spacing(3).Column(1).CenterVertical().Children(title, subtitle)),
                    () => Run(() => AttachChatMusicAsync(item, url, subtitle, generation, peer, token)), TrackButtons.Text, 64);
                if (isPrivate) button.Hint("Приватный плейлист");
                button.CornerRadius = 10; button.Padding = new Thickness(8, 0);
                ((MaterialTrackButton)button).UseListStateLayer();
                ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
                chatMusicChoices.Add(button);
                Run(async () =>
                {
                    var image = await GetCollectionArtworkAsync(item);
                    if (disposed || generation != chatMusicGeneration) return;
                    SetCollectionArtwork(cover, item, image, finished: true);
                    chatMusicDialog.InvalidateTransitionSnapshot();
                });
            }
            if (chatMusicChoices.Children.Count == 0)
            {
                chatMusicStatus.Text = emptyMessage;
                chatMusicEmptyArt.IsVisible = query.Length > 0 && items.Count == 0;
            }
        }
        catch (OperationCanceledException) { }
        catch { if (generation == chatMusicGeneration) chatMusicStatus.Text = "Не удалось загрузить музыку. Попробуй ещё раз."; }
        finally
        {
            if (generation == chatMusicGeneration)
            {
                chatMusicLoading.SetLoading(false);
                chatMusicField.Glyph.SetLoading(false);
                chatMusicDialog.InvalidateTransitionSnapshot();
            }
        }
    }
    private async Task AttachChatMusicAsync(LibraryItem item, string? url, TextBlock subtitle, long generation,
        long peer, CancellationToken token)
    {
        bool Current() => !disposed && !token.IsCancellationRequested && generation == chatMusicGeneration &&
            chatRecipient?.Id == peer && chatMusicDialog.IsOpen && CanAttachChat();
        if (!Current() || !chatMusicChoices.IsEnabled) return;
        if (item.Playlist is { } playlist && (playlist.IsSystemMix || playlist.IsPrivate))
        {
            chatMusicChoices.IsEnabled = false;
            subtitle.Text = playlist.IsSystemMix ? "Проверяем доступ к миксу…" : "Загружаем секретную ссылку…";
            try
            {
                var resolved = playlist.IsSystemMix ? await api.GetMixForSharingAsync(playlist.Urn, token) : await FetchPlaylistForSharingAsync(playlist, token);
                if (!Current()) return;
                url = resolved.IsSystemMix || resolved.IsPrivate ? PlaylistSharing.ShareUri(resolved)?.AbsoluteUri : null;
                if (url == null)
                {
                    subtitle.Text = playlist.IsSystemMix ? "Публичная ссылка на микс недоступна." : "Секретная ссылка недоступна. Попробуй ещё раз.";
                    return;
                }
                item = LibraryData.FromPlaylist(resolved);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch
            {
                if (Current()) subtitle.Text = "Не удалось получить ссылку. Попробуй ещё раз.";
                return;
            }
            finally { if (generation == chatMusicGeneration) chatMusicChoices.IsEnabled = true; }
        }
        if (url == null) return;
        CacheChatMusic(url, item);
        CloseChatMusicPicker();
        chatInput.Text = (chatInput.Text.TrimEnd() is { Length: > 0 } text ? text + "\n" : "") + url;
        chatInput.CaretPosition = chatInput.Text.Length;
        chatInput.Focus();
    }

    private void CacheChatMusic(string url, LibraryItem item)
    { chatMusicCache[url] = item; while (chatMusicCache.Count > 64) chatMusicCache.Remove(chatMusicCache.First().Key); }
    private void QueueChatMusicSummary(string url)
    {
        if (chatMusicCache.ContainsKey(url) || !chatMusicAttempted.Add(url)) return;
        while (chatMusicAttempted.Count > 128) chatMusicAttempted.Remove(chatMusicAttempted.First());
        var account = chatAccountId;
        Run(async () =>
        {
            try { await ResolveChatMusicAsync(url); if (!disposed && account == chatAccountId) RenderChatInbox(); }
            catch { } // Keep the ordinary link preview usable when metadata is unavailable.
        });
    }
    private async Task<LibraryItem> ResolveChatMusicAsync(string url)
    {
        if (chatMusicCache.TryGetValue(url, out var cached)) return cached;
        if (chatMusicLoads.TryGetValue(url, out var running)) return await running;
        var account = chatAccountId; var token = chatAccountLifetime!.Token;
        async Task<LibraryItem> Fetch()
        {
            await chatMusicGate.WaitAsync(token);
            try { var item = await api.ResolveChatMusicAsync(url, token); if (!disposed && account == chatAccountId) CacheChatMusic(url, item); return item; }
            finally { chatMusicGate.Release(); }
        }
        var task = Fetch(); chatMusicLoads[url] = task;
        try { return await task; } finally { if (chatMusicLoads.GetValueOrDefault(url) == task) chatMusicLoads.Remove(url); }
    }
    private sealed class ChatMusicView(LikedTrackRow row) : Panel, IChatMusicFooter
    {
        internal readonly LikedTrackRow Row = row;
        internal readonly ChatMusicLoading Loading = new(row.Root, compact: false);
        internal Task WaveformLoad = Task.CompletedTask;
        internal LibraryItem? Item;
        internal SoundCloudTrack? PreviewTrack;
        internal ChatMusicGroup Group = null!;
        internal Func<bool> IsCurrent = () => false;
        public void ReserveFooter(double width) => Row.ReserveFooter(width);
        internal ChatMusicView Initialize() { Add(Loading); return this; }
        protected override Size MeasureContent(Size availableSize)
        {
            var width = double.IsFinite(availableSize.Width) ? Math.Min(560, availableSize.Width) : 560;
            Row.FitEmbedded(width); Loading.Measure(new Size(width, TrackRowLayout.EmbeddedHeight));
            return new(width, TrackRowLayout.EmbeddedHeight);
        }
        protected override Size ArrangeOverride(Size finalSize)
        { Loading.Arrange(new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height)); return finalSize; }
    }

    // A backdrop fills the card but never participates in its desired size.
    private sealed class ChatMusicSurface(FrameworkElement content, Image backdrop, ChatBubbleContent body) : Panel
    {
        private readonly Border scrim = new Border().Background(ArtworkBackdrop.CardScrim).IsHitTestVisible(false);
        private readonly Border caption = new Border().IsHitTestVisible(false);
        internal ChatMusicSurface Initialize() { Add(backdrop); Add(scrim); Add(caption); Add(content); return this; }
        protected override Size MeasureContent(Size availableSize)
        {
            content.Measure(availableSize); backdrop.Measure(content.DesiredSize); scrim.Measure(content.DesiredSize);
            return content.DesiredSize;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var area = new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height);
            backdrop.Arrange(area); scrim.Arrange(area); content.Arrange(area);
            caption.Background = body.CaptionBackground;
            var top = body.MusicCaptionTop;
            caption.Arrange(new Rect(Bounds.X, top, finalSize.Width, Math.Max(0, Bounds.Bottom - top)));
            return finalSize;
        }
    }

    private bool BindChatMusic(ChatBubbleRow bubble)
    {
        var message = bubble.Message!; var body = Pookie.App.Messages.ChatProtocol.Project(message,chatAccountId,chatRecipient?.Id??0).Text; var urls = ChatMusicLink.Find(body); if (urls.Length == 0) return false;
        bubble.Attachments.IsVisible = true; bubble.Body.Text = ChatMusicLink.Caption(body, urls); bubble.Body.IsVisible = bubble.Body.Text.Length > 0;
        var group = new ChatMusicGroup(urls, $"chat:{chatAccountId}:{chatRecipient?.Id}:message:{message.Id}", chatRecipient?.Username);
        UpdateChatMusicMentions(bubble, urls);
        if (urls.Length > 1) { BindChatCompactMusic(bubble, group); return true; }
        bubble.SetMusicLayout();
        var revision = chatRevision;
        foreach (var url in urls)
        {
            ChatMusicView view = null!;
            var row = new LikedTrackRow(_ => Run(() => PlayChatMusicAsync(view)),
                _ => Run(() => OpenChatMusicAsync(view)), track => Run(() => ToggleTrackLikeAsync(track)),
                track => Run(() => ToggleTrackRepostAsync(track)), track => Run(() => OpenPlaylistPickerAsync(track)),
                (_, fraction) => Run(() => SeekChatMusicAsync(view, fraction)), ArtworkLayer, glass: true, share: ShareTrack);
            view = new ChatMusicView(row) { Group = group }.Initialize();
            if (bubble.Body.IsVisible && url == urls[^1]) view.Margin = new Thickness(0, 0, 0, 12);
            view.IsCurrent = () => !disposed && revision == chatRevision && bubble.Attachments.IsAncestorOf(view) && chatMusicViews.TryGetValue(row.Root, out _);
            chatMusicViews.Add(row.Root, view); bubble.Attachments.Add(view);
            row.Title.Cursor = CursorType.Hand;
            row.Title.MouseDown += args => { if (args.Button == MouseButton.Left) { args.Handled = true; Run(() => OpenChatMusicAsync(view)); } };
            if (chatMusicCache.TryGetValue(url, out var cached)) ApplyChatMusic(view, cached);
            Run(async () =>
            {
                try
                {
                    var item = view.Item ?? await ResolveChatMusicAsync(url);
                    if (!view.IsCurrent()) return;
                    if (view.Item == null) { BeginChatMusicChange(bubble); ApplyChatMusic(view, item); }
                    UpdateChatMusicMentions(bubble, urls);
                    var artwork = GetCollectionArtworkAsync(item);
                    var track = view.PreviewTrack;
                    if (item.Playlist is { TrackCount: > 0 } && track == null)
                    {
                        var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, ChatReadToken());
                        if (!view.IsCurrent()) return;
                        collectionDetails[item.Key] = detail;
                        view.PreviewTrack = detail.Tracks.Tracks.FirstOrDefault(); RefreshChatMusic(view);
                    }
                    var image = await artwork;
                    await view.WaveformLoad;
                    if (view.IsCurrent()) { SetCollectionArtwork(row.Cover, item, image, finished: true); UpdateChatMusicMentions(bubble, urls); view.Loading.Show(); }
                    if (image != null && url == urls[0])
                    {
                        var backdrop = await Task.Run(() => ArtworkBackdrop.Create(image), lifetime.Token);
                        if (view.IsCurrent()) bubble.ShowMusicBackdrop(backdrop);
                    }
                }
                catch (OperationCanceledException) { }
                catch
                {
                    if (!view.IsCurrent()) return;
                    if (view.Item != null) SetCollectionArtwork(row.Cover, view.Item, finished: true);
                    else { row.Title.Text = "Трек или плейлист"; row.Author.Text = "Не удалось загрузить предпросмотр"; SetCardArtwork(row.Cover, null, null, finished: true); }
                }
                finally { if (view.IsCurrent()) view.Loading.Show(); }
            });
        }
        return true;
    }

    private void ApplyChatMusic(ChatMusicView view, LibraryItem item)
    {
        view.Item = item;
        view.PreviewTrack = item.Track ?? item.Playlist?.Tracks.FirstOrDefault(track => track.Id > 0 && !string.IsNullOrEmpty(track.Title));
        RefreshChatMusic(view);
        SetCollectionArtwork(view.Row.Cover, item);
    }

    private void RefreshChatMusicRows()
    { foreach (var (_, view) in chatMusicViews) if (view.IsCurrent()) RefreshChatMusic(view); }

    private void RefreshChatMusic(ChatMusicView view)
    {
        if (view.Item is not { } item) return;
        var row = view.Row;
        var selected = item.Playlist != null ? current != null && playbackQueue.Context?.Key == item.Key : current?.Id == item.Track?.Id;
        var track = item.Playlist != null && selected ? current : view.PreviewTrack;
        if (track != null && !ReferenceEquals(row.Track, track))
        {
            row.Bind(track); SetCollectionArtwork(row.Cover, item);
            var loading = view.WaveformLoad = LoadLikedWaveformAsync(row, track);
            Run(() => loading);
        }
        row.Title.Text = item.Title;
        if (item.Playlist is { } playlist)
        {
            row.Author.Text = $"{(item.IsAlbum ? "Альбом" : "Плейлист")} · {playlist.TrackCount} {RussianPlural(playlist.TrackCount, "трек", "трека", "треков")} · {playlist.User?.Username ?? item.Subtitle}";
            row.SetCollectionContext(track == null ? "В плейлисте пока нет доступных треков" : track.Author + " — " + track.Title);
            row.Refresh(selected, isPlaying.Value, false, false, selected ? likedPlaybackPosition : 0);
        }
        else { row.SetCollectionContext(null); RefreshLikedRow(row); }
    }

    private async Task OpenChatMusicAsync(ChatMusicView view)
    {
        if (!view.IsCurrent() || view.Item is not { } item) return;
        if (item.Track is { } track) await OpenTrackPageAsync(track); else await OpenLibraryItemAsync(item);
    }

    private async Task PlayChatMusicAsync(ChatMusicView view)
    {
        if (!view.IsCurrent() || view.Item is not { } item) return;
        if (item.Playlist != null) { await PlayLibraryCollectionAsync(item); return; }
        if (item.Track is not { } track) return;
        if (current?.Id == track.Id) { await ToggleAsync(); return; }
        StartQueueContext(new(view.Group.Key, "Чат с " + view.Group.Name, "Сейчас играет из чата"), [track], track, 0);
        await PlayAsync(track);
    }

    private async Task SeekChatMusicAsync(ChatMusicView view, double fraction)
    {
        if (!view.IsCurrent() || view.Item == null) return;
        if (view.Item.Playlist != null ? playbackQueue.Context?.Key != view.Item.Key || current == null : current?.Id != view.Item.Track?.Id)
            await PlayChatMusicAsync(view);
        if (!view.IsCurrent() || current?.Id != view.Row.Track?.Id || !audioReady) return;
        progress.Value = Math.Clamp(fraction, 0, 1) * progress.Maximum; FlushSeek();
    }
}
