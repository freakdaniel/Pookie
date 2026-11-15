using Pookie.App.Storage;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly AsyncCleanup chatCleanup = new();
    private CancellationTokenSource? chatNativeStarts = new();
    private bool chatClosing;
    private Func<byte[], bool, Task<ChatVideoSession>> chatSessionFactory = (bytes, silent) => ChatVideoSession.StartAsync(bytes, silent);

    private Task TrackChatOperation(Task operation)
    {
        // The operation's UI caller handles startup/cancellation failures; its
        // lifetime is retained so shutdown can wait for any stale-session cleanup.
        var token = disposed || chatClosing ? new CancellationToken(true) : chatNativeStarts?.Token ?? new CancellationToken(true);
        chatCleanup.Add(operation.WaitAsync(token), propagateFailure: false);
        return operation;
    }

    private void TrackChatCleanup(Task operation) => chatCleanup.Add(operation);

    private Task DisposeChatResourceAsync(IAsyncDisposable resource)
    {
        Task closing;
        try { closing = resource.DisposeAsync().AsTask(); }
        catch (Exception error) { closing = Task.FromException(error); }
        TrackChatCleanup(closing);
        return closing;
    }

    private void RetireChatHistory()
    {
        var history = chatHistory; chatHistory = null;
        if (history != null) _ = DisposeChatResourceAsync(history);
    }

    private Task RetireChatRecorderAsync()
    {
        var recorder = chatVoiceRecorder; chatVoiceRecorder = null;
        return recorder == null ? Task.CompletedTask : DisposeChatResourceAsync(recorder);
    }

    private void CancelChatNativeStarts(bool renew)
    {
        if (chatNativeStarts == null) return;
        chatNativeStarts.Cancel();
        if (renew && !disposed && !chatClosing)
        {
            chatNativeStarts.Dispose(); chatNativeStarts = new();
        }
    }

    private AsyncResourceStartup<ChatVideoSession> StartOwnedChatSession(byte[] bytes, bool silent = false)
    {
        var startup = new AsyncResourceStartup<ChatVideoSession>(() => chatSessionFactory(bytes, silent), chatNativeStarts?.Token ?? new CancellationToken(true));
        TrackChatCleanup(startup.Completion);
        return startup;
    }

    private async Task FinishChatBeforeCloseAsync()
    {
        chatClosing = true;
        workspace.IsEnabled = workspace.IsHitTestVisible = false;
        ++chatVoicePlaybackGeneration;
        CancelChatNativeStarts(renew: false);
        CancelChatPolling();
        try { await FinishChatVoiceBeforeCloseAsync(); RememberChatThread(); }
        finally
        {
            CancelChatReads();
            foreach (var row in ChatRows) StopChatAnimations(row.Attachments);
            await StopChatVoicePlaybackAsync();
            await RetireChatRecorderAsync();
            RetireChatHistory();
            await FlushChatPersistenceAsync();
        }
    }
}
