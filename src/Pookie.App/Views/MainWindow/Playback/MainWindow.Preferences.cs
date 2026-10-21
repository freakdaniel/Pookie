using Pookie.App.Storage;
using Pookie.Lyrics;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void ApplyPlaybackPreferences(AppConfiguration before)
    {
        if (before.AutoContinue != preferences.AutoContinue)
        {
            playbackQueue.SetAutoContinue(preferences.AutoContinue);
            queueLoader.PolicyChanged();
            RefreshQueue();
            if (preferences.AutoContinue) Run(() => queueLoader.EnsureAheadAsync());
        }
        if (before.LyricsEnabled != preferences.LyricsEnabled)
        {
            CancelLyricsRequest(); ++lyricsGeneration;
            lyricsResolvedFingerprint = null;
            lyricsResult = new(preferences.LyricsEnabled ? LyricsStatus.Loading : LyricsStatus.NotFound);
            RenderLyrics();
            if (preferences.LyricsEnabled && expandedOpen && expandedPanelMode == PlayerPanel.Lyrics) StartLyricsLoad();
        }
        if (before.LyricsAutoScroll != preferences.LyricsAutoScroll || before.LyricsEnabled != preferences.LyricsEnabled)
        {
            lyricsFollowTimer.Stop();
            if (preferences.LyricsEnabled && preferences.LyricsAutoScroll) ResumeLyricsFollowing();
            else { lyricsFollow = false; smoothScrolls[lyricsScroll].Stop(); }
        }
        // Turning history off also seals this occurrence. Re-enabling starts
        // writing with the next track, never retroactively submits a hidden play.
        if (!preferences.SendPlaybackHistory) playbackHistorySubmitted = true;
        if (before.SystemMediaEnabled != preferences.SystemMediaEnabled)
        {
            if (preferences.SystemMediaEnabled) Run(InitializeSystemMediaAsync);
            else DisposeSystemMedia();
        }
    }
}
