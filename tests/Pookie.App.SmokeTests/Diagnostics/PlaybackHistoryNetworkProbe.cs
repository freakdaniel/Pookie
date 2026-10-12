using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class PlaybackHistoryNetworkProbe
{
    internal static async Task RunAsync(string url, string? context = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? throw new InvalidOperationException("No saved session for the history probe.");
        await using var browser = new NativeBrowserSession(account);
        using var http = new HttpClient();
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
        var user = await browser.GetMeAsync(timeout.Token);
        Console.WriteLine("HISTORY_ACCOUNT_OK: saved session authenticated; account details omitted");
        var track = await api.ResolveAsync(url, timeout.Token);
        var before = await api.GetLibraryAsync("history", user.Id, timeout.Token);
        if (before.Items.FirstOrDefault()?.Track?.Id == track.Id)
            throw new InvalidOperationException("Choose a track other than the latest history entry so the probe can verify a change.");
        Console.WriteLine($"HISTORY_BEFORE: first={before.Items.FirstOrDefault()?.Track?.Id}; target={track.Id}; index={Array.FindIndex(before.Items, item => item.Track?.Id == track.Id)}");
        await api.RecordPlaybackAsync(track.Id, context, timeout.Token);
        var after = await api.GetLibraryAsync("history", user.Id, timeout.Token);
        // Observe server persistence separately from the write's HTTP success.
        using var refresh = new PeriodicTimer(TimeSpan.FromSeconds(1));
        for (var attempt = 0; after.Items.FirstOrDefault()?.Track?.Id != track.Id && attempt < 10; attempt++)
        {
            await refresh.WaitForNextTickAsync(timeout.Token);
            after = await api.GetLibraryAsync("history", user.Id, timeout.Token);
        }
        Console.WriteLine($"HISTORY_AFTER: first={after.Items.FirstOrDefault()?.Track?.Id}; target={track.Id}; index={Array.FindIndex(after.Items, item => item.Track?.Id == track.Id)}");
        if (after.Items.FirstOrDefault()?.Track?.Id != track.Id)
            throw new InvalidOperationException("SoundCloud accepted the history write but the track did not become the latest play.");
        Console.WriteLine("PLAYBACK_HISTORY_NETWORK_OK: production browser write appeared in server history");
    }
}
