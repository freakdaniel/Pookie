using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

// One persistent website owns fetch, the site's tag, cookies, audio and its
// interactive iframe. The modal presents this existing view, never a copy.
internal sealed class EmbeddedWebWorker(Window window, WebSession account, string? fixture = null, string? profilePath = null) : IAsyncDisposable
{
    private readonly Action<Action> dispatch = action => Application.Current.Dispatcher!.BeginInvoke(action);
    private readonly int uiThread = Environment.CurrentManagedThreadId;
    private EmbeddedLoginView? view;
    private Task? initializing;
    private Task resetting = Task.CompletedTask;
    private bool disposed;
    private TaskCompletionSource challenge = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal event Action<BrowserRequestEvent>? Changed;
    internal EmbeddedLoginView View => view ?? throw new InvalidOperationException("Браузер ещё не открыт.");
    internal Task Challenge => challenge.Task;

    internal Task<BrowserRequestEvent> RequestAsync(BrowserRequestCommand command, CancellationToken token) => OnUiAsync(async () =>
    {
        token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(disposed, this);
        await resetting;
        if (view == null)
        {
            view = new(profilePath ?? BrowserProfile.DefaultPath(), fixture, account);
            view.Changed += Observe;
            initializing = view.PrepareWorkerAsync(window, token);
        }
        try { await initializing!; }
        catch { await ResetAsync(); throw; }
        return await view.RequestAsync(command, token);
    }, token);

    private void Observe(BrowserRequestEvent message)
    {
        if (message.Kind == "checking" && challenge.Task.IsCompleted)
            challenge = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (message.Kind is "passed" or "challenge-error" or "blocked") challenge.TrySetResult();
        Changed?.Invoke(message);
    }

    internal Task CancelReadAsync(string id) => OnUiAsync(async () =>
    {
        if (view != null) await view.CancelReadAsync(id);
        return true;
    }, CancellationToken.None);

    internal Task ResetAsync() => OnUiAsync(async () =>
    {
        var previous = view; view = null; initializing = null;
        var previousChallenge = challenge;
        if (previous != null)
        {
            resetting = previous.StopAsync();
            previous.Changed -= Observe;
            if (previous.FindVisualRoot() == null) previous.Dispose();
        }
        await resetting.ConfigureAwait(false);
        previousChallenge.TrySetResult();
        return true;
    }, CancellationToken.None);

    private Task<T> OnUiAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task RunAsync()
        {
            try { token.ThrowIfCancellationRequested(); result.TrySetResult(await operation().ConfigureAwait(false)); }
            catch (OperationCanceledException) { result.TrySetCanceled(); }
            catch (Exception error) { result.TrySetException(error); }
        }
        if (Environment.CurrentManagedThreadId == uiThread) _ = RunAsync();
        else dispatch(() => _ = RunAsync());
        return result.Task.WaitAsync(token);
    }

    public async ValueTask DisposeAsync() { disposed = true; await ResetAsync().ConfigureAwait(false); }
}
