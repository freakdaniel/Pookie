using System.Buffers.Binary;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Pookie.App.Messages;

internal sealed record ChatVoiceRecording(byte[] Data, string Extension, string MediaType, ChatVoiceMetadata Metadata);
internal sealed record ChatVoiceMeter(long FirstFrame, float[] Peaks);
internal interface IChatVoiceRecorder : IAsyncDisposable
{
    bool Recording { get; }
    bool Paused { get; }
    double Duration { get; }
    byte[] Peaks { get; }
    ChatVoiceMeter Meter { get; }
    Task StartAsync(CancellationToken token = default);
    void Pause();
    Task ResumeAsync(CancellationToken token = default);
    Task<ChatVoiceRecording> FinishAsync(CancellationToken token = default);
    Task CancelAsync();
}
/// <summary>Capture callback only copies into bounded PCM memory. Encoding and file IO run on workers.</summary>
internal sealed class ChatVoiceRecorder : IChatVoiceRecorder
{
    private readonly ChatVoicePcm pcm = new();
    internal ChatVoiceRecorder(float[]? seed = null) { if (seed != null) pcm.Append(seed); }
    private MiniAudioEngine? engine;
    private AudioCaptureDevice? device;
    private Recorder? recorder;
    private readonly SemaphoreSlim operations = new(1, 1);
    private volatile bool recording, paused;
    internal static AudioFormat Format => new() { SampleRate = 16000, Channels = 1, Layout = ChannelLayout.Mono, Format = SampleFormat.F32 };
    public bool Recording => recording;
    public bool Paused => paused;
    public double Duration => pcm.Duration;
    public byte[] Peaks => pcm.Peaks();
    public ChatVoiceMeter Meter => pcm.Meter();
    public Task StartAsync(CancellationToken token = default) => ResumeAsync(token);
    public async Task ResumeAsync(CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (pcm.Duration >= 300) throw new InvalidOperationException("Максимум 5 минут.");
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (recorder != null) { if (!recorder.ResumeRecording().IsSuccess) throw new InvalidOperationException("Не удалось продолжить запись."); paused = false; recording = true; return; }
                try
                {
                    pcm.Reserve();
                    engine = new MiniAudioEngine();
                    if (engine.CaptureDevices.Length == 0) throw new InvalidOperationException("Микрофон недоступен.");
                    device = engine.InitializeCaptureDevice(null, Format);
                    recorder = new Recorder(device, (samples, _) => pcm.Append(samples));
                    if (!recorder.StartRecording().IsSuccess) throw new InvalidOperationException("Не удалось включить запись."); device.Start(); paused = false; recording = true;
                }
                catch { device?.Dispose(); device = null; engine?.Dispose(); engine = null; recorder = null; throw; }
            }, token).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }
    public void Pause() { if (recorder != null && !recorder.PauseRecording().IsSuccess) throw new InvalidOperationException("Не удалось приостановить запись."); paused = true; }
    public async Task<ChatVoiceRecording> FinishAsync(CancellationToken token = default)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            return await Task.Run(() => { var samples = pcm.Snapshot(); try { return ChatVoicePcm.Encode(samples); } finally { Array.Clear(samples); } }, token).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }
    private async Task StopCoreAsync()
    {
        if (recorder != null) await recorder.StopRecordingAsync().ConfigureAwait(false);
        await Task.Run(() => { device?.Stop(); device?.Dispose(); device = null; engine?.Dispose(); engine = null; recorder = null; recording = false; paused = false; }).ConfigureAwait(false);
    }
    public async Task CancelAsync() { await operations.WaitAsync().ConfigureAwait(false); try { await StopCoreAsync().ConfigureAwait(false); pcm.Clear(); } finally { operations.Release(); } }
    public async ValueTask DisposeAsync() => await CancelAsync().ConfigureAwait(false);
}
internal sealed class ChatVoicePcm
{
    internal const int Rate = 16000, MaximumSamples = Rate * 300;
    private float[]? samples;
    private int count;
    internal const int MeterSamples = Rate / 25, MeterCapacity = 128;
    private readonly float[] meter = new float[MeterCapacity];
    private long meterFrames;
    private int meterSamples;
    private float meterPeak;
    private readonly object sync = new();
    internal double Duration { get { lock (sync) return count / (double)Rate; } }
    internal void Reserve() { lock (sync) samples ??= new float[MaximumSamples]; }
    internal void Append(ReadOnlySpan<float> input)
    {
        lock (sync)
        {
            samples ??= new float[MaximumSamples];
            var length = Math.Min(input.Length, MaximumSamples - count);
            input[..length].CopyTo(samples.AsSpan(count)); count += length;
            // Analyse each accepted sample once. The UI copies only a small rolling
            // window; it never rescans minutes of PCM while holding this lock.
            foreach (var sample in input[..length])
            {
                if (float.IsFinite(sample)) meterPeak = Math.Max(meterPeak, Math.Min(1, Math.Abs(sample)));
                if (++meterSamples != MeterSamples) continue;
                meter[meterFrames++ % MeterCapacity] = meterPeak;
                meterSamples = 0; meterPeak = 0;
            }
        }
    }
    internal float[] Snapshot() { lock (sync) return samples == null ? [] : samples.AsSpan(0, count).ToArray(); }
    internal byte[] Peaks() { lock (sync) return samples == null ? [] : Wave(samples.AsSpan(0, count)); }
    internal ChatVoiceMeter Meter()
    {
        lock (sync)
        {
            var length = (int)Math.Min(meterFrames, MeterCapacity);
            var first = meterFrames - length;
            var result = new float[length];
            for (var i = 0; i < length; i++) result[i] = meter[(first + i) % MeterCapacity];
            return new(first, result);
        }
    }
    internal void Clear()
    {
        lock (sync)
        {
            if (samples != null) Array.Clear(samples);
            count = 0; samples = null; Array.Clear(meter); meterFrames = 0; meterSamples = 0; meterPeak = 0;
        }
    }
    internal static byte[] Wave(ReadOnlySpan<float> data)
    {
        if (data.Length == 0) return [];
        var output = new byte[Math.Min(64, data.Length)];
        Span<float> levels = stackalloc float[output.Length];
        var maximum = .001f;
        for (var i = 0; i < output.Length; i++)
        {
            var peak = 0f;
            for (var j = i * data.Length / output.Length; j < (i + 1) * data.Length / output.Length; j++)
                if (float.IsFinite(data[j])) peak = Math.Max(peak, Math.Min(1, Math.Abs(data[j])));
            levels[i] = peak; maximum = Math.Max(maximum,peak);
        }
        for (var i = 0; i < output.Length; i++) output[i] = (byte)Math.Round(levels[i] / maximum * 255);
        return output;
    }
    private static readonly float[][] opusPhases = [OpusPhase(1d/3), OpusPhase(2d/3)];
    private const int OpusRadius = 12;
    private static float[] OpusPhase(double phase)
    {
        var weights = new float[OpusRadius * 2]; double total = 0;
        for (var tap = 0; tap < weights.Length; tap++)
        {
            var distance = tap - OpusRadius + 1 - phase;
            var weight = Math.Sin(Math.PI*distance)/(Math.PI*distance) * (.5 + .5*Math.Cos(Math.PI*distance/OpusRadius));
            weights[tap] = (float)weight; total += weight;
        }
        for (var tap = 0; tap < weights.Length; tap++) weights[tap] /= (float)total;
        return weights;
    }
    // The bundled FFmpeg native Opus encoder accepts 48 kHz. Keep capture and
    // WAV fallback at 16 kHz, and convert small blocks only on the encoding worker.
    internal static int ResampleOpus(ReadOnlySpan<float> input, int offset, Span<float> output)
    {
        var length = Math.Min(input.Length-offset,output.Length/3);
        for (var i = 0; i < length; i++)
        {
            var source = offset+i;
            output[i*3] = float.IsFinite(input[source]) ? Math.Clamp(input[source],-1,1) : 0;
            for (var phase = 0; phase < 2; phase++)
            {
                double value = 0;
                for (var tap = 0; tap < OpusRadius*2; tap++)
                {
                    var sample = input[Math.Clamp(source+tap-OpusRadius+1,0,input.Length-1)];
                    if (float.IsFinite(sample)) value += sample*opusPhases[phase][tap];
                }
                output[i*3+phase+1] = (float)Math.Clamp(value,-1,1);
            }
        }
        return length*3;
    }
    internal static float[] Decode(byte[] bytes)
    {
        if (bytes.Length >= 44 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) &&
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)) == Rate && BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)) == 1 &&
            BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)) == 16 && bytes.AsSpan(36, 4).SequenceEqual("data"u8))
        {
            var count = Math.Min(MaximumSamples, (bytes.Length - 44) / 2); var pcm = new float[count];
            for (var i = 0; i < count; i++) pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + i * 2)) / 32768f;
            return pcm;
        }
        using var stream = new MemoryStream(bytes, false);
        using var decoder = new FFmpegCodecFactory().TryCreateDecoder(stream, out var format, ChatVoiceRecorder.Format) ?? throw new InvalidDataException("Не удалось продолжить запись.");
        if (format.Channels is < 1 or > 2 || format.SampleRate is < 8000 or > 192000) throw new InvalidDataException("Неизвестный формат голосового черновика.");
        var result = new List<float>(); var buffer = new float[8192 * format.Channels]; long frame = 0; double next = 0; int read;
        while (result.Count < MaximumSamples && (read = decoder.Decode(buffer)) > 0)
            for (var i = 0; i + format.Channels <= read && result.Count < MaximumSamples; i += format.Channels, frame++)
            {
                if (frame < next) continue; float mono = 0; for (var c = 0; c < format.Channels; c++) mono += buffer[i + c] / format.Channels;
                result.Add(mono); next += format.SampleRate / (double)Rate;
            }
        Array.Clear(buffer); return result.ToArray();
    }
    internal static ChatVoiceRecording Encode(float[] samples, bool preferOpus = true)
    {
        if (samples.Length is 0 or > MaximumSamples) throw new InvalidDataException("Запись должна длиться от одного сэмпла до 5 минут.");
        var metadata = new ChatVoiceMetadata(samples.Length / (double)Rate, Wave(samples));
        if (preferOpus)
        {
            try
            {
                using var stream = new MemoryStream();
                var format = ChatVoiceRecorder.Format; format.SampleRate = 48000;
                var converted = new float[4096*3];
                try
                {
                    using var encoder = new FFmpegCodecFactory().CreateEncoder(stream, "opus", format) ?? throw new NotSupportedException();
                    for (var offset = 0; offset < samples.Length; offset += converted.Length/3)
                        encoder.Encode(converted.AsSpan(0,ResampleOpus(samples,offset,converted)));
                }
                finally { Array.Clear(converted); }
                var bytes = stream.ToArray();
                if (bytes.Length > 0 && bytes.Length <= ChatMediaCrypto.MaxFileBytes) return new(bytes, ".ogg", "audio/ogg", metadata);
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) {/* A codec is optional. */}
        }
        var wav = new byte[44 + samples.Length * 2];
        "RIFF"u8.CopyTo(wav); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8); "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), Rate); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), Rate * 2); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), samples.Length * 2);
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), (short)Math.Round(Math.Clamp(float.IsFinite(samples[i]) ? samples[i] : 0, -1, 1) * 32767));
        return new(wav, ".wav", "audio/wav", metadata);
    }
}
