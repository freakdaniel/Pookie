using System.Net;
using Pookie.App.Playback;
using Pookie.App.Storage;

namespace Pookie.App.Diagnostics;

internal static class WaveformGainSmokeTest
{
    public static async Task RunAsync()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var root = Path.GetFullPath(Path.Combine(temporaryRoot, "pookie-waveform-gain-" + Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(temporaryRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Diagnostic directory must stay inside the temporary directory.");
        try
        {
            var paths = new AppDataPaths(root);
            using var handler = new WaveformHandler();
            using var http = new HttpClient(handler);
            var resolver = new WaveformGainResolver(http, paths);
            var url = "https://wave.sndcdn.com/fixture.png";
            var first = await resolver.ResolveAsync(url, default);
            if (Math.Abs(first + 6.0206) > .0001 || handler.Requests != 1) throw new InvalidOperationException("Whole waveform correction was not resolved.");
            handler.Fail = true;
            var cached = await new WaveformGainResolver(http, new AppDataPaths(root)).ResolveAsync(url, default);
            if (cached != first || handler.Requests != 1) throw new InvalidOperationException("Waveform correction did not survive reopening without a network request.");
            if (await resolver.ResolveAsync("https://wave.sndcdn.com/missing.json", default) != 0)
                throw new InvalidOperationException("Unavailable waveform must keep the original track volume.");
            var before = handler.Requests;
            foreach (var invalid in new[] { "https://evil.test/wave.json", "https://wave.sndcdn.com/wave.json#fragment", "file:///wave.json" })
                if (await resolver.ResolveAsync(invalid, default) != 0) throw new InvalidOperationException("Untrusted waveform accepted.");
            if (handler.Requests != before) throw new InvalidOperationException("Untrusted waveform issued a network request.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var observed = false;
            try { await resolver.ResolveAsync("https://wave.sndcdn.com/cancelled.json", cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            if (!observed) throw new InvalidOperationException("Changing tracks must cancel waveform preparation.");
            Console.WriteLine("WAVEFORM_GAIN_SMOKE_OK: whole-track correction, shared durable cache, missing waveform, trusted endpoints and cancellation");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class WaveformHandler : HttpMessageHandler
    {
        public int Requests;
        public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++;
            if (!request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal)) throw new InvalidOperationException("PNG was not converted to JSON.");
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new StringContent("""{"height":140,"samples":[0,70,70,70]}""") });
        }
    }
}
