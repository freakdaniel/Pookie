using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatMusicAttachmentsAsync(ChatFixtureTransport transport)
    {
        const string firstUrl = "https://soundcloud.com/fixture/track";
        const string secondUrl = "https://soundcloud.com/fixture/second-track";
        const string thirdUrl = "https://soundcloud.com/fixture/third-track";
        var original = chatMusicCache[firstUrl].Track!;
        CacheChatMusic(secondUrl, LibraryItem.FromTrack(original with { Id = 103, Title = "Second attachment", PermalinkUrl = secondUrl }));
        CacheChatMusic(thirdUrl, LibraryItem.FromTrack(original with { Id = 104, Title = "Third attachment", PermalinkUrl = thirdUrl }));
        libraryCoverCache[103] = ImageSource.FromBgraPixels(1, 1, [220, 100, 40, 255], hasAlpha: false);
        libraryCoverCache[104] = ImageSource.FromBgraPixels(1, 1, [35, 150, 230, 255], hasAlpha: false);
        const string mention = "Привет, смотри какой трек — " + firstUrl + ", классный!";
        const string multiple = "Послушай три трека:\n" + firstUrl + "\n" + secondUrl + "\n" + thirdUrl;
        transport.AddIncoming(mention); transport.AddOwn(multiple); await RefreshChatsAsync();
        await WaitForLikedLayoutAsync(() => ChatRows.Any(row => row.Message?.Body == multiple && row.Attachments.Children.Count == 3) &&
            ChatRows.Any(row => row.Message?.Body == mention && row.Body.MusicCount == 1 && row.Body.MusicBounds[0].Width > 30));
        var mentionBubble = ChatRows.Single(row => row.Message?.Body == mention);
        var multipleBubble = ChatRows.Single(row => row.Message?.Body == multiple);
        await WaitForLikedLayoutAsync(() => mentionBubble.Motion.Opacity > .999 && multipleBubble.Motion.Opacity > .999 && !chatScrollBottom);
        var views = multipleBubble.Attachments.Children.OfType<ChatCompactMusicView>().ToArray();
        await WaitForLikedLayoutAsync(() => views.All(view => ReferenceEquals(view.Row.Cover.Source, CachedCollectionArtwork(view.Item!))));
        await WaitForLikedLayoutAsync(() => multipleBubble.MusicBackdrop.Source != null);
        await WaitForLikedLayoutAsync(() => multipleBubble.MusicBackdrop.Opacity > .999 && views.All(view => view.Loading.Reveal > .999));
        if (multipleBubble.Bubble.ActualWidth is < 323 or > 340)
            throw new InvalidOperationException("Short music attachments did not use a compact minimum width.");
        if (mentionBubble.Body.Text != mention || !mentionBubble.Body.IsVisible || views.Length != 3 ||
            views.Any(view => Math.Abs(view.Row.Cover.ActualWidth - 44) > 1 / Window.DpiScale || view.ActualHeight > 65 || view.Item?.Track == null) ||
            multipleBubble.Attachments.FindVisualChild<TrackWaveform>() != null || multipleBubble.Bubble.ActualHeight > 290 ||
            multipleBubble.Bubble.Background != Raised || multipleBubble.Content.CaptionBackground != ChatOutgoingColor || multipleBubble.Body.Text != "Послушай три трека:")
            throw new InvalidOperationException("Chat mentions lost their inline link or multiple attachments did not reuse compact search rows.");
        CaptureUiPreview("chats-music-attachments");
        var narrow = new ChatBubbleRow(this); narrow.Bind(mentionBubble.Message!);
        narrow.Bubble.Measure(new(240, 1000)); narrow.Bubble.Arrange(new(0, 0, 240, narrow.Bubble.DesiredSize.Height));
        if (narrow.Body.DesiredSize.Height <= 22 || narrow.Body.Bounds.Right > narrow.Bubble.Bounds.Right)
            throw new InvalidOperationException("Inline music mentions failed to wrap in a narrow conversation.");
        narrow.Clear();
        var chip = mentionBubble.Body.MusicBounds[0]; var point = new Point(chip.X + 9, chip.Y + chip.Height / 2);
        var originalTrack = current?.Id; var generation = playGeneration;
        MoveActionPointer(point); PressActionPointer(point, true); PressActionPointer(point, false);
        await WaitForLikedLayoutAsync(() => page.Value == Page.Track && !navigationHistory[navigationIndex].Pending);
        if (trackDetailState?.Track.Id != 101 || current?.Id != originalTrack || playGeneration != generation)
            throw new InvalidOperationException("A message's inline music title did not open its track without changing playback.");
        await MoveNavigationAsync(-1); chatTimer.Stop();
        await WaitForLikedLayoutAsync(() => page.Value == Page.Chats && ChatRows.Any(row => row.Message?.Body == multiple && row.Attachments.Children.Count == 3));
        multipleBubble = ChatRows.Single(row => row.Message?.Body == multiple);
        views = multipleBubble.Attachments.Children.OfType<ChatCompactMusicView>().ToArray();
        var sourceVersion = playbackQueue.SourceVersion;
        try
        {
            var selected = await SelectChatCompactMusicAsync(views[0]);
            if (selected is not { Toggle: false, Track.Id: 101 } ||
                !playbackQueue.Snapshot.Source.Select(entry => entry.Track.Id).SequenceEqual(new long[] { 101, 103, 104 }) ||
                playbackQueue.Context?.Key != views[0].Group.Key || playbackQueue.Current?.SourceIndex != 0)
                throw new InvalidOperationException("Playing an attachment did not create an ordered message-scoped queue.");
            sourceVersion = playbackQueue.SourceVersion;
            if (playbackQueue.Next()?.Track.Id != 103 || playbackQueue.Previous()?.Track.Id != 101)
                throw new InvalidOperationException("Message attachments were not retained for Next/Previous.");
            selected = await SelectChatCompactMusicAsync(views[2]);
            if (selected is not { Toggle: false, Track.Id: 104 } || playbackQueue.SourceVersion != sourceVersion || playbackQueue.Current?.SourceIndex != 2)
                throw new InvalidOperationException("Selecting another attachment rebuilt or escaped its message queue.");
            selected = await SelectChatCompactMusicAsync(views[1]);
            if (selected is not { Toggle: false, Track.Id: 103 } || playbackQueue.Context?.Key != views[1].Group.Key ||
                playbackQueue.Snapshot.Source.Count != 3 || playbackQueue.Current?.SourceIndex != 1)
                throw new InvalidOperationException("Selecting a previously skipped attachment lost the message queue.");
            sourceVersion = playbackQueue.SourceVersion;
            selected = await SelectChatCompactMusicAsync(views[1]);
            if (selected is not { Toggle: true } || playbackQueue.SourceVersion != sourceVersion)
                throw new InvalidOperationException("Toggling the selected attachment replaced the message queue.");
            // A recycled row must leave both playback registries and ignore late results.
            var detached = new ChatBubbleRow(this); detached.Bind(multipleBubble.Message! with { Body = firstUrl + "\n" + secondUrl + "\n" + thirdUrl });
            detached.Bubble.Measure(new(384, 1000)); detached.Bubble.Arrange(new(0, 0, 384, detached.Bubble.DesiredSize.Height));
            if (detached.Body.IsVisible || detached.Footer.Bounds.Bottom > detached.Attachments.Bounds.Bottom || detached.Bubble.ActualHeight > 233)
                throw new InvalidOperationException("Compact attachments without a caption put delivery time on a separate row.");
            var compact = (ChatCompactMusicView)detached.Attachments.Children[0]; detached.Clear();
            if (chatCompactMusicViews.TryGetValue(compact.Row.Root, out _) || compactTrackRows.Contains(compact.Row) || compact.IsCurrent())
                throw new InvalidOperationException("Recycling a compact music message retained its playback binding.");
            if (detached.MusicBackdrop.Source != null || !ReferenceEquals(detached.Bubble.Child, detached.Content))
                throw new InvalidOperationException("Recycling a compact music message retained its mixed cover backdrop.");
        }
        finally { playbackQueue.Clear(); queueLoader.ContextChanged(); RefreshQueue(); RefreshLikedPlayback(); }
    }
}
