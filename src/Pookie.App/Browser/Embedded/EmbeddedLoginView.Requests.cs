using System.Text;
using System.Text.Json;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal sealed partial class EmbeddedLoginView
{
    private readonly TaskCompletionSource workerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, TaskCompletionSource<BrowserRequestEvent>> pendingRequests = [];
    private readonly Dictionary<string, (StringBuilder Text, int Next)> responseBodies = [];
    private readonly Dictionary<string, List<long>> responseIds = [];
    internal event Action<BrowserRequestEvent>? Changed;

    internal async Task PrepareWorkerAsync(Window window, CancellationToken token)
    {
        // WebView2 can load the API worker while hidden. Keep presentation for
        // explicit login or interactive verification.
        var prepareOffscreen = !OperatingSystem.IsWindows();
        if (prepareOffscreen) { preparing = true; preparationBounds = new(0, 0, 900, 700); }
        initialization = InitializeAsync(window);
        await initialization.WaitAsync(token);
        await workerReady.Task.WaitAsync(token);
        if (prepareOffscreen) { preparing = false; browser?.SetVisible(false); }
    }

    internal async Task PrepareExistingAsync(Window window, Rect bounds, CancellationToken token)
    {
        frozen = active = false; preparing = true; preparationBounds = bounds;
        UpdateBounds();
        await CapturePreviewAsync(window, token);
        token.ThrowIfCancellationRequested();
        if (stopped) throw new OperationCanceledException("Браузер проверки закрыт.");
        preparing = false; browser?.SetVisible(false);
    }

    internal async Task<BrowserRequestEvent> RequestAsync(BrowserRequestCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (stopped) throw new OperationCanceledException("Браузер закрыт.");
        var result = new TaskCompletionSource<BrowserRequestEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests.Add(command.Id, result);
        try
        {
            await browser!.ExecuteAsync("if(window===window.top && location.origin===" + JsonSerializer.Serialize(origin) +
                ") window.__pookieRequest?.(" + JsonSerializer.Serialize(command, SoundCloudJson.Default.BrowserRequestCommand) + ");");
            return await result.Task.WaitAsync(token);
        }
        finally { pendingRequests.Remove(command.Id); responseBodies.Remove(command.Id); responseIds.Remove(command.Id); }
    }

    internal Task CancelReadAsync(string id) => stopped || browser == null ? Task.CompletedTask : browser.ExecuteAsync(
        "if(location.origin===" + JsonSerializer.Serialize(origin) + ") window.__pookieRequest?.({id:" + JsonSerializer.Serialize(id) + ",operation:'cancel'});");

    private void WorkerMessage(string raw, string source)
    {
        var message = BrowserRequestProtocol.Parse(raw, source, origin, pairing);
        if (message == null) return;
        try
        {
            if (message.Kind == "ready") workerReady.TrySetResult();
            if (message.Kind == "blocked")
            {
                var error = NativeBrowserSession.BrowserBlocked();
                workerReady.TrySetException(error);
                foreach (var request in pendingRequests.Values) request.TrySetException(error);
            }
            if (pendingRequests.TryGetValue(message.RequestId, out var pending))
            {
                if (message.Kind == "json-chunk")
                {
                    if (!responseBodies.TryGetValue(message.RequestId, out var body)) body = (new(), 0);
                    if (message.ChunkIndex != body.Next || message.Chunk is not { Length: > 0 and <= 1024 } ||
                        body.Text.Length + message.Chunk.Length > 2 * 1024 * 1024 || body.Next >= 4096)
                        throw new InvalidOperationException("Некорректный ответ браузера.");
                    body.Text.Append(message.Chunk); responseBodies[message.RequestId] = (body.Text, body.Next + 1);
                }
                if (message.Kind == "ids" && message.Ids is { } ids)
                {
                    if (!responseIds.TryGetValue(message.RequestId, out var batch)) responseIds[message.RequestId] = batch = [];
                    if (batch.Count + ids.Length > 100000) throw new InvalidOperationException("Слишком много идентификаторов в ответе браузера.");
                    batch.AddRange(ids);
                }
                if (message.Kind == "complete")
                {
                    if (responseBodies.Remove(message.RequestId, out var body)) message = message with { Json = body.Text.ToString() };
                    if (responseIds.Remove(message.RequestId, out var collected)) message = message with { Ids = collected.ToArray() };
                    pending.TrySetResult(message);
                }
            }
            Changed?.Invoke(message);
        }
        catch (Exception error)
        {
            workerReady.TrySetException(error);
            foreach (var request in pendingRequests.Values) request.TrySetException(error);
            Pookie.Logging.AppLog.Failure("Pookie.Browser", "Не удалось обработать ответ сайта", error);
        }
    }

    private void StopWorker()
    {
        workerReady.TrySetCanceled();
        foreach (var request in pendingRequests.Values) request.TrySetCanceled();
        Changed?.Invoke(new("audio-closed"));
    }
}
