using System.Net;
using System.Net.Http.Headers;

namespace Pookie.Audio;

// FFmpeg reads synchronously on the decoder worker, never on the UI or audio callback.
// Keep a few HTTP ranges in memory instead of downloading a whole progressive track.
internal sealed class HttpRangeStream(HttpClient http, Uri uri, long length, byte[] firstBlock, CancellationToken token) : Stream
{
    private const int BlockSize = 256 * 1024;
    private readonly Dictionary<long, byte[]> blocks = new() { [0] = firstBlock };
    private long position;
    public Exception? ReadError { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }

    public static async Task<Stream> OpenAsync(HttpClient http, Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(0, BlockSize - 1);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        try
        {
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.PartialContent)
                return new ResponseStream(await response.Content.ReadAsStreamAsync(token), response, token);
            var range = response.Content.Headers.ContentRange;
            if (range?.From != 0 || range.To == null || range.Length == null || range.Length <= 0 || range.To >= BlockSize)
                throw new IOException("Invalid HTTP byte range.");
            var first = await ReadBoundedAsync(response.Content, BlockSize, token);
            if (first.Length != range.To + 1) throw new IOException("Incomplete HTTP byte range.");
            return new HttpRangeStream(http, uri, range.Length.Value, first, token);
        }
        catch { response.Dispose(); throw; }
        finally { if (response.StatusCode == HttpStatusCode.PartialContent) response.Dispose(); }
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new IOException("Media response exceeds the buffer limit.");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (memory.Length + read > limit) throw new IOException("Media response exceeds the buffer limit.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    public override int Read(Span<byte> buffer)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var read = 0;
            while (read < buffer.Length && position < length)
            {
                var start = position / BlockSize * BlockSize;
                if (!blocks.TryGetValue(start, out var block))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    var end = Math.Min(length - 1, start + BlockSize - 1);
                    request.Headers.Range = new RangeHeaderValue(start, end);
                    using var response = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    var range = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != start || range.To != end || range.Length != length)
                        throw new IOException("Server did not honor the HTTP byte range.");
                    block = ReadBoundedAsync(response.Content, BlockSize, token).GetAwaiter().GetResult();
                    if (block.Length != end - start + 1) throw new IOException("Incomplete HTTP byte range.");
                    if (blocks.Count == 8) blocks.Remove(blocks.Keys.First());
                    blocks[start] = block;
                }
                var offset = (int)(position - start);
                var count = Math.Min(buffer.Length - read, block.Length - offset);
                if (count <= 0) throw new IOException("Incomplete HTTP byte range.");
                block.AsSpan(offset, count).CopyTo(buffer[read..]);
                read += count; position += count;
            }
            return read;
        }
        catch (Exception error) { ReadError = error; throw; }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = checked((origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => position, SeekOrigin.End => length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }) + offset);
        if (target < 0) throw new IOException("Cannot seek before the start.");
        return position = target;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    internal sealed class ResponseStream(Stream stream, HttpResponseMessage response, CancellationToken token) : Stream
    {
        public Exception? ReadError { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(Span<byte> buffer)
        {
            try
            {
                var bytes = new byte[buffer.Length];
                var count = stream.ReadAsync(bytes, token).AsTask().GetAwaiter().GetResult();
                bytes.AsSpan(0, count).CopyTo(buffer);
                return count;
            }
            catch (Exception error) { ReadError = error; throw; }
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { if (disposing) { stream.Dispose(); response.Dispose(); } base.Dispose(disposing); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
