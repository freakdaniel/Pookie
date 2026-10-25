using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class FeedNetworkProbe
{
    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? throw new InvalidOperationException("No saved session for feed reads");
        await using var browser = new NativeBrowserSession(account);
        using var http = new HttpClient();
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
        using var raw = await browser.GetJsonAsync(new Uri("https://api-v2.soundcloud.com/stream?limit=30&linked_partitioning=1"), timeout.Token);
        var first = raw.RootElement.GetProperty("collection").EnumerateArray().FirstOrDefault();
        Console.WriteLine("FEED_SHAPE: " + string.Join(",", first.EnumerateObject().Select(property => property.Name)));
        var page = await api.GetFeedActivitiesAsync(token: timeout.Token);
        if (page.Entries.Length == 0) throw new InvalidOperationException("The authenticated feed contained no playable publications");
        Console.WriteLine($"FEED_NETWORK_OK: entries={page.Entries.Length}; reposts={page.Entries.Count(entry => entry.IsRepost)}; actors={page.Entries.Count(entry => entry.Actor?.Id > 0)}; playlists={page.Entries.Count(entry => entry.Item.Playlist != null)}; next={page.NextHref != null}");
        if (page.NextHref != null)
        {
            var next = await api.GetFeedActivitiesAsync(page.NextHref, timeout.Token);
            Console.WriteLine($"FEED_NEXT_NETWORK_OK: entries={next.Entries.Length}; no account writes");
        }
    }
}
