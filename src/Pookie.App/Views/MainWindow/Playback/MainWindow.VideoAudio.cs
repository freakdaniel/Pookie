using Aprillz.MewUI.Animation;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly AnimationClock videoMusicFade = new(TimeSpan.FromMilliseconds(300), Easing.CubicBezier(.4, 0, .2, 1));
    private TaskCompletionSource? videoMusicFadeCompletion;
    private double videoMusicGain = 1;
    private long? videoMusicGeneration;
    private bool videoMusicPaused;

    // Keep the user's volume separate from the temporary viewer gain.
    private void ApplyMusicVolume() => player?.Volume(volume.Value * videoMusicGain);

    private Task FadeVideoMusic(double target, Action? completed = null)
    {
        videoMusicFade.Stop(); videoMusicFadeCompletion?.TrySetResult();
        var completion = videoMusicFadeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var from = videoMusicGain;
        void Finish()
        {
            videoMusicGain = target; RunSync(ApplyMusicVolume);
            if (completed != null) RunSync(completed);
            completion.TrySetResult();
        }
        if (Math.Abs(from - target) < .001) { Finish(); return completion.Task; }
        videoMusicFade.TickCallback = t => { videoMusicGain = from + (target - from) * t; RunSync(ApplyMusicVolume); };
        videoMusicFade.CompletedCallback = Finish;
        videoMusicFade.Start();
        return completion.Task;
    }

    private Task SuspendMusicForChatVideo()
    {
        if (isPlaying.Value && (audioReady || audioPreparing)) videoMusicGeneration = playGeneration;
        return FadeVideoMusic(0, () =>
        {
            if (!disposed && (chatVideoOpen.Value || chatVoiceAudioActive) && videoMusicGeneration == playGeneration && !paused)
            { videoMusicPaused = true; SetPlaybackPaused(true); }
        });
    }

    private void RestoreMusicAfterChatVideo()
    {
        if (disposed)
        {
            videoMusicFade.Stop(); videoMusicFadeCompletion?.TrySetResult();
            videoMusicGeneration = null; videoMusicPaused = false; return;
        }
        var resume = videoMusicPaused && videoMusicGeneration == playGeneration && paused && (audioReady || audioPreparing);
        videoMusicGeneration = null; videoMusicPaused = false;
        if (resume) RunSync(() => SetPlaybackPaused(false));
        _ = FadeVideoMusic(1);
    }
}
