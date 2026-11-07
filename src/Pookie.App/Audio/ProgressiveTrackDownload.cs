using System.Net;
using System.Net.Http.Headers;

namespace Pookie.Audio;

// Servers without byte-range support still download independently of decoding.
// A resumed response is validated, or its already stored prefix is skipped.
internal sealed class ProgressiveTrackDownload
{
    private readonly object gate = new();
    private readonly string path;
    private long available, total;
    private bool complete;
    private Exception? error;
    private TaskCompletionSource changed = NewSignal();
    internal Task Completion { get; }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal ProgressiveTrackDownload(HttpClient http, Uri uri, string path, HttpResponseMessage initial, CancellationToken token)
    {
        this.path = path; total = initial.Content.Headers.ContentLength ?? -1;
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
        Completion = Task.Run(async () =>
        {
            HttpResponseMessage? first = initial;
            try
            {
                await AudioHttp.RetryAsync(async stop =>
                {
                    using var headers = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    headers.CancelAfter(AudioHttp.StallTimeout);
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    var start = Volatile.Read(ref available);
                    if (start > 0) request.Headers.Range = new RangeHeaderValue(start, null);
                    using var response = first ?? await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
                    first = null; headers.CancelAfter(Timeout.InfiniteTimeSpan);
                    response.EnsureSuccessStatusCode();
                    var ranged = response.StatusCode == HttpStatusCode.PartialContent;
                    var size = ranged ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength;
                    if (ranged && response.Content.Headers.ContentRange?.From != start || size is > 0 && total > 0 && size != total)
                        throw new InvalidDataException("Progressive audio changed while reconnecting.");
                    if (size is > 0) Volatile.Write(ref total, size.Value);
                    await using var input = await response.Content.ReadAsStreamAsync(stop).ConfigureAwait(false);
                    var buffer = new byte[65536];
                    async ValueTask<int> Read(int count)
                    {
                        using var read = CancellationTokenSource.CreateLinkedTokenSource(stop);
                        read.CancelAfter(AudioHttp.StallTimeout);
                        return await input.ReadAsync(buffer.AsMemory(0, count), read.Token).ConfigureAwait(false);
                    }
                    for (var skip = ranged ? 0 : start; skip > 0;)
                    {
                        var count = await Read((int)Math.Min(skip, buffer.Length));
                        if (count == 0) throw new IOException("Interrupted progressive audio prefix.");
                        skip -= count;
                    }
                    int received;
                    while ((received = await Read(buffer.Length)) > 0)
                    {
                        try
                        {
                            await output.WriteAsync(buffer.AsMemory(0, received), stop).ConfigureAwait(false);
                            await output.FlushAsync(stop).ConfigureAwait(false);
                        }
                        catch (IOException failure) { throw new InvalidDataException("Cannot store the audio cache.", failure); }
                        lock (gate) { available += received; Signal(); }
                    }
                    if (total > 0 && available != total) throw new IOException("Interrupted progressive audio response.");
                    return true;
                }, token, attemptTimeout: false).ConfigureAwait(false);
            }
            catch (Exception failure) { lock (gate) error = failure; }
            finally
            {
                first?.Dispose(); await output.DisposeAsync().ConfigureAwait(false);
                lock (gate) { complete = true; if (total < 0) total = available; Signal(); }
            }
        });
    }

    private void Signal() { var previous = changed; changed = NewSignal(); previous.TrySetResult(); }
    internal Reader Open(CancellationToken token) => new(this, token);

    internal sealed class Reader : Stream
    {
        private readonly ProgressiveTrackDownload owner;
        private readonly CancellationToken token;
        private readonly FileStream input;
        internal Reader(ProgressiveTrackDownload owner, CancellationToken token)
        {
            this.owner = owner; this.token = token;
            input = new(owner.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        internal double BufferedFraction => Volatile.Read(ref owner.total) > 0 ? Math.Min(1, Volatile.Read(ref owner.available) / (double)Volatile.Read(ref owner.total)) : 0;
        internal Exception? ReadError { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => Volatile.Read(ref owner.total) > 0;
        public override bool CanWrite => false;
        public override long Length => CanSeek ? Volatile.Read(ref owner.total) : throw new NotSupportedException();
        public override long Position { get => input.Position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty) return 0;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    Task wait;
                    lock (owner.gate)
                    {
                        var count = Math.Min(buffer.Length, owner.available - input.Position);
                        if (count > 0) return input.Read(buffer[..(int)count]);
                        if (owner.complete)
                        {
                            if (owner.error != null) throw owner.error;
                            return 0;
                        }
                        wait = owner.changed.Task;
                    }
                    wait.WaitAsync(token).GetAwaiter().GetResult();
                }
            }
            catch (Exception failure) { ReadError = failure; throw; }
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek) throw new NotSupportedException();
            return origin == SeekOrigin.End ? input.Seek(checked(Length + offset), SeekOrigin.Begin) : input.Seek(offset, origin);
        }
        protected override void Dispose(bool disposing) { if (disposing) input.Dispose(); base.Dispose(disposing); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
