using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Channels;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;

namespace Pookie.Audio;

// All HTTP and decoding runs on a worker. The audio callback only drains a bounded PCM queue.
internal sealed class StreamingProvider : ISoundDataProvider
{
    private readonly Channel<float[]> chunks = Channel.CreateBounded<float[]>(new BoundedChannelOptions(32)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource cancellation;
    private readonly TaskCompletionSource<AudioFormat> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private float[]? chunk;
    private int chunkOffset;
    private long samplesPlayed;
    private long startingSample;
    private volatile bool buffering = true;
    private volatile bool ended;
    private Exception? error;
    public AudioFormat Format { get; private set; }
    public Task<AudioFormat> Ready => ready.Task;
    public Task Completion { get; }
    public double Duration { get; private set; }
    public double Seconds => Format.SampleRate == 0 ? 0 : (Interlocked.Read(ref samplesPlayed) + startingSample) / (double)(Format.SampleRate * Format.Channels);
    public bool Buffering => buffering;
    public bool Ended => ended;
    public Exception? Error => error;
    public int Position => (int)Math.Min(int.MaxValue, Interlocked.Read(ref samplesPlayed) + startingSample);
    // SoundPlayer must not mistake a temporary underrun for the end of a finite track.
    public int Length => -1;
    public bool CanSeek => false;
    public SampleFormat SampleFormat => SampleFormat.F32;
    public int SampleRate => Format.SampleRate;
    public bool IsDisposed { get; private set; }
    public SoundFormatInfo? FormatInfo => null;
    public event EventHandler<EventArgs>? EndOfStreamReached;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    public StreamingProvider(HttpClient http, AudioSource source, double offset, Func<Uri, bool> allowed, CancellationToken token)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Duration = source.Duration;
        Completion = Task.Run(async () =>
        {
            var stop = cancellation.Token;
            try
            {
                if (source.Transport == AudioTransport.WidevineHls)
                {
                    var uri = MediaUri(source.Location, allowed);
                    var manifest = await FetchAsync(http, uri, 1024 * 1024, stop);
                    var playlist = ProtectedHlsPlaylist.Parse(Encoding.UTF8.GetString(manifest), uri, allowed);
                    var init = CencAudioInitialization.Parse(await FetchAsync(http, playlist.Initialization, 1024 * 1024, stop));
                    using var session = await WidevineSession.OpenAsync(http, source.LicenseAuthToken ?? "", playlist.InitData, stop);
                    Duration = playlist.Duration;
                    foreach (var segment in playlist.Segments.Where(segment => segment.Start + segment.Duration > offset))
                    {
                        stop.ThrowIfCancellationRequested();
                        await session.RenewAsync(stop);
                        var encrypted = await FetchAsync(http, segment.Uri, 16 * 1024 * 1024, stop);
                        var audio = CencAudioFragment.DecryptAdts(encrypted, init, session, stop);
                        try
                        {
                            using var stream = new MemoryStream(audio, writable: false);
                            await DecodeAsync(stream, Math.Max(0, offset - segment.Start), offset, stop);
                        }
                        finally { CryptographicOperations.ZeroMemory(audio); }
                    }
                }
                else if (source.Transport == AudioTransport.Hls)
                {
                    var uri = MediaUri(source.Location, allowed);
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, stop);
                    response.EnsureSuccessStatusCode();
                    var bytes = await HttpRangeStream.ReadBoundedAsync(response.Content, 1024 * 1024, stop);
                    var playlist = HlsPlaylist.Parse(Encoding.UTF8.GetString(bytes), uri, allowed);
                    Duration = playlist.Duration;
                    foreach (var segment in playlist.Segments.Where(segment => segment.Start + segment.Duration > offset))
                    {
                        stop.ThrowIfCancellationRequested();
                        using var request = new HttpRequestMessage(HttpMethod.Get, segment.Uri);
                        if (segment.Length != null) request.Headers.Range = new RangeHeaderValue(segment.Offset, checked(segment.Offset!.Value + segment.Length.Value - 1));
                        using var media = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
                        media.EnsureSuccessStatusCode();
                        if (segment.Length != null && (media.StatusCode != HttpStatusCode.PartialContent
                            || media.Content.Headers.ContentRange?.From != segment.Offset
                            || media.Content.Headers.ContentRange?.To != segment.Offset + segment.Length - 1))
                            throw new IOException("Server did not honor the HLS byte range.");
                        var data = await HttpRangeStream.ReadBoundedAsync(media.Content, 16 * 1024 * 1024, stop);
                        if (segment.Length != null && data.Length != segment.Length) throw new IOException("Incomplete HLS segment.");
                        using var stream = new MemoryStream(data, writable: false);
                        await DecodeAsync(stream, Math.Max(0, offset - segment.Start), offset, stop);
                    }
                }
                else
                {
                    using var stream = source.Transport == AudioTransport.File ? File.OpenRead(source.Location)
                        : await HttpRangeStream.OpenAsync(http, MediaUri(source.Location, allowed), stop);
                    await DecodeAsync(stream, offset, offset, stop);
                }
                if (!ready.Task.IsCompleted) throw new InvalidOperationException("Аудиопоток пуст.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { ready.TrySetCanceled(stop); }
            catch (Exception failure)
            {
                error = failure is DrmPlaybackException ? failure : new InvalidOperationException("Не удалось загрузить или декодировать аудиопоток. Попробуй открыть трек ещё раз.", failure);
                ready.TrySetException(error);
            }
            finally { chunks.Writer.TryComplete(); }
        });
    }

    private static async Task<byte[]> FetchAsync(HttpClient http, Uri uri, int maximum, CancellationToken token)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new DrmPlaybackException($"CDN SoundCloud не предоставил защищённое аудио (HTTP {(int)response.StatusCode}).");
        return await HttpRangeStream.ReadBoundedAsync(response.Content, maximum, token);
    }

    private async Task DecodeAsync(Stream stream, double skipSeconds, double offset, CancellationToken token)
    {
        // FFmpeg 1.4.0 reports the source sample format, although Decode returns the requested F32.
        // It preserves source channels/rate, so configure the output device from these actual values.
        using var decoder = new FFmpegCodecFactory().TryCreateDecoder(stream, out var detected, AudioFormat.DvdHq)
            ?? throw new InvalidOperationException("Unsupported audio format.");
        var format = detected with { Format = SampleFormat.F32 };
        if (format.Channels is < 1 or > 8 || format.SampleRate <= 0)
            throw new InvalidOperationException($"Unsupported decoded audio format: {format.Format}, {format.Channels} channels, {format.SampleRate} Hz.");
        if (Format.SampleRate == 0)
        {
            Format = format;
            startingSample = (long)(offset * format.SampleRate) * format.Channels;
            if (Duration <= 0 && decoder.Length > 0) Duration = decoder.Length / (double)(format.SampleRate * format.Channels);
            ready.TrySetResult(format);
        }
        else if (Format.Channels != format.Channels || Format.SampleRate != format.SampleRate)
            throw new InvalidOperationException("HLS audio format changed between segments.");

        var skip = (long)(skipSeconds * format.SampleRate) * format.Channels;
        if (skip > 0 && skip <= int.MaxValue && decoder.Seek((int)skip)) skip = 0;
        var buffer = new float[8192];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = decoder.Decode(buffer);
            // FFmpeg's managed read callback turns an I/O failure into EOF; retain the real failure here.
            if (stream is HttpRangeStream { ReadError: { } rangeError }) throw rangeError;
            if (stream is HttpRangeStream.ResponseStream { ReadError: { } responseError }) throw responseError;
            if (count <= 0) break;
            var discard = (int)Math.Min(skip, count);
            skip -= discard;
            if (discard < count) await chunks.Writer.WriteAsync(buffer[discard..count], token);
        }
    }

    private static Uri MediaUri(string value, Func<Uri, bool> allowed) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && allowed(uri)
        ? uri : throw new InvalidOperationException("Недопустимый адрес аудиопотока.");

    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;
        var copied = 0;
        while (copied < buffer.Length)
        {
            if (chunk == null && !chunks.Reader.TryRead(out chunk)) break;
            var count = Math.Min(buffer.Length - copied, chunk!.Length - chunkOffset);
            chunk.AsSpan(chunkOffset, count).CopyTo(buffer[copied..]);
            copied += count; chunkOffset += count;
            if (chunkOffset == chunk.Length) { chunk = null; chunkOffset = 0; }
        }
        Interlocked.Add(ref samplesPlayed, copied);
        if (copied > 0) PositionChanged?.Invoke(this, new(Position));
        buffering = copied < buffer.Length && !chunks.Reader.Completion.IsCompleted;
        if (copied == 0 && chunks.Reader.Completion.IsCompleted)
        {
            if (!ended) { ended = true; EndOfStreamReached?.Invoke(this, EventArgs.Empty); }
            return 0;
        }
        buffer[copied..].Clear();
        return buffer.Length;
    }
    public void Seek(int sampleOffset) => throw new NotSupportedException("Restart the provider at the requested time.");
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        cancellation.Cancel();
        _ = Completion.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }
}
