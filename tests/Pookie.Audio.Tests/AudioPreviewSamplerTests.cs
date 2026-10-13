using Xunit;

namespace Pookie.Audio.Tests;

public sealed class AudioPreviewSamplerTests
{
    [Fact]
    public async Task PreviewSkipsIntroCapturesNineSecondsAndClosesDecoderWithoutAnAudioDevice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pookie-preview-{Guid.NewGuid():N}.wav");
        try
        {
            const int rate = 16000, seconds = 40;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + rate * seconds * 2); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
                writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(rate * seconds * 2);
                for (var i = 0; i < rate * seconds; i++)
                    writer.Write((short)(i < rate * 10 ? 0 : short.MaxValue * .2 * Math.Sin(2 * Math.PI * 440 * i / rate)));
            }
            using var http = new HttpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var sample = await AudioPreviewSampler.CaptureAsync(http, new(path, AudioTransport.File, seconds), _ => false, timeout.Token);
            Assert.NotNull(sample);
            Assert.Equal(144000, sample.Mono.Length);
            Assert.InRange(Math.Sqrt(sample.Mono.Average(value => (double)value * value)), .13, .15);
            // Successful exclusive access proves the decoder has finished and released the input.
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ProtectedPreviewDoesNotStartALicenseOrBrowserSession()
    {
        using var http = new HttpClient();
        Assert.Null(await AudioPreviewSampler.CaptureAsync(http,
            new("invalid://no-request", AudioTransport.WidevineHls, 180), _ => throw new InvalidOperationException("Must not request media"), default));
    }

    [Fact]
    public async Task CancellationReleasesTheDecoderAndPropagates()
    {
        using var http = new HttpClient();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AudioPreviewSampler.CaptureAsync(http,
            new("invalid://no-request", AudioTransport.Progressive, 180), _ => true, stop.Token));
    }
}
