using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.Audio;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatMusicCardsAsync()
    {
        var trackBubble = ChatRows.Single(row => row.Body.Text == "Послушай");
        var playlistBubble = ChatRows.Single(row => row.Message?.Body == "https://soundcloud.com/fixture/sets/list");
        var track = (ChatMusicView)trackBubble.Attachments.Children[0];
        var playlist = (ChatMusicView)playlistBubble.Attachments.Children[0];
        await WaitForLikedLayoutAsync(() => track.Row.Waveform.HasSamples && playlist.Row.Waveform.HasSamples && track.ActualWidth > 400);
        await WaitForLikedLayoutAsync(() => trackBubble.MusicBackdrop.Source != null && playlistBubble.MusicBackdrop.Source != null);
        foreach (var bubble in new[] { trackBubble, playlistBubble })
        {
            var cover = ((ChatMusicView)bubble.Attachments.Children[0]).Row.Cover.Bounds;
            var left = cover.X - bubble.Bubble.Bounds.X;
            var top = cover.Y - bubble.Bubble.Bounds.Y;
            var bottom = (bubble.Body.IsVisible ? bubble.Content.MusicCaptionTop : bubble.Bubble.Bounds.Bottom) - cover.Bottom;
            if (Math.Abs(left - 12) > 1 || Math.Abs(top - left) > 1 || Math.Abs(bottom - left) > 1)
                throw new InvalidOperationException($"Music card padding is not symmetric: {left}, {top}, {bottom}.");
        }
        foreach (var view in new[] { track, playlist })
        {
            var row = view.Row;
            if (Math.Abs(row.Cover.ActualWidth - row.Cover.ActualHeight) > 1 || row.Waveform.ActualWidth < 180 ||
                row.Waveform.Bounds.X < row.Cover.Bounds.Right || row.Waveform.Bounds.Y < row.Title.Bounds.Bottom ||
                !view.Bounds.Contains(row.PlayButton.Bounds) || !view.Bounds.Contains(row.Waveform.Bounds) ||
                Math.Abs(view.ActualHeight - TrackRowLayout.EmbeddedHeight) > 1)
                throw new InvalidOperationException("Chat music lost the shared horizontal library row, waveform or contained controls.");
        }
        if (track.Row.Title.Text != "Fixture track" || !playlist.Row.Author.Text.Contains("Плейлист · 2 трека") || playlist.Row.Title.Text != "Fixture playlist")
            throw new InvalidOperationException("Chat music rows lost track/playlist identity or the playlist count.");
        var glassActions = 0;
        var actionsBottom = 0d;
        VisualTree.Visit(track.Row.Root, child => { if (child is Button { StyleName: TrackButtons.Glass } button) { glassActions++; actionsBottom = Math.Max(actionsBottom, button.Bounds.Bottom); } });
        if (Math.Abs(track.Row.PlayButton.Bounds.Y - track.Row.Cover.Bounds.Y) > 1 ||
            Math.Abs(actionsBottom - track.Row.Cover.Bounds.Bottom) > 1 || track.Row.Waveform.ActualHeight > 57)
            throw new InvalidOperationException("Music controls no longer align with the top/bottom of the artwork or the embedded waveform is too tall.");
        if (glassActions != 4 || trackBubble.Bubble.Background != Raised || playlistBubble.Bubble.Background != Raised ||
            trackBubble.Content.CaptionBackground != ChatOutgoingColor || playlistBubble.Content.CaptionBackground != Color.Transparent ||
            trackBubble.Footer.Bounds.Bottom > trackBubble.Body.Bounds.Bottom + 1.1 ||
            playlistBubble.Footer.Bounds.Bottom > playlistBubble.Attachments.Bounds.Bottom ||
            playlistBubble.Content.ActualHeight > TrackRowLayout.EmbeddedHeight + 1)
            throw new InvalidOperationException("Music messages lost their artwork backdrop, glass actions or inline delivery footer.");
        // Narrow conversations retain the waveform and four actions without overflow.
        var narrow = new ChatBubbleRow(this); narrow.Bind(trackBubble.Message!);
        var narrowView = (ChatMusicView)narrow.Attachments.Children[0];
        narrowView.Measure(new Size(360, 160)); narrowView.Arrange(new Rect(0, 0, 360, 160));
        if (narrowView.Row.Waveform.ActualWidth < 180 || narrowView.Row.Waveform.Bounds.Right > 361)
            throw new InvalidOperationException("A narrow chat clipped the embedded waveform row.");
        var removed = narrowView.Row.Root; narrow.Clear();
        if (chatMusicViews.TryGetValue(removed, out _)) throw new InvalidOperationException("A recycled chat row retained its music playback binding.");
        narrow.Bind(trackBubble.Message! with { Body = "Plain text" });
        if (narrow.MusicBackdrop.Source != null || !ReferenceEquals(narrow.Bubble.Child, narrow.Content) || narrow.Bubble.MaxWidth != 520)
            throw new InvalidOperationException("A recycled music message retained its background or layout on a text message.");
        narrow.Clear(); narrow.Bind(playlistBubble.Message!);
        narrow.Bubble.Measure(new Size(384, 300)); narrow.Bubble.Arrange(new Rect(0, 0, 384, narrow.Bubble.DesiredSize.Height));
        if (narrow.Footer.Bounds.Bottom > narrow.Attachments.Bounds.Bottom + 1 || narrow.Bubble.ActualHeight > TrackRowLayout.EmbeddedHeight + 25)
            throw new InvalidOperationException("A narrow playlist message put delivery time on a separate row.");
        narrow.Clear();

        var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
        var originalOffset = viewer.VerticalOffset; var wasAtBottom = ChatIsAtBottom();
        viewer.SetScrollOffsets(0, Math.Max(0, viewer.VerticalOffset + trackBubble.Bubble.Bounds.Y - chatMessagesView.Bounds.Y - 8));
        await WaitForLoginFrameAsync(); CaptureUiPreview("chats-music-waveform");
        var originalPlayer = player; var originalTrack = current; var originalReady = audioReady;
        var originalPreparing = audioPreparing; var originalPaused = paused; var originalPlaying = isPlaying.Value;
        var originalMaximum = progress.Maximum; var originalProgress = progress.Value;
        var originalQueue = playbackQueue.Snapshot;
        var fixture = new ChatMusicFixturePlayer(); player = fixture;
        try
        {
            current = track.Item!.Track; audioReady = true; audioPreparing = false; paused = false; isPlaying.Value = true;
            progress.Maximum = 120;
            var sourceVersion = playbackQueue.SourceVersion;
            RefreshLikedPlayback();
            await WaitForLikedLayoutAsync(() => track.Row.ShowsPause);
            var button = track.Row.PlayButton;
            var point = new Point(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
            MoveActionPointer(point); PressActionPointer(point, true); PressActionPointer(point, false);
            if (!paused || !fixture.Paused || track.Row.ShowsPause || playbackQueue.SourceVersion != sourceVersion || page.Value != Page.Chats)
                throw new InvalidOperationException("Chat row restarted the current track, navigated away, or failed to pause it.");
            PressActionPointer(point, true); PressActionPointer(point, false);
            if (paused || fixture.Paused || !track.Row.ShowsPause) throw new InvalidOperationException("Chat row failed to resume the current track.");
            var wave = track.Row.Waveform.Bounds;
            point = new Point(wave.X + wave.Width * .6, wave.Y + 10);
            MoveActionPointer(point); PressActionPointer(point, true); PressActionPointer(point, false);
            await WaitForLikedLayoutAsync(() => Math.Abs(fixture.Position - 72) < 1);
            if (playbackQueue.SourceVersion != sourceVersion || Math.Abs(track.Row.Waveform.Progress - .6) > .02)
                throw new InvalidOperationException("Chat waveform seek replaced the queue or lost the current position.");
            CaptureUiPreview("chats-music-waveform-playing");
            if (originalQueue.Context == null)
            {
                var next = track.Item.Track! with { Id = 103, Title = "Next playlist track", WaveformUrl = "https://wave.sndcdn.com/chat-next-fixture.json" };
                waveformRequests[next.WaveformUrl] = Task.FromResult<float[]?>([.1f, .8f, .4f, .7f]);
                playbackQueue.StartContext(new(playlist.Item!.Key, playlist.Item.Title, "Плейлист"), [track.Item.Track!, next], 1);
                current = next; RefreshLikedRows(30);
                await WaitForLikedLayoutAsync(() => playlist.Row.Track?.Id == 103 && playlist.Row.Waveform.HasSamples);
                if (playlist.Row.Title.Text != "Fixture playlist" || !playlist.Row.ShowsPause || track.Row.ShowsPause || Math.Abs(playlist.Row.Waveform.Progress - .25) > .01)
                    throw new InvalidOperationException("Playlist row lost its identity, active-track waveform or progress after advancing.");
            }
        }
        finally
        {
            CancelSeek(); player = originalPlayer; current = originalTrack; audioReady = originalReady; audioPreparing = originalPreparing;
            paused = originalPaused; isPlaying.Value = originalPlaying;
            if (originalQueue.Context == null) playbackQueue.Clear();
            updatingProgress = true; progress.Maximum = originalMaximum; progress.Value = originalProgress; updatingProgress = false;
            RefreshLikedRows(0); RefreshLikedPlayback();
            if (wasAtBottom) RequestChatBottom(); else viewer.SetScrollOffsets(0, originalOffset);
        }
    }

    private sealed class ChatMusicFixturePlayer : IAudioPlayer
    {
        internal bool Paused; internal double Position;
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) { Position = seconds; return Task.CompletedTask; }
        public AudioState Poll() => new(Position, 120, !Paused, false, false);
        public void Pause(bool paused) => Paused = paused;
        public void Volume(double volume) { }
        public void Stop() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
