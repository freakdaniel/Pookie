using System.Threading.Channels;
using Pookie.Recommendations.Audio;
using Microsoft.ML.OnnxRuntime;

namespace Pookie.Recommendations.Storage;

public sealed class ListeningJournal : IAsyncDisposable
{
    private abstract record Work;
    private sealed record Write(WaveEvent Event, AudioSample? Sample, CancellationToken Token, TaskCompletionSource Completion) : Work;
    private sealed record ReadSnapshot(long Account, WaveTrack[] Tracks, TaskCompletionSource<RecommendationSnapshot> Completion) : Work;
    private sealed record Analyze(long Account, WaveTrack Track, AudioSample Sample, CancellationToken Token, TaskCompletionSource<bool> Completion) : Work;
    private sealed record Maintenance(Action<ListeningEventStore> Apply, TaskCompletionSource Completion) : Work;
    private readonly Channel<Work> writes;
    private readonly Task worker;
    private int pendingAudio;
    private readonly bool analyzeMoods;
    public Task Completion => worker;
    public event Action<Exception>? AnalysisFailed;

    public ListeningJournal(string path, int capacity = 256, bool analyzeMoods = false, JournalPolicy? policy = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.analyzeMoods = analyzeMoods;
        writes = Channel.CreateBounded<Work>(new BoundedChannelOptions(capacity) {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        worker = Task.Run(() => ProcessAsync(path, policy));
    }

    // Successful completion means the transaction committed. A cancellation can
    // discard queued implicit observations before commit; default-token writes
    // still drain on shutdown. Retrying the same event is idempotent.
    public Task AppendAsync(WaveEvent item, CancellationToken token = default) => AppendAsync(item, null, token);
    public async Task AppendAsync(WaveEvent item, AudioSample? sample, CancellationToken token = default)
    {
        item.Validate();
        if (sample != null && Interlocked.Increment(ref pendingAudio) > 4)
        {
            Interlocked.Decrement(ref pendingAudio); sample = null;
        }
        var write = new Write(item, sample, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
        try { await writes.Writer.WriteAsync(write, token).ConfigureAwait(false); }
        catch
        {
            if (sample != null) Interlocked.Decrement(ref pendingAudio);
            throw;
        }
        sample = null;
        await write.Completion.Task.ConfigureAwait(false);
    }
    public async Task<RecommendationSnapshot> SnapshotAsync(long account, WaveTrack[] tracks, CancellationToken token = default)
    {
        if (account <= 0 || tracks.Length > 2000) throw new ArgumentOutOfRangeException(nameof(account));
        var read = new ReadSnapshot(account, tracks, new(TaskCreationOptions.RunContinuationsAsynchronously));
        await writes.Writer.WriteAsync(read, token).ConfigureAwait(false);
        return await read.Completion.Task.ConfigureAwait(false);
    }

    private bool HasAnalysis(AudioVector? vector) => vector?.Model == MusicnnEncoder.ModelId &&
        (!analyzeMoods || vector.Mood?.IsCurrent == true);

    // Analyze already decoded audio while it plays, without inventing a listening reward.
    public async Task<bool> AnalyzeAsync(long account, WaveTrack track, AudioSample sample, CancellationToken token = default)
    {
        if (account <= 0 || !WaveTaste.IsPlayable(track)) throw new ArgumentException("Invalid analysis track.");
        if (Interlocked.Increment(ref pendingAudio) > 4) { Interlocked.Decrement(ref pendingAudio); return false; }
        var request = new Analyze(account, track, sample, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
        try { await writes.Writer.WriteAsync(request, token).ConfigureAwait(false); }
        catch { Interlocked.Decrement(ref pendingAudio); throw; }
        return await request.Completion.Task.ConfigureAwait(false);
    }

    public Task SetRawHistoryDaysAsync(int days, CancellationToken token = default)
    {
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(days));
        return MaintainAsync(store => store.SetRawHistoryDays(days), token);
    }
    public Task ClearListeningHistoryAsync(long account, CancellationToken token = default)
    {
        if (account <= 0) throw new ArgumentOutOfRangeException(nameof(account));
        return MaintainAsync(store => store.ClearListeningHistory(account), token);
    }
    public Task ResetLearningAsync(long account, CancellationToken token = default)
    {
        if (account <= 0) throw new ArgumentOutOfRangeException(nameof(account));
        return MaintainAsync(store => store.ResetLearning(account), token);
    }
    private async Task MaintainAsync(Action<ListeningEventStore> apply, CancellationToken token)
    {
        var request = new Maintenance(apply, new(TaskCreationOptions.RunContinuationsAsynchronously));
        await writes.Writer.WriteAsync(request, token).ConfigureAwait(false);
        await request.Completion.Task.ConfigureAwait(false);
    }

    private async Task ProcessAsync(string path, JournalPolicy? policy)
    {
        var batch = new List<Write>(64);
        ReadSnapshot? activeRead = null;
        Analyze? activeAnalysis = null;
        Maintenance? activeMaintenance = null;
        try
        {
            using var store = new ListeningEventStore(path, policy: policy);
            while (await writes.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();
                if (writes.Reader.TryPeek(out var maintenanceWork) && maintenanceWork is Maintenance)
                {
                    writes.Reader.TryRead(out var request); activeMaintenance = (Maintenance)request!;
                    activeMaintenance.Apply(store); activeMaintenance.Completion.TrySetResult();
                    activeMaintenance = null; continue;
                }
                if (writes.Reader.TryPeek(out var analysisWork) && analysisWork is Analyze)
                {
                    writes.Reader.TryRead(out var request); activeAnalysis = (Analyze)request!;
                    try
                    {
                        activeAnalysis.Token.ThrowIfCancellationRequested();
                        if (!HasAnalysis(store.GetVector(activeAnalysis.Account, activeAnalysis.Track.Id)))
                        {
                            using var analysisEncoder = new MusicnnEncoder();
                            var vector = analysisEncoder.Encode(activeAnalysis.Sample, includeMood: analyzeMoods);
                            if (vector != null && !activeAnalysis.Token.IsCancellationRequested)
                                store.CacheAudio(activeAnalysis.Account, activeAnalysis.Track, vector);
                        }
                        if (activeAnalysis.Token.IsCancellationRequested) activeAnalysis.Completion.TrySetCanceled(activeAnalysis.Token);
                        else activeAnalysis.Completion.TrySetResult(HasAnalysis(store.GetVector(activeAnalysis.Account, activeAnalysis.Track.Id)));
                    }
                    catch (OperationCanceledException) when (activeAnalysis.Token.IsCancellationRequested)
                    { activeAnalysis.Completion.TrySetCanceled(activeAnalysis.Token); }
                    catch (Exception error) when (error is OnnxRuntimeException or ArgumentException or InvalidDataException or
                        DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
                    { AnalysisFailed?.Invoke(error); activeAnalysis.Completion.TrySetResult(false); }
                    finally { Interlocked.Decrement(ref pendingAudio); }
                    activeAnalysis = null; continue;
                }
                if (writes.Reader.TryPeek(out var next) && next is ReadSnapshot)
                {
                    writes.Reader.TryRead(out var request);
                    activeRead = (ReadSnapshot)request!;
                    activeRead.Completion.TrySetResult(store.Snapshot(activeRead.Account, activeRead.Tracks)); activeRead = null;
                    continue;
                }
                while (batch.Count < 64 && writes.Reader.TryPeek(out next) && next is Write)
                {
                    writes.Reader.TryRead(out var request); batch.Add((Write)request!);
                }
                var vectors = new Dictionary<Guid, AudioVector>();
                // Only this worker runs inference. Sessions are released after the batch; idle has no model resident.
                MusicnnEncoder? encoder = null;
                try
                {
                    foreach (var item in batch)
                    {
                        if (item.Sample == null) continue;
                        try
                        {
                            if (item.Token.IsCancellationRequested || item.Event.Track == null || HasAnalysis(store.GetVector(item.Event.AccountId, item.Event.Track.Id))) continue;
                            encoder ??= new();
                            var vector = encoder.Encode(item.Sample, includeMood: analyzeMoods);
                            if (vector != null) vectors[item.Event.EventId] = vector;
                        }
                        catch (Exception error) when (error is OnnxRuntimeException or ArgumentException or InvalidDataException or
                            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
                        { AnalysisFailed?.Invoke(error); }
                        finally { Interlocked.Decrement(ref pendingAudio); }
                    }
                }
                finally { encoder?.Dispose(); }
                var accepted = batch.Where(item => !item.Token.IsCancellationRequested).ToArray();
                if (accepted.Length > 0) store.AppendBatch(accepted.Select(item => item.Event).ToArray(), vectors);
                foreach (var item in batch)
                    if (accepted.Contains(item)) item.Completion.TrySetResult();
                    else item.Completion.TrySetCanceled(item.Token);
                batch.Clear(); // Do not retain the last batch's PCM while the worker is idle.
            }
        }
        catch (Exception error)
        {
            writes.Writer.TryComplete(error);
            activeRead?.Completion.TrySetException(error);
            activeAnalysis?.Completion.TrySetException(error);
            activeMaintenance?.Completion.TrySetException(error);
            foreach (var item in batch) item.Completion.TrySetException(error);
            while (writes.Reader.TryRead(out var pending))
                switch (pending)
                {
                    case Write write: write.Completion.TrySetException(error); break;
                    case ReadSnapshot read: read.Completion.TrySetException(error); break;
                    case Analyze analysis: analysis.Completion.TrySetException(error); break;
                    case Maintenance maintenance: maintenance.Completion.TrySetException(error); break;
                }
            throw;
        }
    }

    public void Complete() => writes.Writer.TryComplete();
    public async ValueTask DisposeAsync()
    {
        Complete();
        await worker.ConfigureAwait(false);
    }
}
