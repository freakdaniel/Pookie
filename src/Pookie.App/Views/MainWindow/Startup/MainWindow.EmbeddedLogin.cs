using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ModalDialog embeddedLoginDialog = null!;
    private ContentControl embeddedLoginContent = null!;
    private EmbeddedLoginView? embeddedLogin;
    private EmbeddedLoginSurface? embeddedLoginSurface;
    private CancellationTokenSource? embeddedChallenge;

    private void CancelEmbeddedBrowser()
    {
        if (embeddedChallenge != null) embeddedChallenge.Cancel();
        else login?.Cancel();
    }

    private async Task ShowEmbeddedChallengeAsync(EmbeddedWebWorker worker)
    {
        if (disposed || embeddedChallenge != null || worker.Challenge.IsCompleted) return;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        embeddedChallenge = cancel;
        var view = worker.View;
        var surface = new EmbeddedLoginSurface(view);
        embeddedLogin = view; embeddedLoginSurface = surface;
        embeddedLoginContent.Content = surface;
        FitEmbeddedLogin(Window.ClientSize); Window.ClientSizeChanged += FitEmbeddedLogin;
        try
        {
            var width = embeddedLoginContent.Width; var height = embeddedLoginContent.Height;
            var preparing = view.PrepareExistingAsync(Window, new((Window.ClientSize.Width - width) / 2,
                (Window.ClientSize.Height - height) / 2, width, height), cancel.Token);
            embeddedLoginDialog.Opened = surface.Opened; embeddedLoginDialog.Show();
            await preparing;
            if (!worker.Challenge.IsCompleted) await surface.RevealAsync(cancel.Token);
            await worker.Challenge.WaitAsync(cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            await worker.ResetAsync();
            if (!disposed) status.Value = "Проверка отменена. Действие остановлено.";
        }
        catch { await worker.ResetAsync(); throw; }
        finally
        {
            await surface.FreezeAsync();
            Window.ClientSizeChanged -= FitEmbeddedLogin; embeddedLoginDialog.Opened = null;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            embeddedLoginDialog.Closed = () => closed.TrySetResult();
            embeddedLoginDialog.Close(disposed);
            try { await closed.Task.WaitAsync(lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { embeddedLoginDialog.Close(true); }
            embeddedLoginDialog.Closed = null;
            surface.Remove(view);
            if (view.Stopped) view.Dispose();
            embeddedLoginContent.Content = null; surface.Dispose();
            embeddedLogin = null; embeddedLoginSurface = null; embeddedChallenge = null;
        }
    }

    private FrameworkElement EmbeddedLoginPanel()
    {
        var cancel = TrackButtons.Icon(Icons.View("x", 20), CancelEmbeddedBrowser, TrackButtons.Glass).Top();
        ((MaterialTrackButton)cancel).UseLightGlassFinish();
        embeddedLoginContent = new ContentControl().Width(900).Height(700);
        embeddedLoginDialog = new ModalDialog(embeddedLoginContent, cancel, cancel) { DismissRequested = CancelEmbeddedBrowser };
        ((MaterialTrackButton)cancel).UseOverlayBackdrop(embeddedLoginDialog, embeddedLoginDialog.RenderGlassBackdrop);
        return embeddedLoginDialog;
    }

    private async Task<object> AuthorizeEmbeddedAsync(CancellationToken token, string? fixture = null, string? profilePath = null)
    {
        token.ThrowIfCancellationRequested();
        var path = profilePath ?? BrowserProfile.DefaultPath();
        var view = new EmbeddedLoginView(path, fixture);
        var surface = new EmbeddedLoginSurface(view);
        embeddedLogin = view;
        embeddedLoginSurface = surface;
        embeddedLoginContent.Content = surface;
        FitEmbeddedLogin(Window.ClientSize);
        Window.ClientSizeChanged += FitEmbeddedLogin;
        try
        {
            var width = embeddedLoginContent.Width;
            var height = embeddedLoginContent.Height;
            // Show the opaque loading surface immediately, while the native engine loads offscreen.
            var preparing = view.PrepareAsync(Window, new((Window.ClientSize.Width - width) / 2, (Window.ClientSize.Height - height) / 2, width, height), token);
            embeddedLoginDialog.Opened = surface.Opened;
            embeddedLoginDialog.Show();
            await preparing;
            if (!view.Completion.IsCompleted)
                await surface.RevealAsync(token);
            return await view.Completion.WaitAsync(token);
        }
        finally
        {
            // Release the native controller and profile lease before the API worker
            // opens the same durable browser profile, also on cancellation/shutdown.
            await surface.FreezeAsync();
            if (embeddedLogin == view)
            {
                Window.ClientSizeChanged -= FitEmbeddedLogin;
                embeddedLoginDialog.Opened = null;
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                embeddedLoginDialog.Closed = () => closed.TrySetResult();
                embeddedLoginDialog.Close(disposed);
                try { await closed.Task.WaitAsync(lifetime.Token); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { embeddedLoginDialog.Close(true); }
                embeddedLoginDialog.Closed = null;
                embeddedLogin = null; embeddedLoginSurface = null;
                embeddedLoginContent.Content = null;
            }
            await view.StopAsync();
            surface.Dispose();
        }
    }

    private void FitEmbeddedLogin(Size size)
    {
        // Keep the existing overlay's close button outside the native browser,
        // including at the minimum window size: HWND/NSView surfaces draw above MewUI.
        embeddedLoginContent.Width = Math.Min(900, Math.Max(1, size.Width - 128));
        embeddedLoginContent.Height = Math.Min(700, Math.Max(1, size.Height - 128));
    }
}
