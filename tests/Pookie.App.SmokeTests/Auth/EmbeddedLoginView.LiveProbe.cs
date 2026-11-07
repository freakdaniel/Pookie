namespace Pookie.App.Browser;

internal sealed partial class EmbeddedLoginView
{
    internal async Task OpenFixturePopupForVerificationAsync(CancellationToken token)
    {
        await ready.Task.WaitAsync(token);
        // Native script execution supplies the gesture context that the fixture's
        // interval lacks in WebKit's default popup policy.
        await browser!.ExecuteAsync("window.open('/popup','pookie-popup')");
    }
    internal async Task CapturePresentedPageForVerificationAsync(Aprillz.MewUI.Window window, CancellationToken token)
    {
        await Task.Delay(250, token);
        if (!NativePresented || !await CaptureAsync(window, token))
            throw new InvalidOperationException("The revealed native login did not produce a painted page.");
    }
    internal async Task SavePreparationProbeAsync(string path, CancellationToken token)
    {
        Console.WriteLine($"EMBEDDED_LIVE_ENGINE initialized={browser != null} document_ready={ready.Task.IsCompletedSuccessfully} controls_ready={controlsReady.Task.IsCompletedSuccessfully} completed={completed.Task.IsCompleted} preparing={preparing}");
        if (browser != null)
            await File.WriteAllBytesAsync(path, await browser.CaptureAsync().WaitAsync(TimeSpan.FromSeconds(5), token), token);
    }
}
