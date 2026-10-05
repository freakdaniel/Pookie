using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class PlaybackTests
{
    [Fact]
    public async Task DecoderProducesFloatPcmWithTheOriginalMonoSampleRate()
    {
        await using var server = await MediaServer.StartAsync(1, 22050);
        using var http = new HttpClient();
        using var provider = new StreamingProvider(http, new(server.Url + "/track.wav", AudioTransport.Progressive), 0, uri => uri.IsLoopback, default);
        var format = await provider.Ready;
        Assert.Equal(1, format.Channels);
        Assert.Equal(22050, format.SampleRate);
        var buffer = new float[8192];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (provider.Position == 0) { provider.ReadBytes(buffer); await Task.Delay(10, timeout.Token); }
        var rms = Math.Sqrt(buffer.Average(sample => (double)sample * sample));
        Assert.InRange(rms, .015, .035);
        provider.Dispose();
        await provider.Completion;
    }

    [Fact]
    public async Task NewTrackCancelsOutstandingPreparation()
    {
        await using var server = await MediaServer.StartAsync(1, 22050);
        await using var player = new SoundFlowPlayer(silent: true);
        var previous = player.PlayAsync(new(server.Url + "/slow", AudioTransport.Progressive));
        await Task.Delay(100);
        await player.PlayAsync(new(server.Url + "/track.wav", AudioTransport.Progressive));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previous);
        await WaitAsync(player, state => state.Playing && state.Position > .1);
    }

    [Theory]
    [InlineData(1, 22050)]
    [InlineData(2, 48000)]
    public async Task ProgressivePlaybackSupportsPauseSeekResumeAndStop(int channels, int rate)
    {
        await using var server = await MediaServer.StartAsync(channels, rate);
        await using var player = new SoundFlowPlayer(silent: true);
        await player.PlayAsync(new(server.Url + "/track.wav", AudioTransport.Progressive));
        await WaitAsync(player, state => state.Playing && state.Position > .1);
        Assert.InRange(player.Poll().Duration, 11.9, 12.1);
        player.Pause(true);
        var paused = player.Poll().Position;
        await Task.Delay(150);
        Assert.False(player.Poll().Playing);
        Assert.InRange(player.Poll().Position, paused, paused + .05);
        await player.SeekAsync(8);
        Assert.False(player.Poll().Playing); // seeking preserves pause
        player.Pause(false);
        await WaitAsync(player, state => state.Playing && state.Position > 8.1);
        Assert.InRange(player.Poll().Position, 8, 9.5);
        Assert.Contains(server.Ranges, range => range != null);
        player.Stop();
        Assert.False(player.Poll().Playing);
        Assert.Equal(0, player.Poll().Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HlsDurationAndSeekCoverSegmentsAndByteRanges(bool byteRanges)
    {
        await using var server = await MediaServer.StartAsync(2, 44100);
        await using var player = new SoundFlowPlayer(silent: true);
        await player.PlayAsync(new(server.Url + (byteRanges ? "/ranges.m3u8" : "/playlist.m3u8"), AudioTransport.Hls));
        await WaitAsync(player, state => state.Playing && state.Position > .1);
        Assert.Equal(6, player.Poll().Duration);
        await player.SeekAsync(4.5);
        await WaitAsync(player, state => state.Playing && state.Position > 4.6);
        Assert.InRange(player.Poll().Position, 4.5, 5.5);
        await WaitAsync(player, state => state.Ended);
        Assert.False(player.Poll().Ended); // queue advancement receives end only once
    }

    [Fact]
    public async Task CancellingPreparationAllowsNextTrackToPlay()
    {
        await using var server = await MediaServer.StartAsync(1, 22050);
        await using var player = new SoundFlowPlayer(silent: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.PlayAsync(new(server.Url + "/slow", AudioTransport.Progressive), timeout.Token));
        await player.PlayAsync(new(server.Url + "/track.wav", AudioTransport.Progressive));
        await WaitAsync(player, state => state.Playing && state.Position > .1);
    }

    [Fact]
    public async Task NetworkFailureDoesNotExposeSignedUrls()
    {
        await using var server = await MediaServer.StartAsync(1, 22050);
        await using var player = new SoundFlowPlayer(silent: true);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => player.PlayAsync(
            new(server.Url + "/missing?Policy=private-signed-data", AudioTransport.Progressive)));
        Assert.DoesNotContain("private-signed-data", error.Message);
    }

    private static async Task WaitAsync(IAudioPlayer player, Func<AudioState, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (!condition(player.Poll())) await Task.Delay(30, timeout.Token);
    }

    private sealed class MediaServer(WebApplication app, string url, ConcurrentBag<string?> ranges) : IAsyncDisposable
    {
        public string Url => url;
        public ConcurrentBag<string?> Ranges => ranges;
        public static async Task<MediaServer> StartAsync(int channels, int rate)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var ranges = new ConcurrentBag<string?>();
            var track = Wav(channels, rate, 12);
            var segment = Wav(channels, rate, 2);
            app.MapGet("/track.wav", (HttpContext context) => { ranges.Add(context.Request.Headers.Range); return Results.File(track, "audio/wav", enableRangeProcessing: true); });
            app.MapGet("/segment.wav", () => Results.File(segment, "audio/wav"));
            app.MapGet("/combined.wav", () => Results.File(segment.Concat(segment).Concat(segment).ToArray(), "audio/wav", enableRangeProcessing: true));
            app.MapGet("/playlist.m3u8", () => Results.Text("#EXTM3U\n#EXTINF:2,\nsegment.wav\n#EXTINF:2,\nsegment.wav\n#EXTINF:2,\nsegment.wav\n#EXT-X-ENDLIST", "application/vnd.apple.mpegurl"));
            app.MapGet("/ranges.m3u8", () => Results.Text($"#EXTM3U\n#EXTINF:2,\n#EXT-X-BYTERANGE:{segment.Length}@0\ncombined.wav\n#EXTINF:2,\n#EXT-X-BYTERANGE:{segment.Length}\ncombined.wav\n#EXTINF:2,\n#EXT-X-BYTERANGE:{segment.Length}\ncombined.wav\n#EXT-X-ENDLIST", "application/vnd.apple.mpegurl"));
            app.MapGet("/slow", async (HttpContext context) => { try { await Task.Delay(10000, context.RequestAborted); } catch (OperationCanceledException) { } });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, address, ranges);
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }

    private static byte[] Wav(int channels, int rate, int seconds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var frames = rate * seconds;
        var length = frames * channels * 2;
        writer.Write("RIFF"u8); writer.Write(36 + length); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(rate);
        writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(length);
        for (var i = 0; i < frames; i++)
            for (var channel = 0; channel < channels; channel++)
                writer.Write((short)(Math.Sin(2 * Math.PI * (440 + channel * 110) * i / rate) * 1000));
        return stream.ToArray();
    }
}
