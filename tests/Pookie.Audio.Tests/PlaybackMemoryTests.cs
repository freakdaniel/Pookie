using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Pookie.Audio.Tests;

[CollectionDefinition("Playback memory", DisableParallelization = true)]
public sealed class PlaybackMemoryCollection { }

[Collection("Playback memory")]
public sealed class PlaybackMemoryTests
{
    [Fact]
    public async Task FortyMinutesOfDecodedAudioKeepsPcmAndHttpMemoryBounded()
    {
        using var handler = new GeneratedWaveHandler(40*60);
        using var http = new HttpClient(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var cache = new EncodedTrackCache(http);
        using var provider = new StreamingProvider(http,
            new("https://fixture.invalid/forty-minutes.wav", AudioTransport.Progressive, 40*60) { CaptureAnalysis = true },
            0, uri => uri.Host == "fixture.invalid", timeout.Token, cache);
        try
        {
            await provider.Ready.WaitAsync(timeout.Token);
            var buffer = new float[8192];
            long? warmHeap = null;
            var nextCheck = 120d;
            while (await provider.WaitForDataAsync(timeout.Token))
            {
                provider.ReadBytes(buffer);
                if (provider.Seconds < nextCheck) continue;
                var heap = GC.GetTotalMemory(forceFullCollection: true);
                warmHeap ??= heap;
                Assert.True(heap-warmHeap < 8*1024*1024, $"Decoded/HTTP buffers grew by {(heap-warmHeap)/1048576d:F1} MiB at {provider.Seconds:F0}s.");
                nextCheck += 120;
                if (provider.TakeAnalysisSample() is { } analysis)
                    Assert.InRange(analysis.Mono.Length, 48000, 16000*9);
            }
            await provider.Completion.WaitAsync(timeout.Token);
            Assert.Null(provider.Error);
            Assert.InRange(provider.Seconds, 2399.99, 2400.01);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.InRange(handler.Payloads.Count(reference => reference.TryGetTarget(out _)), 0, 12);
            Assert.True(handler.Payloads.Count > 200, "The test must exercise successive HTTP ranges instead of one retained file.");
        }
        finally
        {
            provider.Dispose(); await provider.Completion;
            cache.Dispose(); await cache.Completion;
        }
        Assert.False(Directory.Exists(cache.DirectoryPath));
    }

    // Produces ranges on demand: the fixture never allocates a 40-minute PCM file.
    private sealed class GeneratedWaveHandler(int seconds) : HttpMessageHandler
    {
        private const int Rate = 16000;
        internal readonly List<WeakReference<byte[]>> Payloads = [];
        private readonly byte[] header = Header(seconds);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = 44L+seconds*Rate*2;
            var range = request.Headers.Range?.Ranges.Single();
            var start = range?.From ?? 0;
            var end = Math.Min(range?.To ?? length-1, length-1);
            var bytes = new byte[checked((int)(end-start+1))];
            for (var i = 0; i < bytes.Length; i++)
            {
                var position = start+i;
                // Stable non-silent PCM; sample values are irrelevant to ownership.
                bytes[i] = position < 44 ? header[position] : (byte)((position-44) % 4 < 2 ? 16 : 240);
            }
            lock (Payloads) Payloads.Add(new(bytes));
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, length);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            return Task.FromResult(response);
        }
        private static byte[] Header(int seconds)
        {
            var bytes = new byte[44];
            "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36+seconds*Rate*2);
            "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), Rate); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), Rate*2);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
            "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), seconds*Rate*2);
            return bytes;
        }
    }
}
