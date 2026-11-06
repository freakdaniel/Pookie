using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    internal void StartWebChallengeVerification(string root, string profile, Action<int> solve, Func<int, int> calls, Func<int> documents) => Run(async () =>
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            while (startupSplash.IsVisible) await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
            var account = new WebSession("fixture", "fixture-token", "Test");
            var connected = new NativeBrowserSession(account);
            var worker = new EmbeddedWebWorker(Window, account, root + "/", profile);
            connected.UseEmbedded(worker); AttachBrowser(connected); api.Session = account;
            me = await connected.GetMeAsync(timeout.Token); signedIn.Value = true;
            await ShowWorkspaceAsync();
            var like = api.SetLikedAsync(42, 17, true, timeout.Token);
            await VisibleAsync(like);
            var original = worker.View;
            if (!workspace.IsVisible || loginScreen.IsVisible || embeddedLogin != original || documents() != 1 || calls(17) != 1)
                throw new InvalidOperationException("The slider did not present the existing website in the workspace overlay.");
            CaptureUiPreview("web-slider-overlay");
            solve(17); await like;
            await ClosedAsync();
            if (calls(17) != 2 || !ReferenceEquals(original, worker.View)) throw new InvalidOperationException("The verified action was duplicated or the browser replaced.");
            await api.SetLikedAsync(42, 17, false, timeout.Token);
            if (calls(17) != 3 || documents() != 1) throw new InvalidOperationException("The following action lost its live website session.");
            Console.WriteLine("WEB_SLIDER_EMBEDDED_OK: existing in-app website, one rejected request and one confirmed retry, subsequent web action, no extra profile or navigation");
            var canceled = api.SetLikedAsync(42, 18, true, timeout.Token);
            await VisibleAsync(canceled); CancelEmbeddedBrowser();
            var stopped = false;
            try { await canceled; }
            catch (OperationCanceledException) { stopped = true; }
            await ClosedAsync(); solve(18);
            if (!stopped || calls(18) != 1) throw new InvalidOperationException("A dismissed slider did not stop the pending write.");
            await api.SetLikedAsync(42, 21, true, timeout.Token);
            if (calls(21) != 1 || documents() != 2) throw new InvalidOperationException("The website did not recover after cancellation.");
            await api.SetLikedAsync(42, 19, true, timeout.Token);
            if (calls(19) != 2 || embeddedLoginDialog.IsOpen) throw new InvalidOperationException("The invisible check opened an interactive modal or failed to resume.");
            var blocked = false;
            try { await api.SetLikedAsync(42, 20, true, timeout.Token); }
            catch (SoundCloudException error) when (error.StatusCode == 403) { blocked = true; }
            if (!blocked || calls(20) != 1 || embeddedLoginDialog.IsOpen) throw new InvalidOperationException("Hard block replayed a write or opened a slider.");
            Console.WriteLine("WEB_SLIDER_CANCEL_OK: close cancels the pending write; next request restores the same profile; invisible checks stay hidden and hard block is terminal");

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
