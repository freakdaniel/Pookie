using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ConditionalWeakTable<Grid, ChatCompactMusicView> chatCompactMusicViews = new();
    private long chatMusicPlaybackRequest;
    private sealed record ChatMusicGroup(string[] Urls, string Key, string? Name);
    private sealed class ChatCompactMusicView(CompactTrackRow row, ChatMusicGroup group, string url) : Panel, IChatMusicFooter
    {
        internal readonly CompactTrackRow Row = row;
        internal readonly ChatMusicLoading Loading = new(row.Root, compact: true);
        internal readonly ChatMusicGroup Group = group;
        internal readonly string Url = url;
        internal LibraryItem? Item;
        internal Func<bool> IsCurrent = () => false;
        private double footerReserve;
        public void ReserveFooter(double width) { footerReserve = width; Row.Root.Padding = new Thickness(8, 6, 8 + width, 6); }
        internal ChatCompactMusicView Initialize() { Add(Loading); return this; }
        protected override Size MeasureContent(Size availableSize)
        {
            Row.Title.Measure(new(double.PositiveInfinity, 21)); Row.Author.Measure(new(double.PositiveInfinity, 19));
            Row.Duration.Measure(new(double.PositiveInfinity, 24));
            var natural = Math.Max(Row.Title.DesiredSize.Width, Row.Author.DesiredSize.Width) + 120 + Row.Duration.DesiredSize.Width + footerReserve;
            var width = Math.Min(double.IsFinite(availableSize.Width) ? availableSize.Width : 460, Math.Clamp(natural, 300, 460));
            Loading.Measure(new(width, 64)); return new(width, 64);
        }
        protected override Size ArrangeOverride(Size finalSize)
        { Loading.Arrange(new(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height)); return finalSize; }
    }

    private void UpdateChatMusicMentions(ChatBubbleRow bubble, string[] urls)
    {
        if (bubble.Body.UpdateMusic(urls.Where(url => bubble.Body.Text.Contains(url, StringComparison.Ordinal)).Select(url =>
            chatMusicCache.TryGetValue(url, out var item) ? new ChatMusicEditor.Link(url, item.Title, CachedCollectionArtwork(item)) : new(url, "Трек", null)).ToArray()))
            BeginChatMusicChange(bubble);
    }
    private void BeginChatMusicChange(ChatBubbleRow bubble)
    {
        if (!bubble.Attachments.Children.OfType<FrameworkElement>().Any(child => child.ActualWidth > 0)) return;
        bubble.FollowMediaResize = ChatIsAtBottom(); bubble.Content.AnimateMediaChange();
    }

    private void BindChatCompactMusic(ChatBubbleRow bubble, ChatMusicGroup group)
    {
        bubble.SetCompactMusicLayout();
        var revision = chatRevision;
        var artwork = new List<Task<ImageSource?>>();
        foreach (var url in group.Urls)
        {
            ChatCompactMusicView view = null!;
            var row = CreateCompactTrackRow(_ => Run(() => PlayChatCompactMusicAsync(view)),
                open: () => { if (view.IsCurrent()) Run(() => OpenChatMusicUrlAsync(url)); });
            view = new(row, group, url);
            view.Initialize();
            if (bubble.Body.IsVisible && url == group.Urls[^1]) view.Margin = new Thickness(0, 0, 0, 12);
            view.IsCurrent = () => !disposed && revision == chatRevision && bubble.Attachments.IsAncestorOf(view) && chatCompactMusicViews.TryGetValue(row.Root, out _);
            chatCompactMusicViews.Add(row.Root, view); bubble.Attachments.Add(view);
            void Apply(LibraryItem item)
            {
                BeginChatMusicChange(bubble);
                view.Item = item;
                BindCompactTrackRow(row, item.Track ?? new SoundCloudTrack { Title = item.Title }, loadArtwork: false);
                SetCollectionArtwork(row.Cover, item);
                if (item.Playlist is { } playlist)
                {
                    row.Title.Text = item.Title;
                    row.Author.Text = $"{(item.IsAlbum ? "Альбом" : "Плейлист")} · {playlist.TrackCount} {RussianPlural(playlist.TrackCount, "трек", "трека", "треков")}";
                    row.Duration.Text = ""; row.Like.IsVisible = false;
                }
                UpdateChatMusicMentions(bubble, group.Urls);
            }
            if (chatMusicCache.TryGetValue(url, out var cached)) Apply(cached);
            async Task<ImageSource?> Load()
            {
                try
                {
                    var item = view.Item ?? await ResolveChatMusicAsync(url);
                    if (!view.IsCurrent()) return null;
                    if (view.Item == null) Apply(item);
                    var image = await GetCollectionArtworkAsync(item);
                    if (!view.IsCurrent()) return null;
                    SetCollectionArtwork(row.Cover, item, image, finished: true);
                    UpdateChatMusicMentions(bubble, group.Urls);
                    return image;
                }
                catch (OperationCanceledException) { }
                catch
                {
                    if (!view.IsCurrent()) return null;
                    row.Title.Text = "Трек или плейлист"; row.Author.Text = "Не удалось загрузить предпросмотр";
                    SetCardArtwork(row.Cover, null, null, finished: true);
                }
                finally { if (view.IsCurrent()) view.Loading.Show(); }
                return null;
            }
            artwork.Add(Load());
        }
        var current = ((ChatCompactMusicView)bubble.Attachments.Children[0]).IsCurrent;
        Run(async () =>
        {
            var covers = (await Task.WhenAll(artwork)).OfType<ImageSource>().ToArray();
            if (!current() || covers.Length == 0) return;
            var backdrop = await Task.Run(() => ArtworkBackdrop.CreateMixed(covers, visibility: .55), lifetime.Token);
            if (current()) bubble.ShowMusicBackdrop(backdrop);
        });
    }

    private async Task PlayChatCompactMusicAsync(ChatCompactMusicView view)
    {
        if (await SelectChatCompactMusicAsync(view) is not { } selection) return;
        if (selection.Toggle) await ToggleAsync();
        else await PlayAsync(selection.Track);
    }

    private async Task<(SoundCloudTrack Track, bool Toggle)?> SelectChatCompactMusicAsync(ChatCompactMusicView view)
    {
        if (!view.IsCurrent() || view.Item == null) return null;
        var request = ++chatMusicPlaybackRequest;
        var sourceVersion = playbackQueue.SourceVersion; var generation = playGeneration;
        // Resolve every attachment before starting, retaining message order rather than load order.
        var items = await Task.WhenAll(view.Group.Urls.Select(async url =>
        {
            try
            {
                var item = await ResolveChatMusicAsync(url);
                if (item.Track is { } track) return (Url: url, Tracks: new[] { track });
                var detail = collectionDetails.GetValueOrDefault(item.Key) ?? await api.GetCollectionDetailAsync(item, ChatReadToken());
                if (view.IsCurrent()) collectionDetails[item.Key] = detail;
                return (Url: url, Tracks: detail.Tracks.Tracks);
            }
            catch (OperationCanceledException) { return (Url: url, Tracks: Array.Empty<SoundCloudTrack>()); }
            catch { return (Url: url, Tracks: Array.Empty<SoundCloudTrack>()); }
        }));
        if (!view.IsCurrent() || request != chatMusicPlaybackRequest || sourceVersion != playbackQueue.SourceVersion || generation != playGeneration) return null;
        var entries = items.SelectMany(item => item.Tracks.Select(track => (item.Url, Track: track))).ToArray();
        var selectedIndex = Array.FindIndex(entries, entry => entry.Url == view.Url);
        if (selectedIndex < 0) return null;
        var tracks = entries.Select(entry => entry.Track).ToArray(); var selected = tracks[selectedIndex];
        if (playbackQueue.Context?.Key == view.Group.Key && playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).SequenceEqual(tracks.Select(track => track.Id)))
        {
            var entry = playbackQueue.Snapshot.Source[selectedIndex];
            if (playbackQueue.Current?.EntryId == entry.EntryId) return (selected, true);
            if (playbackQueue.Select(entry.EntryId) != null) { RefreshQueue(); return (selected, false); }
        }
        StartQueueContext(new(view.Group.Key, "Вложения из чата с " + view.Group.Name, "Сейчас играют вложения сообщения"), tracks, selected, selectedIndex);
        return (selected, false);
    }
}
