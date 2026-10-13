using Pookie.Recommendations.Audio;

namespace Pookie.Audio;

// Decode one bounded excerpt without opening an output device or a browser playback session.
internal static class AudioPreviewSampler
{
    internal static async Task<AudioSample?> CaptureAsync(HttpClient http, AudioSource source,
        Func<Uri, bool> allowed, CancellationToken token)
    {
        if (source.Transport == AudioTransport.WidevineHls) return null;
        var offset = Math.Min(30, Math.Max(0, source.Duration / 4));
        using var provider = new StreamingProvider(http, source with { CaptureAnalysis = true }, offset, allowed, token);
        try
        {
            await provider.Ready.WaitAsync(token).ConfigureAwait(false);
            var buffer = new float[8192];
            while (provider.Seconds < offset + 9 && await provider.WaitForDataAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (provider.ReadBytes(buffer) == 0) break;
            }
            if (provider.Error is { } error) throw error;
            return provider.TakeAnalysisSample();
        }
        finally
        {
            provider.Dispose();
            await provider.Completion.ConfigureAwait(false);
        }
    }
}
