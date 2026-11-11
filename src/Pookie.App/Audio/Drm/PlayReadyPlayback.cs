using System.Threading.Channels;
using Pookie.Logging;

namespace Pookie.Audio;

internal sealed class PlayReadyPlayback
{
    private enum ControlKind { Volume, Pause, Seek }
    private readonly record struct Control(ControlKind Kind, double Value, int Revision = 0, CancellationToken Cancellation = default);
    private readonly Channel<Control> controls = Channel.CreateUnbounded<Control>(new() { SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object seekGate = new();
    private (int Revision, TaskCompletionSource Completion)? seeking;
    private int revision;
    private Exception? failure;
    private AudioState state;
    private readonly double gain;
    private int endedReported;
    private bool stopped;

    internal Task Ready => ready.Task;
    internal Task Completion { get; }

    public PlayReadyPlayback(HttpClient http, AudioSource source, Func<Uri, bool> allowed, string store, double volume)
    {
        state = new(0, source.Duration, false, true, false);
        gain = source.NormalizationGainDb;
        Volume(volume);
        // Even a synchronously completed HTTP read must not prepare HLS on the caller's UI thread.
        Completion = Task.Run(() => RunAsync(http, source, allowed, store));
    }

    internal void Volume(double percent) => controls.Writer.TryWrite(new(ControlKind.Volume, Math.Clamp(percent / 100 * Math.Pow(10, gain / 20), 0, 4)));
    internal void Pause(bool paused) => controls.Writer.TryWrite(new(ControlKind.Pause, paused ? 1 : 0));

    internal async Task SeekAsync(double position, CancellationToken token)
    {
        await Ready.WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (seekGate)
        {
            if (failure != null) throw failure;
            if (stopped) throw new OperationCanceledException();
            seeking?.Completion.TrySetCanceled();
            var next = ++revision;
            seeking = (next, completion);
            var duration = Volatile.Read(ref state).Duration;
            controls.Writer.TryWrite(new(ControlKind.Seek, Math.Clamp(position, 0, Math.Max(0, duration - .01)), next, token));
        }
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
    }

    internal AudioState Poll()
    {
        if (Volatile.Read(ref failure) is { } error) throw error;
        var current = Volatile.Read(ref state);
        var ended = current.Ended && Interlocked.Exchange(ref endedReported, 1) == 0;
        return current with { Ended = ended };
    }

    internal void Stop()
    {
        lock (seekGate)
        {
            if (stopped) return;
            stopped = true;
            lifetime.Cancel();
        }
    }

    private async Task RunAsync(HttpClient http, AudioSource source, Func<Uri, bool> allowed, string store)
    {
        var stop = lifetime.Token;
        try
        {
#if WINDOWS
            if (!Uri.TryCreate(source.Location, UriKind.Absolute, out var uri) || !allowed(uri))
                throw new DrmPlaybackException("Неверный адрес защищённого аудиопотока.");
            var authorization = source.LicenseAuthToken ?? throw new DrmPlaybackException("SoundCloud не предоставил авторизацию PlayReady.");
            await using var media = await PlayReadyHlsStream.OpenAsync(http, uri, allowed, stop).ConfigureAwait(false);
            media.Prefetch(0, stop);
            await PlayReadyMediaEngine.RunAsync(http, media, authorization, Directory.CreateDirectory(store).FullName,
                engine => RunControls(engine, media, stop), stop).ConfigureAwait(false);
#else
            await Task.FromException(new PlatformNotSupportedException("PlayReady playback requires Windows.")).ConfigureAwait(false);
#endif
        }
        catch (Exception error) when (stop.IsCancellationRequested)
        {
            AppLog.Failure("Pookie.Audio.PlayReady", "Воспроизведение отменено", error, Serilog.Events.LogEventLevel.Debug);
        }
        catch (Exception error)
        {
            Volatile.Write(ref failure, error);
            ready.TrySetException(error);
            lock (seekGate) seeking?.Completion.TrySetException(error);
        }
        finally
        {
            controls.Writer.TryComplete();
            ready.TrySetCanceled();
            lock (seekGate)
            {
                stopped = true;
                seeking?.Completion.TrySetCanceled();
                lifetime.Dispose();
            }
        }
    }

#if WINDOWS
    private void RunControls(PlayReadyMediaEngine engine, PlayReadyHlsStream media, CancellationToken stop)
    {
        engine.Volume(0);
        engine.Pause(false);
        var paused = false;
        var appliedRevision = 0;
        double? seekTarget = null;
        Task<bool>? wake = null;
        do
        {
            stop.ThrowIfCancellationRequested();
            if (engine.Failure is { } error) throw error;
            while (controls.Reader.TryRead(out var command))
            {
                if (command.Cancellation.IsCancellationRequested) continue;
                switch (command.Kind)
                {
                    case ControlKind.Volume:
                        engine.Volume(command.Value);
                        break;
                    case ControlKind.Pause:
                        paused = command.Value != 0;
                        engine.Pause(paused);
                        break;
                    case ControlKind.Seek:
                        media.PrefetchSeek(engine.Position, command.Value, stop);
                        engine.Seek(command.Value);
                        appliedRevision = command.Revision;
                        seekTarget = command.Value;
                        break;
                }
            }

            var position = engine.Position;
            if (!double.IsFinite(position)) throw new InvalidDataException("Invalid native playback position.");
            position = Math.Clamp(position, 0, media.Duration);
            var seekingNow = engine.Seeking;
            if (seekTarget is { } target && !seekingNow && Math.Abs(position - target) < .75) seekTarget = null;
            var hasAudio = engine.HasAudio;
            var readyState = engine.ReadyState;
            var ended = engine.Ended;
            const ushort haveCurrentData = 2, haveFutureData = 3;
            var buffering = !paused && !ended && (!hasAudio || readyState < haveFutureData || seekTarget.HasValue || seekingNow);
            var playing = !buffering && !paused && !ended && !engine.Paused;
            if (playing && position > 1) engine.ReportVolumeSupport();
            var buffered = media.BufferedAt(position);
            if (!ended) Volatile.Write(ref endedReported, 0);
            Volatile.Write(ref state, new(position, media.Duration, playing, buffering, ended)
            {
                BufferedStart = buffered.Start, BufferedEnd = buffered.End, NormalizationGainDb = gain
            });
            if (!ended) media.Prefetch(position, stop);
            if (hasAudio && readyState >= haveCurrentData) ready.TrySetResult();
            if (seekTarget == null)
            {
                lock (seekGate)
                {
                    if (seeking is { } pending && pending.Revision == appliedRevision)
                    {
                        pending.Completion.TrySetResult();
                        seeking = null;
                    }
                }
            }
            wake ??= controls.Reader.WaitToReadAsync(stop).AsTask();
            if (wake.Wait(seekTarget.HasValue ? 15 : 50, stop)) wake = null;
        } while (!stop.IsCancellationRequested);
    }
#endif
}
