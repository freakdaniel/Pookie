using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class PlayReadyHlsStreamTests
{
    [Fact]
    public async Task VirtualFileIndexesLengthsAndReadsOnlyTheRequestedFragments()
    {
        using var handler = new Fragments();
        using var http = new HttpClient(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var manifest = new Uri("https://media.test/track.m3u8");
        await using var media = await PlayReadyHlsStream.OpenAsync(http, manifest, uri => uri.Host == manifest.Host, timeout.Token);
        Assert.Equal(19, media.Length);
        Assert.Equal(30, media.Duration);
        Assert.Empty(handler.Downloads);
        Assert.Equal(4, handler.IndexRequests);

        var output = new byte[5];
        Assert.Equal(5, await media.ReadAsync(12, output, timeout.Token));
        Assert.Equal(new byte[] { 12, 13, 14, 15, 16 }, output);
        Assert.DoesNotContain("/first", handler.Downloads.Keys);
        Assert.DoesNotContain("/init", handler.Downloads.Keys);
        Assert.Equal((10d, 30d), media.BufferedAt(23));

        await media.ReadAsync(12, output, timeout.Token);
        Assert.Equal(1, handler.Downloads["/second"]);
        Assert.Equal(1, handler.Downloads["/third"]);
    }

    private sealed class Fragments : HttpMessageHandler
    {
        private int indexRequests;
        internal int IndexRequests => Volatile.Read(ref indexRequests);
        internal ConcurrentDictionary<string, int> Downloads { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/track.m3u8")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"""
                    #EXTM3U
                    #EXT-X-KEY:METHOD=SAMPLE-AES-CTR,KEYFORMAT="urn:uuid:9a04f079-9840-4286-ab92-e65be0885f95",URI="data:text/plain;base64,{Convert.ToBase64String(new byte[32])}"
                    #EXT-X-MAP:URI="init"
                    #EXTINF:10,
                    first
                    #EXTINF:10,
                    second
                    #EXTINF:10,
                    third
                    #EXT-X-ENDLIST
                    """) });
            var (start, count) = path switch
            {
                "/init" => (0, 4), "/first" => (4, 6), "/second" => (10, 4), "/third" => (14, 5),
                _ => throw new InvalidOperationException("Unexpected media request.")
            };
            if (request.Headers.Range != null)
            {
                Assert.Equal("bytes=0-0", request.Headers.Range.ToString());
                Interlocked.Increment(ref indexRequests);
                var content = new ByteArrayContent([(byte)start]);
                content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, count);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
            }
            Downloads.AddOrUpdate(path, 1, (_, reads) => reads + 1);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(Enumerable.Range(start, count).Select(value => (byte)value).ToArray()) });
        }
    }
}
