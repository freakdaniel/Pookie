using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Playback;
using Pookie.App.Storage;
using Pookie.SoundCloud;
using System.Text.Json;

namespace Pookie.App.Diagnostics;

internal static class TrackPageNetworkProbe
{
    internal static async Task RunAsync(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? throw new InvalidOperationException("No saved SoundCloud session for the track page probe.");
        await using var browser = new NativeBrowserSession(account);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
        await browser.GetMeAsync(timeout.Token);
        Console.WriteLine("TRACK_PAGE_SESSION_OK: preserved session accepted; account details omitted");
        var track = await api.ResolveAsync(url, timeout.Token);
        track = await api.GetTrackAsync(track.Id, timeout.Token);
        if (track.Id <= 0 || track.Title.Length == 0 || track.DurationSeconds <= 0 || track.User == null)
            throw new InvalidOperationException("Incomplete real track metadata.");
        Console.WriteLine($"TRACK_PAGE_METADATA_OK: duration={track.DurationSeconds:F1}s; description={track.Description?.Length ?? 0} chars");
        var comments = await api.GetTrackCommentsAsync(track.Id, timeout.Token);
        Console.WriteLine($"TRACK_PAGE_COMMENTS_OK: count={comments.Comments.Length}; timed={comments.Comments.Count(comment => comment.Timestamp >= 0)}; next={comments.NextHref != null}");
        if (comments.NextHref is { } cursor)
        {
            var next = await api.GetTrackCommentsNextAsync(track.Id, cursor, timeout.Token);
            if (next.Comments.Length == 0) throw new InvalidOperationException("Empty second page of real comments.");
            Console.WriteLine($"TRACK_PAGE_COMMENTS_NEXT_OK: count={next.Comments.Length}");
        }
        var related = await api.GetRelatedTracksAsync(track.Id, timeout.Token);
        Console.WriteLine($"TRACK_PAGE_RELATED_OK: count={related.Tracks.Length}");
        if (!Uri.TryCreate(track.WaveformUrl, UriKind.Absolute, out var waveform))
            throw new InvalidOperationException("Real track has no waveform URL.");
        if (waveform.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            waveform = new UriBuilder(waveform) { Path = waveform.AbsolutePath[..^4] + ".json" }.Uri;
        var bytes = await new WaveformGainResolver(http, new AppDataPaths()).ReadAsync(waveform, timeout.Token)
            ?? throw new InvalidOperationException("Real waveform could not be downloaded.");
        using var json = JsonDocument.Parse(bytes);
        if (WaveformData.Parse(json.RootElement).Length == 0) throw new InvalidOperationException("Empty real waveform.");
        Console.WriteLine("TRACK_PAGE_NETWORK_OK: metadata, timed comments, next page, related tracks and waveform; no account writes");
    }
}
