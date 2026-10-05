using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;

namespace Pookie.Audio;

public sealed class SoundFlowPlayer : IAudioPlayer
{
    private readonly MiniAudioEngine engine;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly Func<Uri, bool> allowed;
    private readonly SemaphoreSlim operations = new(1);
    private readonly List<Task> decoding = [];
    private AudioPlaybackDevice? device;
    private SoundPlayer? player;
    private StreamingProvider? provider;
    private CancellationTokenSource? pending;
    private AudioSource? source;
    private long generation;
    private float volume = .7f;
    private bool paused;
    private bool endedReported;
    private bool disposed;
    private Task? release;

    public SoundFlowPlayer(bool silent = false, Func<Uri, bool>? allowedUri = null)
    {
        allowed = allowedUri ?? (uri => uri.UserInfo.Length == 0 && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)));
        // SoundFlow 1.4.1's Null enum is 0 (native WASAPI); the bundled miniaudio ABI uses 14.
        // Leave normal backend probing to miniaudio; use the correct ABI value for headless checks.
        const MiniAudioBackend nativeNull = (MiniAudioBackend)14;
        engine = new MiniAudioEngine(silent ? [nativeNull] : null);
        if (!silent && engine.ActiveBackend == nativeNull)
        {
            engine.Dispose(); http.Dispose();
            throw new InvalidOperationException("Не найден доступный аудиосервис. Проверь устройство вывода звука в системе.");
        }
    }

    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default) => StartAsync(source, 0, false, cancellationToken);

    private async Task StartAsync(AudioSource target, double position, bool startPaused, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var request = Interlocked.Increment(ref generation);
        pending?.Cancel();
        await operations.WaitAsync(token);
        StreamingProvider? next = null;
        try
        {
            if (request != generation) throw new OperationCanceledException();
            StopCore();
            source = target;
            paused = startPaused;
            pending = CancellationTokenSource.CreateLinkedTokenSource(token);
            var operationToken = pending.Token;
            next = new StreamingProvider(http, target, position, allowed, operationToken);
            decoding.RemoveAll(task => task.IsCompleted);
            decoding.Add(next.Completion);
            var format = await next.Ready.WaitAsync(operationToken);
            operationToken.ThrowIfCancellationRequested();
            if (request != generation || disposed) throw new OperationCanceledException();
            provider = next;
            source = target;
            endedReported = false;
            device = engine.InitializePlaybackDevice(null, format);
            player = new SoundPlayer(engine, format, provider) { Volume = volume };
            device.MasterMixer.AddComponent(player);
            if (!paused) player.Play();
            device.Start();
        }
        catch { next?.Dispose(); StopCore(); throw; }
        finally { operations.Release(); }
    }

    public Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (source == null) return Task.CompletedTask;
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        var duration = provider?.Duration ?? source.Duration;
        var position = Math.Max(0, duration > 0 ? Math.Min(seconds, Math.Max(0, duration - .01)) : seconds);
        return StartAsync(source, position, paused, cancellationToken);
    }

    public void Pause(bool value)
    {
        paused = value;
        if (value) player?.Pause(); else player?.Play();
    }
    public void Volume(double percent)
    {
        if (!double.IsFinite(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
        volume = (float)Math.Clamp(percent / 100, 0, 1);
        if (player != null) player.Volume = volume;
    }
    public AudioState Poll()
    {
        if (provider?.Error is { } error) throw error;
        var ended = provider?.Ended == true;
        var notifyEnd = ended && !endedReported;
        if (ended) { endedReported = true; player?.Pause(); }
        return new(provider?.Seconds ?? 0, provider?.Duration ?? 0, provider != null && !paused && !ended && !provider.Buffering,
            provider?.Buffering == true && !paused, notifyEnd);
    }
    public void Stop() { Interlocked.Increment(ref generation); pending?.Cancel(); StopCore(); }
    private void StopCore()
    {
        // Stop the callback before disposing the provider. Decoders finish cancellation on their worker.
        device?.Stop();
        player?.Pause();
        if (player != null) device?.MasterMixer.RemoveComponent(player);
        player?.Dispose(); provider?.Dispose(); device?.Dispose();
        player = null; provider = null; device = null; source = null;
        pending?.Cancel(); pending?.Dispose(); pending = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Stop();
        // Never destroy native decoder factories while a cancelled worker is still using them.
        release = ReleaseAsync();
    }
    public async ValueTask DisposeAsync() { Dispose(); await release!.ConfigureAwait(false); }
    private async Task ReleaseAsync()
    {
        await operations.WaitAsync();
        try { await Task.WhenAll(decoding); engine.Dispose(); http.Dispose(); }
        finally { operations.Release(); }
    }
}
