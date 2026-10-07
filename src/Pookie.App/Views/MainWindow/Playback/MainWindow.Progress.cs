using Aprillz.MewUI.Animation;
using Pookie.Audio;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const int PlaybackPollIntervalMs = 350;
    private readonly AnimationClock progressAnimation = new(TimeSpan.FromMilliseconds(PlaybackPollIntervalMs), Easing.Linear);
    private double progressFrom, progressTarget;

    private void AnimatePlaybackProgress(AudioState state)
    {
        progressAnimation.Stop();
        progressFrom = progress.Value;
        progressTarget = Math.Clamp(state.Position, 0, progress.Maximum);
        if (!state.Playing || state.Buffering || paused)
            SetPlaybackProgress(progressTarget);
        else if (progressFrom != progressTarget)
            progressAnimation.Start();
    }

    private void AdvancePlaybackProgress(double fraction)
    {
        if (disposed || !audioReady || paused || seekDragging || seeking || pendingSeek != null)
        {
            progressAnimation.Stop();
            return;
        }
        // Every animation frame is a display update, never a seek command.
        SetPlaybackProgress(progressFrom + (progressTarget - progressFrom) * fraction);
    }

    private void SetPlaybackProgress(double position)
    {
        var updating = updatingProgress;
        updatingProgress = true;
        try { progress.Value = position; }
        finally { updatingProgress = updating; }
    }

    private void RefreshPlayerTimeline()
    {
        // Shimmer only while opening a track. A seek or a temporary underrun
        // retains the two progress layers instead of replacing the whole rail.
        var loading = playbackLoading.Value && !audioReady;
        loadingTrack.IsVisible = loading;
        bufferedTrack.IsVisible = progress.IsVisible = !loading;
        loadingTrack.SetLoading(loading);
        SyncExpandedTimeline();
    }
}
