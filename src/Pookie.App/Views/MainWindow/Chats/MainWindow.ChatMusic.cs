using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ConditionalWeakTable<Grid, ChatMusicView> chatMusicViews = new();
    private readonly ObservableValue<bool> chatMusicOpen = new(false);
    private readonly Dictionary<string, LibraryItem> chatMusicCache = [];
    private readonly Dictionary<string, Task<LibraryItem>> chatMusicLoads = [];
    private readonly HashSet<string> chatMusicAttempted = [];
    private readonly SemaphoreSlim chatMusicGate = new(2, 2);
    private TextBox chatMusicSearch = null!;
    private TextBlock chatMusicStatus = null!;
    private StackPanel chatMusicChoices = null!;
    private CancellationTokenSource? chatMusicRequest;
    private long chatMusicGeneration;
    private readonly DispatcherTimer chatMusicSearchTimer = new(TimeSpan.FromMilliseconds(350));
    private FrameworkElement ChatMusicPickerView()
    {
        chatMusicSearch = new TextBox().Placeholder("Найти трек или плейлист").Background(Color.Transparent).BorderThickness(0).FontSize(14);
        chatMusicSearch.OnTextChanged(_ => { chatMusicSearchTimer.Stop(); if (chatMusicOpen.Value) chatMusicSearchTimer.Start(); });
        chatMusicSearch.OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; chatMusicSearchTimer.Stop(); Run(SearchChatMusicAsync); } });
        chatMusicSearchTimer.Tick += () => { chatMusicSearchTimer.Stop(); Run(SearchChatMusicAsync); };
        chatMusicStatus = new TextBlock().FontSize(12).Foreground(Muted);
        chatMusicChoices = new StackPanel().Vertical().Spacing(6);
        var panel = new Border().Background(Raised).CornerRadius(24).Padding(22).Width(540).Center().Child(
            new Grid().Columns("*").Rows("Auto,Auto,Auto,340").Spacing(14).Children(
                new Grid().Columns("*,36").Rows("36").Children(new TextBlock().Text("Трек или плейлист").FontSize(22).Bold().CenterVertical(),
                    TrackButtons.Icon(Icons.View("x", 20), CloseChatMusicPicker, TrackButtons.Text, 36).Column(1)),
                MaterialSearchField.Create(chatMusicSearch).Row(1), chatMusicStatus.Row(2),
                new ScrollViewer().Content(chatMusicChoices).Background(Color.Transparent).BorderThickness(0).Padding(0).Row(3)));
        return new Grid().Columns("*").Rows("*").BindIsVisible(chatMusicOpen).Children(
            new Button().Background(Color.FromArgb(150, 0, 0, 0)).BorderThickness(0).Padding(0).OnClick(CloseChatMusicPicker), panel);
    }
    private Task OpenChatMusicPickerAsync()
    {
        if (!CanAttachChat()) return Task.CompletedTask;
        CloseChatAttachMenu(); chatMusicSearch.Text = ""; chatMusicOpen.Value = true; chatMusicSearch.Focus();
        return SearchChatMusicAsync();
    }
    private void CloseChatMusicPicker()
    {
        chatMusicOpen.Value = false; ++chatMusicGeneration; chatMusicSearchTimer.Stop();
        chatMusicRequest?.Cancel(); chatMusicRequest?.Dispose(); chatMusicRequest = null; chatMusicChoices?.Clear();
    }
    private async Task SearchChatMusicAsync()
    {
        if (!chatMusicOpen.Value || me == null || chatRecipient == null) return;
        chatMusicRequest?.Cancel(); chatMusicRequest?.Dispose(); chatMusicRequest = CancellationTokenSource.CreateLinkedTokenSource(ChatReadToken());
        var token = chatMusicRequest.Token; var generation = ++chatMusicGeneration; var peer = chatRecipient.Id;
        chatMusicChoices.Clear(); chatMusicStatus.Text = "Загрузка…";
        try
        {
            var query = chatMusicSearch.Text.Trim(); LibraryItem[] items;
            if (query.Length == 0)
            {
                var likes = api.GetLikesAsync(me.Id, token); var playlists = api.GetOwnedPlaylistsAsync(me.Id, token);
                await Task.WhenAll(likes, playlists);
                items = likes.Result.Tracks.Take(20).Select(LibraryItem.FromTrack).Concat(playlists.Result.Take(20)).ToArray();
            }
            else if (ChatMusicLink.IsMusicUrl(query)) items = [await api.ResolveChatMusicAsync(query, token)];
            else items = (await api.SearchResultsAsync(query, SearchSection.All, token)).Items.Where(i => i.Track != null || i.Playlist != null).ToArray();
            if (disposed || generation != chatMusicGeneration || chatRecipient?.Id != peer) return;
            chatMusicStatus.Text = items.Length == 0 ? "Ничего не найдено" : query.Length == 0 ? "Из твоей библиотеки" : "";
            foreach (var item in items)
            {
                var url = item.Track?.PermalinkUrl ?? item.Playlist?.PermalinkUrl;
                if (url == null || !ChatMusicLink.IsMusicUrl(url)) continue;
                var cover = new Image().Width(44).Height(44).StretchMode(Stretch.UniformToFill);
                SetCollectionArtwork(cover, item);
                var button = TrackButtons.Action(new Grid().Columns("44,*").Rows("52").Spacing(12).Children(
                    new Border().CornerRadius(7).ClipToBounds().Child(ArtworkLayer(cover)).CenterVertical(),
                    new StackPanel().Vertical().Spacing(3).Column(1).CenterVertical().Children(
                        new TextBlock().Text(item.Title).FontSize(14).TextTrimming(TextTrimming.CharacterEllipsis),
                        new TextBlock().Text(item.Playlist != null ? $"Плейлист · {item.Playlist.TrackCount} треков" : item.Subtitle).FontSize(12).Foreground(Muted).TextTrimming(TextTrimming.CharacterEllipsis))),
                    () =>
                    {
                        if (chatRecipient?.Id != peer || !CanAttachChat()) return;
                        CacheChatMusic(url, item); CloseChatMusicPicker();
                        chatInput.Text = (chatInput.Text.TrimEnd() is { Length: > 0 } text ? text + "\n" : "") + url;
                        chatInput.CaretPosition = chatInput.Text.Length; chatInput.Focus();
                    }, TrackButtons.Text, 60);
                ((FrameworkElement)button.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
                chatMusicChoices.Add(button);
                Run(async () => { var image = await GetCollectionArtworkAsync(item); if (!disposed && generation == chatMusicGeneration) SetCollectionArtwork(cover, item, image, finished: true); });
            }
        }
        catch (OperationCanceledException) { }
        catch { if (generation == chatMusicGeneration) chatMusicStatus.Text = "Не удалось загрузить музыку. Попробуй ещё раз."; }
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
                CopyTrackLink, track => Run(() => ToggleTrackRepostAsync(track)), track => Run(() => OpenPlaylistPickerAsync(track)),
                (_, fraction) => Run(() => SeekChatMusicAsync(view, fraction)), ArtworkLayer, glass: true);
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
