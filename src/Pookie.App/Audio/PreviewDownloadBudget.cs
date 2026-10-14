namespace Pookie.Audio;

// Counts consumed response bodies across one wave session; shared by sequential preview batches.
internal sealed class PreviewDownloadBudget(long maximumBytes)
{
    private long remaining = maximumBytes > 0 ? maximumBytes : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
    internal long Remaining => Interlocked.Read(ref remaining);
    internal long Used => maximumBytes - Remaining;
    internal int Reserve(int requested)
    {
        if (requested == 0) return 0;
        while (true)
        {
            var available = Remaining;
            if (available == 0) throw new IOException("Исчерпан бюджет фонового аудиоанализа.");
            var count = (int)Math.Min(requested, available);
            if (Interlocked.CompareExchange(ref remaining, available - count, available) == available) return count;
        }
    }
    internal void Refund(int count) => Interlocked.Add(ref remaining, count);
}

internal sealed class PreviewBudgetHandler(PreviewDownloadBudget budget, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (budget.Remaining == 0) throw new IOException("Исчерпан бюджет фонового аудиоанализа.");
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.Content = new BudgetContent(response.Content, budget);
        return response;
    }

    private sealed class BudgetContent : HttpContent
    {
        private readonly HttpContent inner;
        private readonly PreviewDownloadBudget budget;
        internal BudgetContent(HttpContent inner, PreviewDownloadBudget budget)
        {
            this.inner = inner; this.budget = budget;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override bool TryComputeLength(out long length)
        { length = inner.Headers.ContentLength ?? 0; return inner.Headers.ContentLength.HasValue; }
        protected override async Task<Stream> CreateContentReadStreamAsync() => new BudgetStream(await inner.ReadAsStreamAsync().ConfigureAwait(false), budget);
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => new BudgetStream(await inner.ReadAsStreamAsync(token).ConfigureAwait(false), budget);
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        { using var source = await CreateContentReadStreamAsync().ConfigureAwait(false); await source.CopyToAsync(stream).ConfigureAwait(false); }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class BudgetStream(Stream inner, PreviewDownloadBudget budget) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var reserved = budget.Reserve(buffer.Length);
            var read = 0;
            try { read = inner.Read(buffer[..reserved]); return read; }
            finally { budget.Refund(reserved - read); }
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var reserved = budget.Reserve(buffer.Length);
            var read = 0;
            try { read = await inner.ReadAsync(buffer[..reserved], token).ConfigureAwait(false); return read; }
            finally { budget.Refund(reserved - read); }
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
