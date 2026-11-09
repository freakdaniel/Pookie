using System.Net;
using System.Net.Http.Headers;

namespace Pookie.Audio;

// Present the init segment and fragments as one seekable, encrypted fMP4. Index
// byte lengths up front and fetch media bodies only as the reader needs them.
internal sealed class PlayReadyHlsStream : IAsyncDisposable
{
    private const int ReadAheadBytes = 5 * 1024 * 1024;
    private readonly EncodedTrackCache cache;
    private readonly EncodedTrackCache.Request[] parts;
    private readonly long[] offsets;
    private readonly ProtectedHlsPlaylist playlist;
    internal long Length => offsets[^1];
    internal double Duration => playlist.Duration;
    internal byte[] InitData => playlist.InitData;

    private PlayReadyHlsStream(HttpClient http, ProtectedHlsPlaylist playlist, EncodedTrackCache.Request[] parts, long[] offsets)
    {
        cache = new(http);
        this.playlist = playlist;
        this.parts = parts;
        this.offsets = offsets;
    }

    public static async Task<PlayReadyHlsStream> OpenAsync(HttpClient http, Uri manifest, Func<Uri, bool> allowed, CancellationToken token)
    {
        var text = System.Text.Encoding.UTF8.GetString(await AudioHttp.ReadAsync(http, manifest, 1024 * 1024, token).ConfigureAwait(false));
        var playlist = ProtectedHlsPlaylist.Parse(text, manifest, allowed, playReady: true);
        var parts = new[] { new EncodedTrackCache.Request(playlist.Initialization, 1024 * 1024) }
            .Concat(playlist.Segments.Select(segment => new EncodedTrackCache.Request(segment.Uri, 16 * 1024 * 1024))).ToArray();
        var lengths = new long[parts.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, parts.Length), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token },
            async (index, stop) => lengths[index] = await LengthAsync(http, parts[index], stop).ConfigureAwait(false)).ConfigureAwait(false);
        var offsets = new long[parts.Length + 1];
        for (var index = 0; index < parts.Length; index++) offsets[index + 1] = checked(offsets[index] + lengths[index]);
        return new(http, playlist, parts, offsets);
    }

    private static Task<long> LengthAsync(HttpClient http, EncodedTrackCache.Request part, CancellationToken token) => AudioHttp.RetryAsync(async stop =>
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, part.Uri);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? length;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange is not { From: 0, To: 0 } range)
                throw new InvalidDataException("Invalid protected media length response.");
            length = range.Length;
            if ((await HttpRangeStream.ReadBoundedAsync(response.Content, 1, stop).ConfigureAwait(false)).Length != 1)
                throw new IOException("Incomplete media length response.");
        }
        else length = response.Content.Headers.ContentLength;
        if (length is not > 0 || length > part.Maximum) throw new InvalidDataException("Invalid protected media fragment size.");
        return length.Value;
    }, token);

    internal async Task<int> ReadAsync(long position, Memory<byte> destination, CancellationToken token)
    {
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        var count = (int)Math.Min(destination.Length, Math.Max(0, Length - position));
        if (count == 0) return 0;
        var index = Array.BinarySearch(offsets, position);
        if (index < 0) index = ~index - 1;
        var written = 0;
        while (written < count)
        {
            var size = (int)Math.Min(count - written, offsets[index + 1] - position);
            await cache.ReadRangeAsync(parts[index], position - offsets[index], destination.Slice(written, size), token).ConfigureAwait(false);
            written += size;
            position += size;
            index++;
        }
        return count;
    }

    internal (double Start, double End) BufferedAt(double position)
    {
        var index = SegmentAt(position);
        if (!cache.Contains(parts[index + 1])) return default;
        var start = index;
        var end = index;
        while (start > 0 && cache.Contains(parts[start])) start--;
        while (end + 1 < playlist.Segments.Length && cache.Contains(parts[end + 2])) end++;
        var last = playlist.Segments[end];
        return (playlist.Segments[start].Start, last.Start + last.Duration);
    }

    internal void Prefetch(double position, CancellationToken token)
    {
        var start = SegmentAt(position) + 1;
        var end = Math.Min(parts.Length, start + 3);
        // Start playing on demand, and warm a bounded amount of encrypted
        // media ahead of playback so forward seeks can use the local cache.
        while (end < Math.Min(parts.Length, start + 24) && offsets[end + 1] - offsets[start] <= ReadAheadBytes)
            end++;
        cache.Prefetch(parts[start..end], token, concurrency: 3);
    }

    internal void PrefetchSeek(double current, double target, CancellationToken token)
    {
        // The Windows MP4 source walks intervening moof boxes on a cold
        // forward seek. Fetch that range concurrently while it walks.
        var destination = SegmentAt(target);
        var start = Math.Min(SegmentAt(current), destination) + 1;
        var end = Math.Min(parts.Length, destination + 4);
        cache.Prefetch(parts[start..end], token, seeking: true, concurrency: 4);
    }

    private int SegmentAt(double position)
    {
        var index = Array.FindIndex(playlist.Segments, segment => position < segment.Start + segment.Duration);
        return index < 0 ? playlist.Segments.Length - 1 : index;
    }

    public async ValueTask DisposeAsync()
    {
        cache.Dispose();
        await cache.Completion.ConfigureAwait(false);
    }
}
