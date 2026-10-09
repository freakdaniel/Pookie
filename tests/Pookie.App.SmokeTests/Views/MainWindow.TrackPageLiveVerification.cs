using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackPageLiveUiAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var savedVault = SessionVault.Open(new AppDataPaths());
            var account = savedVault.Load() ?? throw new InvalidOperationException("No saved account for the live track page check.");
            AttachBrowser(new NativeBrowserSession(account));
            me = await browser!.GetMeAsync(timeout.Token);
            api.Session = browser.Account; signedIn.Value = true;
            await ShowWorkspaceAsync();
            volume.Value = 0; // Exercise real audio decoding without sound or an OS media session.
            Window.WindowSize = WindowSize.Resizable(1160, 900, minWidth: 1000, minHeight: 680);
            var track = await api.ResolveAsync("https://soundcloud.com/kar-tonw/dipins-etazhi-speed-up", timeout.Token);
            libraryLikes = new([track], null); ShowLibraryTracks(); likesAsList.Value = true;
            await WaitForTrackPageLiveAsync(() => likedRows.Values.Any(row => row.Track?.Id == track.Id && row.Title.ActualWidth > 100));
            var title = likedRows.Values.First(row => row.Track?.Id == track.Id && row.Title.ActualWidth > 100).Title;
            ClickLiveTrackElement(title);
            await WaitForTrackPageLiveAsync(() => page.Value == Page.Track && trackDetailState is { CommentsLoading: false, RelatedLoading: false } &&
                !navigationHistory[navigationIndex].Pending && trackDetailWaveform.HasSamples && trackDetailCover.Source is ImageSource);
            if (trackDetailState!.MetadataError.Length > 0 || trackDetailState.CommentsError.Length > 0 || trackDetailState.RelatedError.Length > 0 ||
                trackDetailState.Comments.Comments.Length == 0 || trackDetailState.Related.Tracks.Length == 0 || current != null)
                throw new InvalidOperationException("Live track page did not load all sections, or title click started playback.");
            await WaitForTrackPageLiveAsync(() => trackDetailRelatedRows.Any(row => row.Track != null && row.Root.IsVisible &&
                row.Root.ActualWidth > 100 && row.Root.ActualHeight > 30));
            await WaitForLoginFrameAsync();
            CaptureUiPreview("track-page-live");
            var firstPageCount = trackDetailState.Comments.Comments.Length;
            await MoreTrackCommentsAsync();
            if (trackDetailState.Comments.Comments.Length <= firstPageCount)
                throw new InvalidOperationException("Live comments pagination did not add rows.");
            Button? playButton = null;
            VisualTree.Visit(trackDetailScroll, element => { if (element is Button button && button.Content == trackDetailPlayIcon) playButton = button; });
            if (playButton == null) throw new InvalidOperationException("Track page play button was not found.");
            ClickLiveTrackElement(playButton);
            await WaitForTrackPageLiveAsync(() => current?.Id == track.Id && audioReady && progress.Value > .1);
            Console.WriteLine("TRACK_PAGE_LIVE_PLAY_OK: native play click, real audio decoding and advancing position");
            var waveformPoint = new Point(trackDetailWaveform.Bounds.X + trackDetailWaveform.Bounds.Width * .25,
                trackDetailWaveform.Bounds.Y + trackDetailWaveform.Bounds.Height / 2);
            SendLikedActionMouse(0x201, 1, waveformPoint); SendLikedActionMouse(0x202, 0, waveformPoint);
            await WaitForTrackPageLiveAsync(() => progress.Value >= track.DurationSeconds * .25 - .5 && progress.Value < track.DurationSeconds * .25 + 3);
            ClickLiveTrackElement(playButton);
            await WaitForTrackPageLiveAsync(() => !isPlaying.Value);
            var timed = trackDetailState.Comments.Comments.First(comment => comment.Timestamp > 1000 && comment.Timestamp < track.Duration);
            var index = Array.FindIndex(trackDetailState.Comments.Comments, comment => comment.Id == timed.Id);
            Button? timeButton = null;
            VisualTree.Visit(trackDetailComments.Children[index], element => { if (element is Button button) timeButton = button; });
            if (timeButton == null) throw new InvalidOperationException("Live timed comment button was not rendered.");
            trackDetailScroll.SetScrollOffsets(0, Math.Max(0, timeButton.Bounds.Y - Window.ClientSize.Height * .6));
            await WaitForLoginFrameAsync();
            ClickLiveTrackElement(timeButton);
            await WaitForTrackPageLiveAsync(() => Math.Abs(progress.Value - timed.Timestamp!.Value / 1000) < 1);
            Console.WriteLine("TRACK_PAGE_LIVE_SEEK_OK: native waveform and comment timestamp clicks, pause button");
            await MoveNavigationAsync(-1);
            if (page.Value != Page.LibraryTracks || !likesAsList.Value || current?.Id != track.Id)
                throw new InvalidOperationException("Live back navigation lost the list or playback.");
            await MoveNavigationAsync(1);
            if (page.Value != Page.Track || trackDetailState?.Track.Id != track.Id || trackDetailState.Comments.Comments.Length <= firstPageCount)
                throw new InvalidOperationException("Live forward navigation lost the page or comments.");
            Console.WriteLine("TRACK_PAGE_LIVE_UI_OK: preserved account, real track data/artwork/waveform/comments/related, native title/play/pause/seek and back/forward; no likes or clipboard writes");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("TRACK_PAGE_LIVE_UI_FAILED: " + error); }
        finally { Window.Close(); }
    }

    private void ClickLiveTrackElement(FrameworkElement element)
    {
        var point = new Point(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        SendLikedActionMouse(0x200, 0, point); SendLikedActionMouse(0x201, 1, point); SendLikedActionMouse(0x202, 0, point);
    }

    private async Task WaitForTrackPageLiveAsync(Func<bool> ready)
    {
        var started = Stopwatch.GetTimestamp();
        while (!ready())
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(30))
                throw new InvalidOperationException($"Live track UI did not settle: page={page.Value}; audio_ready={audioReady}; preparing={audioPreparing}; status={status.Value}");
            await WaitForLoginFrameAsync();
        }
    }
}
