using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    internal void StartWebChallengeVerification(string root, string profile, Action<int> solve, Func<int, int> calls, Func<int> documents,
        Action expireDuringUse) => Run(async () =>
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            while (startupSplash.IsVisible) await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
            preferences = preferences with { ChatBackgroundRefresh = false };
            var account = new WebSession("fixture", "fixture-expired", "Test") { VerifiedUserId = 42 };
            var connected = new NativeBrowserSession(account);
            var worker = new EmbeddedWebWorker(Window, account, root + "/", profile);
            connected.UseEmbedded(worker); AttachBrowser(connected, useNativeHttp: false);
            me = await connected.GetMeAsync(timeout.Token); signedIn.Value = true;
            if (me.Id != 42 || connected.Account.AccessToken != "fixture-token")
                throw new InvalidOperationException("The saved account was not renewed from the existing website profile.");
            await ShowWorkspaceAsync();
            likedIdsReady = true;
            current = new() { Id = 17, Title = "Local slider like" }; UpdateLikeState();
            var like = ToggleTrackLikeAsync(current);
            if (!isLiked.Value || likedIds.Contains(17)) throw new InvalidOperationException("Like was not selected before web verification.");
            await VisibleAsync(like);
            var original = worker.View;
            if (!workspace.IsVisible || loginScreen.IsVisible || embeddedLogin != original || documents() != 1 || calls(17) != 1)
                throw new InvalidOperationException("The slider did not present the existing website in the workspace overlay.");
            if (!isLiked.Value) throw new InvalidOperationException("The slider erased the optimistic like.");
            CaptureUiPreview("web-slider-overlay");
            solve(17); await like;
            await ClosedAsync();
            if (calls(17) != 2 || !ReferenceEquals(original, worker.View)) throw new InvalidOperationException("The verified action was duplicated or the browser replaced.");
            await ToggleTrackLikeAsync(current);
            if (calls(17) != 3 || documents() != 1) throw new InvalidOperationException("The following action lost its live website session.");
            Console.WriteLine("WEB_SLIDER_EMBEDDED_OK: existing in-app website, one rejected request and one confirmed retry, subsequent web action, no extra profile or navigation");
            current = new() { Id = 18, Title = "Local canceled like" }; UpdateLikeState();
            var canceled = ToggleTrackLikeAsync(current);
            await VisibleAsync(canceled); CancelEmbeddedBrowser();
            var stopped = false;
            try { await canceled; }
            catch (OperationCanceledException) { stopped = true; }
            await ClosedAsync(); solve(18);
            if (!stopped || calls(18) != 1 || isLiked.Value || likedIds.Contains(18)) throw new InvalidOperationException("A dismissed slider did not stop and roll back the pending write.");
            await api.SetLikedAsync(42, 21, true, timeout.Token);
            if (calls(21) != 1 || documents() != 2) throw new InvalidOperationException("The website did not recover after cancellation.");
            await api.SetLikedAsync(42, 19, true, timeout.Token);
            if (calls(19) != 2 || embeddedLoginDialog.IsOpen) throw new InvalidOperationException("The invisible check opened an interactive modal or failed to resume.");
            current = new() { Id = 22, Title = "Local rejected like after slider" }; UpdateLikeState();
            var rejected = ToggleTrackLikeAsync(current);
            await VisibleAsync(rejected);
            if (!isLiked.Value) throw new InvalidOperationException("Interactive verification did not retain the selected heart.");
            solve(22);
            var rolledBack = false;
            try { await rejected; }
            catch (SoundCloudException error) when (error.StatusCode == 429) { rolledBack = true; }
            await ClosedAsync();
            if (!rolledBack || isLiked.Value || likedIds.Contains(22) || calls(22) != 2)
                throw new InvalidOperationException("Rejection after passing the slider did not roll back exactly once.");
            Console.WriteLine("WEB_OPTIMISTIC_LIKE_OK: selected before/during slider; passing retains success, rejection after passing rolls back, closing rolls back cancellation");
            var blocked = false;
            try { await api.SetLikedAsync(42, 20, true, timeout.Token); }
            catch (SoundCloudException error) when (error.StatusCode == 403 && error.ChallengeKind == SoundCloudChallengeKind.HardBlock) { blocked = true; }
            if (!blocked || calls(20) != 1 || embeddedLoginDialog.IsOpen) throw new InvalidOperationException("Hard block replayed a write or opened a slider.");
            Console.WriteLine("WEB_SLIDER_CANCEL_OK: close cancels the pending write; next request restores the same profile; invisible checks stay hidden and hard block is terminal");

            localRecent.Add(LibraryItem.FromTrack(new() { Id = 31, Title = "Local history fixture" }));
            var historyBlocked = false;
            try { await ShowLibrarySectionAsync(Page.LibraryHistory).WaitAsync(timeout.Token); }
            catch (SoundCloudException error) when (error.ChallengeKind == SoundCloudChallengeKind.HardBlock) { historyBlocked = true; }
            if (!historyBlocked || !librarySectionRetry.Value || string.IsNullOrWhiteSpace(librarySectionStatus.Value) ||
                collectionGrid.ItemsSource.Count != 1 || collectionGrid.ItemsSource.GetItem(0) is not LibraryItem { Track.Id: 31 } || documents() != 2)
                throw new InvalidOperationException("Blocked history did not keep local tracks and offer a retry.");
            await RetryLibrarySectionAsync().WaitAsync(timeout.Token);
            if (librarySectionRetry.Value || librarySectionStatus.Value != "" || documents() != 3 || calls(20) != 1 ||
                !LibraryItems("history").Select(item => item.Track?.Id).SequenceEqual(new long?[] { 31, 32 }))
                throw new InvalidOperationException("Retrying history did not restore the read without replaying the blocked write.");
            Console.WriteLine("WEB_SESSION_RECOVERY_OK: renewed saved credentials, kept local history through a hard block, retried the read only");

            var checkedFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Run(async () =>
            {
                try { using var response = await connected.RequestChatAsync(new("list", 42), timeout.Token); }
                finally { checkedFailure.TrySetResult(); }
            });
            await checkedFailure.Task.WaitAsync(timeout.Token);
            await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
            if (!signedIn.Value || !workspace.IsVisible || loginScreen.IsVisible || browser != connected)
                throw new InvalidOperationException("An isolated chat rejection signed out the active account.");
            var previousAccount = connected.Account;
            var currentView = worker.View;
            expireDuringUse();
            using (await connected.RequestChatAsync(new("list", 42), timeout.Token)) { }
            if (connected.Account.AccessToken != "fixture-token-renewed" || connected.Account != previousAccount ||
                !signedIn.Value || loginScreen.IsVisible || worker.View != currentView || documents() != 3)
                throw new InvalidOperationException("Runtime refresh did not preserve the verified account and its existing WebView.");
            Console.WriteLine("WEB_RUNTIME_SESSION_OK: isolated chat 401 keeps the workspace; expired token rotates and resumes polling in the same WebView");

            async Task VisibleAsync(Task operation)
            {
                while (embeddedLoginSurface?.NativeVisible != true)
                {
                    if (operation.IsCompleted) { await operation; throw new InvalidOperationException("Action finished before showing its slider."); }
                    await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
                }
            }
            async Task ClosedAsync()
            {
                while (embeddedChallenge != null) await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
            }
        }
        catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine(error); }
        finally { Window.Close(); }
    });
}
