using Aprillz.MewUI;
using Pookie.Audio;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatVideoMusicAsync(string messageId)
    {
        await WaitForLikedLayoutAsync(() => videoMusicGain == 1);
        var originalPlayer = player; var originalTrack = current; var originalReady = audioReady;
        var originalPreparing = audioPreparing; var originalPaused = paused; var originalPlaying = isPlaying.Value;
        var originalVolume = volume.Value;
        var fixture = new VideoMusicFixturePlayer(); player = fixture;
        current = new SoundCloudTrack { Id = 1234567, Title = "Video music fixture", Duration = 12000 };
        audioReady = true; audioPreparing = false; paused = false; isPlaying.Value = true; volume.Value = 63;
        ApplyMusicVolume();
        try
        {
            var opening = ActivateChatAttachmentAsync(messageId, 0);
            await WaitForLikedLayoutAsync(() => fixture.Level is > 0 and < 63);
            if (fixture.Paused || volume.Value != 63) throw new InvalidOperationException("Music paused before fading out or changed the selected volume.");
            await opening;
            if (!fixture.Paused || fixture.Level != 0 || !videoMusicPaused) throw new InvalidOperationException("Opening video did not fade/pause music.");
            await WaitForLikedLayoutAsync(() => chatVideoVersion > 0 && chatVideoSurface.Reveal > .999);

            // Actual routed clicks: controls and picture stay open, letterboxing closes.
            Click(new Point(chatVideoControls.Bounds.X + 8, chatVideoControls.Bounds.Y + 8));
            if (!chatVideoOpen.Value) throw new InvalidOperationException("A video control click closed the viewer.");
            var picture = ChatVideoPictureBounds();
            Click(new Point(picture.X + picture.Width / 2, picture.Y + picture.Height / 2));
            if (!chatVideoOpen.Value) throw new InvalidOperationException("Clicking the video picture closed the viewer.");
            Click(new Point(chatVideoImage.Bounds.X + 4, picture.Y + picture.Height / 2));
            if (chatVideoOpen.Value) throw new InvalidOperationException("Clicking video letterboxing did not close the viewer.");
            await WaitForLikedLayoutAsync(() => fixture.Level is > 0 and < 63);
            if (fixture.Paused) throw new InvalidOperationException("Music did not resume during the fade in.");
            await chatVideoClosing;
            await WaitForLikedLayoutAsync(() => fixture.Level == 63);
            if (volume.Value != 63) throw new InvalidOperationException("Video changed the selected music volume.");

            // A paused track stays paused. Rapid close/reopen reverses the fade safely.
            SetPlaybackPaused(true);
            await ActivateChatAttachmentAsync(messageId, 0);
            Click(new Point(4, 200));
            if (chatVideoOpen.Value) throw new InvalidOperationException("Clicking the outer viewer backdrop did not close it.");
            await chatVideoClosing; await WaitForLikedLayoutAsync(() => fixture.Level == 63);
            if (!fixture.Paused) throw new InvalidOperationException("Closing video started an initially paused track.");
            SetPlaybackPaused(false);
            opening = ActivateChatAttachmentAsync(messageId, 0);
            await WaitForLikedLayoutAsync(() => fixture.Level is > 0 and < 63);
            CloseChatVideo();
            await ActivateChatAttachmentAsync(messageId, 0); await opening;
            if (!fixture.Paused || fixture.Level != 0) throw new InvalidOperationException("Rapid video reopen lost music suspension.");
            await ActivateChatAttachmentAsync(messageId, 0);
            if (!fixture.Paused || fixture.Level != 0) throw new InvalidOperationException("Video replacement briefly resumed music.");
            volume.Value = 41;
            if (fixture.Level != 0) throw new InvalidOperationException("Changing music volume bypassed viewer suspension.");
            // Explicit pause after the automatic pause must revoke automatic resume.
            await ToggleAsync(); await ToggleAsync();
            CloseChatVideo(); await chatVideoClosing; await WaitForLikedLayoutAsync(() => fixture.Level == 41);
            if (!fixture.Paused) throw new InvalidOperationException("Closing video undid an explicit music pause.");
            SetPlaybackPaused(false); await ActivateChatAttachmentAsync(messageId, 0);
            await HandleMediaCommandAsync(new(Pookie.Media.MediaAction.Pause));
            CloseChatVideo(); await chatVideoClosing; await WaitForLikedLayoutAsync(() => fixture.Level == 41);
            if (!fixture.Paused) throw new InvalidOperationException("Closing video undid an explicit system media pause.");
        }
        finally
        {
            CloseChatVideo(true); await chatVideoClosing;
            videoMusicFade.Stop(); videoMusicFadeCompletion?.TrySetResult(); videoMusicGain = 1;
            videoMusicGeneration = null; videoMusicPaused = false;
            player = originalPlayer; current = originalTrack; audioReady = originalReady; audioPreparing = originalPreparing;
            paused = originalPaused; isPlaying.Value = originalPlaying; volume.Value = originalVolume; ApplyMusicVolume();
        }
        void Click(Point point) { MoveActionPointer(point); PressActionPointer(point, true); PressActionPointer(point, false); }
    }
    private sealed class VideoMusicFixturePlayer : IAudioPlayer
    {
        internal bool Paused; internal double Level;
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(double seconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AudioState Poll() => new(0, 12, !Paused, false, false);
        public void Pause(bool paused) => Paused = paused;
        public void Volume(double volume) => Level = volume;
        public void Stop() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
