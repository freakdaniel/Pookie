using Pookie.Audio;

namespace Pookie.App.Playback;

internal sealed class WindowsPlayReadyAudioPlayer(IAudioPlayer native, HttpClient http, Func<Uri, bool> allowed, string store) : IAudioPlayer
{
    private readonly object gate = new();
    private PlayReadyPlayback? active;
    private Task closing = Task.CompletedTask;
    private double volume = 70;
    private bool disposed;
    private long generation;
    internal bool HasProtectedSession { get { lock (gate) return active != null; } }

    public async Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default)
    {
        long selection;
        Task pending;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            selection = ++generation;
            pending = StopCoreAsync();
        }
        await pending.ConfigureAwait(false);
        PlayReadyPlayback? playback = null;
        Task start;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (selection != generation) throw new OperationCanceledException();
            if (source.Transport == AudioTransport.WidevineHls)
            {
                if (!double.IsFinite(source.NormalizationGainDb) || source.NormalizationGainDb is < -24 or > 6)
                    throw new ArgumentOutOfRangeException(nameof(source));
                active = playback = new(http, source, allowed, store, volume);
                start = playback.Ready.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
            }
            else start = native.PlayAsync(source, cancellationToken);
        }
        try { await start.ConfigureAwait(false); }
        catch when (playback != null)
        {
            lock (gate) if (ReferenceEquals(active, playback)) CloseActive();
            await playback.Completion.ConfigureAwait(false);
            throw;
        }
    }

    public Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        lock (gate) return active?.SeekAsync(seconds, cancellationToken) ?? native.SeekAsync(seconds, cancellationToken);
    }

    public void Pause(bool paused)
    {
        lock (gate)
        {
            if (active is { } playback) playback.Pause(paused);
            else native.Pause(paused);
        }
    }

    public void Volume(double percent)
    {
        if (!double.IsFinite(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
        lock (gate)
        {
            volume = Math.Clamp(percent, 0, 100);
            native.Volume(volume);
            active?.Volume(volume);
        }
    }

    public AudioState Poll()
    {
        lock (gate) return active?.Poll() ?? native.Poll();
    }

    public void Stop()
    {
        lock (gate)
        {
            generation++;
            CloseActive();
            native.Stop();
        }
    }

    public Task StopAsync()
    {
        lock (gate) { generation++; return StopCoreAsync(); }
    }

    private Task StopCoreAsync()
    {
        CloseActive();
        return Task.WhenAll(native.StopAsync(), closing);
    }

    private void CloseActive()
    {
        if (active is not { } playback) return;
        active = null;
        playback.Stop();
        closing = closing.IsCompletedSuccessfully ? playback.Completion : Task.WhenAll(closing, playback.Completion);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            CloseActive();
            native.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await closing.ConfigureAwait(false);
        await native.DisposeAsync().ConfigureAwait(false);
    }
}
