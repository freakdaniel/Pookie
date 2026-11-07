using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class DownloadRecoveryTests
{
    [Theory]
    [InlineData(AudioTransport.Progressive)]
    [InlineData(AudioTransport.Hls)]
    public async Task EntireEncodedTrackDownloadsWithoutConsumingPcm(AudioTransport transport)
    {
        using var handler = new MediaHandler(); using var http = new HttpClient(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cache = new EncodedTrackCache(http);
        using var provider = new StreamingProvider(http, new("https://fixture.invalid/" + (transport == AudioTransport.Hls ? "track.m3u8" : "track.wav"), transport, 240), 0, _ => true, timeout.Token, cache);
        try
        {
            await provider.Ready.WaitAsync(timeout.Token);
            await Until(() => provider.BufferedEnd >= 240, timeout.Token);
            Assert.Equal(0, provider.Seconds); // no callback consumption, like a paused player
            Assert.Null(provider.Error);
            if (transport == AudioTransport.Hls) Assert.Contains("/segment19.wav", handler.Requests.Keys);
            else Assert.True(handler.Requests.Keys.Count(key => key.StartsWith("bytes=")) > 8);
        }
        finally { provider.Dispose(); await provider.Completion; cache.Dispose(); await cache.Completion; }
    }

    [Fact]
    public async Task FailedDownloadRecoversWithoutLosingCachedDataAndStopCancelsRetries()
    {
        using var handler = new MediaHandler { Offline = true }; using var http = new HttpClient(handler);
        using var cache = new EncodedTrackCache(http);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new EncodedTrackCache.Request(new("https://fixture.invalid/segment0.wav"), 1048576);
        var waiting = cache.ReadAsync(request, stop.Token);
        await Until(() => handler.Failures >= 2, stop.Token);
        Assert.False(waiting.IsCompleted);
        handler.Offline = false;
        var bytes = await waiting.WaitAsync(stop.Token);
        handler.Offline = true;
        Assert.Equal(bytes, await cache.ReadAsync(request, stop.Token));
        var pending = cache.ReadAsync(request with { Uri = new("https://fixture.invalid/segment1.wav") }, stop.Token);
        await Until(() => handler.Failures >= 3, stop.Token);
        cache.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cache.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(Directory.Exists(cache.DirectoryPath));
    }

    [Fact]
    public async Task HlsUnderrunWaitsForReconnectionWithoutEndingOrResettingPlayback()
    {
        using var handler = new MediaHandler { BlockSegmentAt = 1 }; using var http = new HttpClient(handler);
        using var cache = new EncodedTrackCache(http);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var provider = new StreamingProvider(http, new("https://fixture.invalid/track.m3u8", AudioTransport.Hls), 0, _ => true, timeout.Token, cache);
        try
        {
            await provider.Ready.WaitAsync(timeout.Token);
            var pcm = new float[8192];
            do { Assert.True(await provider.WaitForDataAsync(timeout.Token)); provider.ReadBytes(pcm); }
            while (!provider.Buffering || provider.Seconds < 1.99);
            var position = provider.Seconds;
            Assert.False(provider.Ended); Assert.Null(provider.Error);
            provider.ReadBytes(pcm); Assert.Equal(position, provider.Seconds);
            handler.BlockSegmentAt = null;
            Assert.True(await provider.WaitForDataAsync(timeout.Token)); provider.ReadBytes(pcm);
            Assert.True(provider.Seconds > position); Assert.False(provider.Ended); Assert.Null(provider.Error);
            Assert.Equal(1, handler.Requests["/segment0.wav"]);
        }
        finally { provider.Dispose(); await provider.Completion; cache.Dispose(); await cache.Completion; }
    }

    [Fact]
    public async Task SeekCancellationDoesNotCancelFullTrackPrefetch()
    {
        using var handler = new MediaHandler { Slow = true }; using var http = new HttpClient(handler);
        using var cache = new EncodedTrackCache(http); using var seek = new CancellationTokenSource();
        var requests = Enumerable.Range(0, 20).Select(index => new EncodedTrackCache.Request(new($"https://fixture.invalid/segment{index}.wav"), 1048576)).ToArray();
        cache.Prefetch(requests, seek.Token); seek.Cancel();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Until(() => cache.Contains(requests[^1]), timeout.Token);
        Assert.All(requests, request => Assert.True(cache.Contains(request)));
        Assert.All(handler.Requests, request => Assert.Equal(1, request.Value));
        cache.Dispose(); await cache.Completion;
    }

    [Fact]
    public async Task NonRangeStreamDownloadsInBackgroundAndResumesAnInterruptedResponse()
    {
        using var handler = new MediaHandler { NoRanges = true, BreakBody = true }; using var http = new HttpClient(handler);
        using var cache = new EncodedTrackCache(http);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stream = (ProgressiveTrackDownload.Reader)await HttpRangeStream.OpenAsync(http, new("https://fixture.invalid/track.wav"), timeout.Token, cache, 240);
        await Until(() => stream.BufferedFraction == 1, timeout.Token);
        Assert.True(handler.Reconnections > 0);
        stream.Position = 700000;
        var expected = handler.Track.AsSpan(700000, 128).ToArray(); var actual = new byte[128];
        stream.ReadExactly(actual); Assert.Equal(expected, actual);
        stream.Dispose(); cache.Dispose(); await cache.Completion;
    }

    [Fact]
    public async Task AuthorizationAndProtocolFailuresDoNotRetryForever()
    {
        using var handler = new MediaHandler { Forbidden = true }; using var http = new HttpClient(handler);
        using var cache = new EncodedTrackCache(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.ReadAsync(new(new("https://fixture.invalid/segment0.wav"), 1048576), default));
        Assert.Single(handler.Requests);
        cache.Dispose(); await cache.Completion;
    }

    private static async Task Until(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(20, token); }

    private sealed class MediaHandler : HttpMessageHandler
    {
        internal volatile bool Offline;
        internal bool Slow, NoRanges, BreakBody, Forbidden;
        internal int? BlockSegmentAt;
        internal int Failures, Reconnections;
        internal readonly ConcurrentDictionary<string, int> Requests = new();
        internal readonly byte[] Track = Wave(240);
        private readonly byte[] segment = Wave(2);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Slow) await Task.Delay(10, token);
            var key = request.RequestUri!.AbsolutePath == "/track.wav" ? request.Headers.Range?.ToString() ?? "whole" : request.RequestUri.AbsolutePath;
            Requests.AddOrUpdate(key, 1, (_, count) => count + 1);
            if (Offline) { Interlocked.Increment(ref Failures); throw new HttpRequestException("Connection lost."); }
            var name = Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath);
            if (name.StartsWith("segment") && int.TryParse(name.AsSpan(7), out var segmentIndex) && BlockSegmentAt is { } firstBlocked && segmentIndex >= firstBlocked)
            { Interlocked.Increment(ref Failures); throw new HttpRequestException("VPN switching."); }
            if (Forbidden) return new(HttpStatusCode.Forbidden);
            if (request.RequestUri.AbsolutePath.EndsWith("m3u8"))
                return new(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U\n" + string.Concat(Enumerable.Range(0, 20).Select(index => $"#EXTINF:12,\nsegment{index}.wav\n")) + "#EXT-X-ENDLIST", Encoding.UTF8) };
            var track = request.RequestUri.AbsolutePath == "/track.wav";
            var bytes = track ? Track : segment;
            var range = request.Headers.Range?.Ranges.Single();
            if (track && NoRanges)
            {
                if (range?.From > 0) Interlocked.Increment(ref Reconnections);
                HttpContent content = BreakBody && Reconnections == 0 ? new StreamContent(new InterruptedBody(bytes)) : new ByteArrayContent(bytes);
                content.Headers.ContentLength = bytes.Length;
                return new(HttpStatusCode.OK) { Content = content };
            }
            var start = range?.From ?? 0; var end = Math.Min(range?.To ?? bytes.Length - 1, bytes.Length - 1);
            var response = new HttpResponseMessage(range != null ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                { Content = new ByteArrayContent(bytes.AsSpan((int)start, (int)(end - start + 1)).ToArray()) };
            if (range != null) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, bytes.Length);
            return response;
        }
    }

    private sealed class InterruptedBody(byte[] bytes) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position >= 65536) throw new IOException("VPN disconnected during response.");
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 65536 - (int)Position)], token);
        }
    }

    private static byte[] Wave(int seconds)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var count = seconds * 16000;
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(count * 2);
        for (var index = 0; index < count; index++) writer.Write((short)(Math.Sin(index * .1) * 1000));
        return stream.ToArray();
    }
}
