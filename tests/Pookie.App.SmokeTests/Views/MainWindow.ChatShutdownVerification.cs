using System.Reflection;
using Aprillz.MewUI;
using Pookie.App.Messages;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatRefreshAdmissionAsync(ChatFixtureTransport transport)
    {
        var previousPreferences = preferences;
        var previousUser = me ?? throw new InvalidOperationException("Chat admission fixture requires its test account.");
        var restore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = Task.CompletedTask, second = Task.CompletedTask;
        try
        {
            preferences = preferences with { ChatBackgroundRefresh = true };
            // This successful read persists its inbox. Keep that account
            // separate so the subsequent first-load skeleton case stays cold.
            me = previousUser with { Id = 424244 };
            chatTimer.Stop(); ResetChats();
            chatPersistenceClosing = Task.WhenAll(chatPersistenceClosing, restore.Task);
            var before = transport.ListReads;
            first = RefreshChatsAsync(); second = RefreshChatsAsync();
            if (transport.ListReads != before || first.IsCompleted || second.IsCompleted)
                throw new InvalidOperationException("Chat refresh did not wait for the controlled account restore.");
            restore.SetResult();
            await WaitForLikedLayoutAsync(() => transport.ListReady != null && (first.IsCompleted || second.IsCompleted));
            if (transport.ListReads != before + 1 || !chatsBusy)
                throw new InvalidOperationException("Concurrent chat refreshes bypassed admission after account restoration.");
            transport.ListReady!.SetResult();
            await Task.WhenAll(first, second);
            if (transport.ListReads != before + 1)
                throw new InvalidOperationException("Account restoration admitted duplicate inbox reads.");
            Console.WriteLine("CHAT_REFRESH_ADMISSION_OK: two callers behind a held account restore issue one inbox request");
        }
        finally
        {
            restore.TrySetResult(); transport.ListReady?.TrySetResult();
            await Task.WhenAll(first, second);
            preferences = previousPreferences; ResetChats(); await chatPersistenceClosing;
            me = previousUser;
            transport.DelayList = true; transport.ListReady = null;
        }
    }

    private sealed class HeldShutdownContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => pending.Enqueue((callback, state));
        public override SynchronizationContext CreateCopy() => this;
        internal void ExecutePending()
        {
            while (pending.TryDequeue(out var item)) item.Callback(item.State);
        }
    }

    private sealed class ShutdownRecorder : IChatVoiceRecorder
    {
        internal readonly TaskCompletionSource Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Recording => false;
        public bool Paused => false;
        public double Duration => 0;
        public byte[] Peaks => [];
        public ChatVoiceMeter Meter => new(0, []);
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public void Pause() { }
        public Task ResumeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<ChatVoiceRecording> FinishAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task CancelAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposing.TrySetResult(); return new(Closing.Task); }
    }

    private sealed class ShutdownExceptionContext(SynchronizationContext? original) : SynchronizationContext
    {
        internal readonly List<Exception> Errors = [];
        public override void Post(SendOrPostCallback callback, object? state)
        {
            void Execute()
            {
                var previous = Current; SetSynchronizationContext(this);
                try { callback(state); }
                catch (Exception error) { Errors.Add(error); }
                finally { SetSynchronizationContext(previous); }
            }
            if (original != null) original.Post(_ => Execute(), null);
            else Application.Current.Dispatcher!.BeginInvoke(Execute);
        }
        public override SynchronizationContext CreateCopy() => this;
    }

    private async Task VerifyChatShutdownOwnershipAsync()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var recorder = new ShutdownRecorder(); var lateResource = new ShutdownRecorder();
        var startup = new TaskCompletionSource<ShutdownRecorder>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacedSessionReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RetireChatHistory();
        var store = chatHistory = new ChatHistoryStore(dataPaths, 424242);
        var enqueue = typeof(ChatHistoryStore).GetMethod("Run", flags)!.MakeGenericMethod(typeof(bool));
        var blocked = (Task<bool>)enqueue.Invoke(store,
            [new Func<Microsoft.Data.Sqlite.SqliteConnection, bool>(_ => { entered.Set(); release.Wait(); return true; }), CancellationToken.None])!;
        try
        {
            if (!await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5)))) throw new InvalidOperationException("Shutdown history worker did not start.");
            chatVoiceRecorder = recorder;
            using var pendingNative = new AsyncResourceStartup<ShutdownRecorder>(() => startup.Task, chatNativeStarts!.Token);
            TrackChatCleanup(pendingNative.Completion);
            var original = SynchronizationContext.Current;
            var context = new ShutdownExceptionContext(original);
            signedIn.Value = true;
            api.Session = new("shutdown", "fixture-token", "Test");
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Run(async () => { await replacedSessionReply.Task; throw new SoundCloudException("Old rejected fixture", 401) { SessionRejected = true }; });
            }
            finally { SynchronizationContext.SetSynchronizationContext(original); }
            var replacement = new WebSession("replacement", "fixture-new-token", "Test");
            api.Session = replacement;
            replacedSessionReply.SetResult(); await Task.Delay(30);
            if (context.Errors.Count != 0 || api.Session != replacement || !signedIn.Value)
                throw new InvalidOperationException("A rejected reply from the old session disconnected its replacement.");
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Run(async () => { await rejection.Task; throw new SoundCloudException("Late rejected fixture", 401) { SessionRejected = true }; });
            }
            finally { SynchronizationContext.SetSynchronizationContext(original); }
            ResetChats();
            var accountClosing = chatPersistenceClosing;
            if (accountClosing.IsCompleted) throw new InvalidOperationException("Account replacement skipped history/native cleanup.");
            // Leave a normal close waiting on a UI context that will no longer
            // pump. The later immediate disposal must still drain its pixels.
            var heldUi = new HeldShutdownContext();
            var fadedBitmap = chatVideoBitmap = new WriteableBitmap(8, 8);
            chatVideoImage.Source = fadedBitmap; chatVideoOpen.Value = true;
            _ = chatVideoSurface.SetOpen(true, immediate: true);
            SynchronizationContext.SetSynchronizationContext(heldUi);
            try { CloseChatVideo(); }
            finally { SynchronizationContext.SetSynchronizationContext(original); }
            var fadedPresentation = chatVideoClosing;
            if (fadedPresentation.IsCompleted) throw new InvalidOperationException("Normal viewer close did not hold a fade continuation.");
            Dispose();
            var session = api.Session; var before = status.Value;
            var shutdown = FinishShutdownAsync();
            if (shutdown.IsCompleted) throw new InvalidOperationException("Shutdown skipped retired chat resources.");
            rejection.SetResult(); await Task.Delay(30);
            if (context.Errors.Count != 0 || api.Session != session || !signedIn.Value || status.Value != before)
                throw new InvalidOperationException("Late rejected session recovered/mutated the disposed UI or escaped async void.", context.Errors.FirstOrDefault());
            release.Set(); await blocked; await store.DisposeAsync();
            if (shutdown.IsCompleted) throw new InvalidOperationException("Shutdown did not retain the retired recorder.");
            recorder.Closing.SetResult(); startup.SetResult(lateResource); await lateResource.Disposing.Task;
            if (shutdown.IsCompleted) throw new InvalidOperationException("Shutdown skipped disposal admitted by a late native startup.");
            lateResource.Closing.SetResult();
            await Task.WhenAll(accountClosing, shutdown).WaitAsync(TimeSpan.FromSeconds(10));
            if (fadedPresentation.IsCompleted) throw new InvalidOperationException("Held UI context unexpectedly pumped during shutdown.");
            try
            {
                using var pixels = fadedBitmap.LockForWrite();
                throw new InvalidOperationException("Viewer pixels were retained behind a stopped UI continuation.");
            }
            catch (ObjectDisposedException) { }
            heldUi.ExecutePending();
            await fadedPresentation.WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("CHAT_SHUTDOWN_OWNERSHIP_OK: account reset and final shutdown drain SQLite, retired recorder and late native starts; disposed rejected-session recovery is harmless");
            Console.WriteLine("CHAT_CLOSE_NO_UI_PUMP_OK: a pending viewer fade does not block native/pixel retirement after immediate disposal");
        }
        finally
        {
            release.Set(); startup.TrySetResult(lateResource); rejection.TrySetResult(); replacedSessionReply.TrySetResult();
            recorder.Closing.TrySetResult(); lateResource.Closing.TrySetResult();
            await blocked; await store.DisposeAsync(); await chatCleanup.DrainAsync();
        }
    }
}
