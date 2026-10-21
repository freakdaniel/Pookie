using Pookie.App.Browser;
using Pookie.Audio;
using Pookie.SoundCloud;

namespace Pookie.App.Playback;

// SoundFlow handles ordinary audio; Windows protected audio stays inside browser EME.
internal sealed class WindowsAudioPlayer(IAudioPlayer native, Func<IBrowserAudioSession?> session) : IAudioPlayer
{
    private readonly object sync = new();
    private IBrowserAudioSession? browser;
    private string? playbackId;
    private AudioState state = new(0, 0, false, false, false);
    private Exception? error;
    private bool endedReported;
    private bool disposed;
    private double volume = 70;
    private CancellationTokenSource? preparing;
    private readonly List<Task> controls = [];
    private Pookie.Recommendations.Audio.MonoAudioSampler? analysis;
    private int analysisOffset;
    private bool analysisCaptureEnabled = true;

    public async Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Transport != AudioTransport.WidevineHls)
        {
            await native.PlayAsync(source, cancellationToken);
            return;
        }
        var connected = session() ?? throw new InvalidOperationException("Для защищённого воспроизведения нужна активная сессия SoundCloud.");
        var id = Guid.NewGuid().ToString("N");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        lock (sync)
        {
            preparing = timeout;
            browser = connected; playbackId = id; error = null; endedReported = false;
            state = new(0, source.Duration, false, true, false);
            analysis = source.CaptureAnalysis && analysisCaptureEnabled ? new(16000, 1) : null; analysisOffset = 0;
            connected.Changed += Received;
        }
        try
        {
            await connected.SendAudioAsync(new("start", id, source.Location, source.LicenseAuthToken, Volume: volume,
                NormalizationGainDb: source.NormalizationGainDb, CaptureAnalysis: source.CaptureAnalysis && analysisCaptureEnabled, PageUrl: source.BrowserPage), timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            lock (sync) if (playbackId != id) throw new OperationCanceledException();
        }
        catch
        {
            Exception? failure;
            lock (sync) failure = playbackId == id ? error : null;
            lock (sync) if (playbackId == id) Stop();
            if (failure != null) throw failure;
            throw;
        }
        finally { lock (sync) if (preparing == timeout) preparing = null; }
    }

    private void Received(BrowserRequestEvent message)
    {
        lock (sync)
        {
            if (playbackId == null) return;
            if (message.Kind == "audio-sample" && message.RequestId == playbackId && analysis != null &&
                message.Sample is { } sample && sample.IsValid() && sample.Offset == analysisOffset)
            {
                var bytes = Convert.FromBase64String(sample.Data);
                var values = new float[bytes.Length / 4];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
                    if (!float.IsFinite(values[i])) { analysis = null; return; }
                }
                analysis.Append(values); analysisOffset += values.Length;
            }
            if (message.Kind == "audio-closed")
                error = new InvalidOperationException("Браузерный проигрыватель закрыт. Включи трек ещё раз.");
            else if (message.Kind == "audio-state" && message.RequestId == playbackId && message.Audio is { } audio && audio.IsValid())
            {
                state = new(audio.Position, audio.Duration, audio.Playing, audio.Buffering, audio.Ended)
                    { BufferedStart = audio.BufferedStart, BufferedEnd = audio.BufferedEnd,
                      NormalizationGainDb = audio.NormalizationGainDb };
                if (audio.Error != null) error = PlaybackError(audio.Error);
            }
        }
    }

    private static InvalidOperationException PlaybackError(string code) => new(code switch
    {
        "license" or "expired" => "SoundCloud отклонил или завершил браузерную DRM-лицензию.",
        "unsupported" => "Не удалось подготовить Widevine CDM в браузере Pookie. Проверь подключение к сети и перезапусти приложение.",
        "network" => "Не удалось загрузить защищённый аудиопоток. Проверь соединение.",
        "autoplay" => "WebView2 не разрешил запуск звука в приложении.",
        "playlist" => "SoundCloud вернул неподдерживаемый защищённый плейлист.",
        _ => "Браузерный проигрыватель не смог декодировать аудиопоток."
    });

    public Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        lock (sync)
            return playbackId is { } id && browser is { } connected
                ? SendAsync(connected, new("seek", id, Position: Math.Clamp(seconds, 0, Math.Max(0, state.Duration - .01))), cancellationToken)
                : native.SeekAsync(seconds, cancellationToken);
    }

    public void Pause(bool paused)
    {
        lock (sync)
        {
            if (playbackId is { } id && browser is { } connected)
            {
                if (paused) state = state with { Playing = false, Buffering = false };
                Queue(connected, new("pause", id, Paused: paused));
            }
            else native.Pause(paused);
        }
    }

    public void Volume(double percent)
    {
        if (!double.IsFinite(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
        lock (sync)
        {
            volume = Math.Clamp(percent, 0, 100);
            native.Volume(volume);
            if (playbackId is { } id && browser is { } connected) Queue(connected, new("volume", id, Volume: volume));
        }
    }

    public AudioState Poll()
    {
        lock (sync)
        {
            if (playbackId == null) return native.Poll();
            if (error != null) throw error;
            var ended = state.Ended && !endedReported;
            endedReported |= state.Ended;
            return state with { Ended = ended };
        }
    }

    public void Stop()
    {
        ClearBrowser();
        native.Stop();
    }
    public Pookie.Recommendations.Audio.AudioSample? TakeAnalysisSample()
    {
        lock (sync) return playbackId == null ? native.TakeAnalysisSample() : analysis?.Take();
    }

    public void SetAnalysisCaptureEnabled(bool enabled)
    {
        lock (sync)
        {
            analysisCaptureEnabled = enabled;
            native.SetAnalysisCaptureEnabled(enabled);
            if (enabled) return;
            analysis = null; analysisOffset = 0;
            if (playbackId is { } id && browser is { } connected)
                Queue(connected, new("analysis", id, CaptureAnalysis: false));
        }
    }

    public Task StopAsync()
    {
        ClearBrowser();
        return native.StopAsync();
    }

    private void ClearBrowser()
    {
        lock (sync)
        {
            preparing?.Cancel();
            if (browser is { } connected)
            {
                connected.Changed -= Received;
                if (playbackId is { } id) Queue(connected, new("stop", id));
            }
            browser = null; playbackId = null; error = null; endedReported = false;
            analysis = null; analysisOffset = 0;
            state = new(0, 0, false, false, false);
        }
    }

    private void Queue(IBrowserAudioSession connected, BrowserAudioCommand command)
    {
        controls.RemoveAll(task => task.IsCompleted);
        controls.Add(SendControlAsync(connected, command));
    }

    private async Task SendControlAsync(IBrowserAudioSession connected, BrowserAudioCommand command)
    {
        try { await SendAsync(connected, command, CancellationToken.None); }
        catch (Exception failure)
        {
            lock (sync) if (playbackId == command.PlaybackId) error = failure;
        }
    }

    private static async Task SendAsync(IBrowserAudioSession connected, BrowserAudioCommand command, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await connected.SendAudioAsync(command, timeout.Token);
    }

    public void Dispose() { if (disposed) return; disposed = true; Stop(); native.Dispose(); }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task[] pending; lock (sync) pending = controls.ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false);
        await native.DisposeAsync().ConfigureAwait(false);
    }
}
