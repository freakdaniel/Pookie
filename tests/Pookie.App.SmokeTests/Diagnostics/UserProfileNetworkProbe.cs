using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class UserProfileNetworkProbe
{
    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? await NativeWebLogin.TryRestoreAsync(timeout.Token) ?? throw new InvalidOperationException("No saved SoundCloud session for profile reads");
        await using var browser = new NativeBrowserSession(account, profilePath: Environment.GetEnvironmentVariable("POOKIE_PROFILE_PROBE_PATH"));
        using var http = new HttpClient();
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
        var me = await browser.GetMeAsync(timeout.Token);
        var profile = await api.GetUserAsync(me.Id, timeout.Token);
        if (profile.Id != me.Id || string.IsNullOrWhiteSpace(profile.Username)) throw new InvalidOperationException("Profile metadata was not hydrated");
        Console.WriteLine($"USER_METADATA_NETWORK_OK: biography={profile.Description?.Length ?? 0} chars, banner={profile.BannerUrl != null}; identity omitted");
        var links = await api.GetUserLinksAsync(profile.Id, timeout.Token);
        Console.WriteLine($"USER_LINKS_NETWORK_OK: count={links.Length}");
        foreach (var section in new[] { "activity", "tracks", "albums", "playlists", "reposts", "likes", "followers", "following" })
        {
            var result = await api.GetUserSectionAsync(profile.Id, section, token: timeout.Token);
            Console.WriteLine($"USER_SECTION_NETWORK_OK: section={section}, count={result.Items.Length}, next={result.NextHref != null}");
            if (result.NextHref is { } cursor)
            {
                var next = await api.GetUserSectionAsync(profile.Id, section, cursor, timeout.Token);
                Console.WriteLine($"USER_SECTION_NEXT_NETWORK_OK: section={section}, count={next.Items.Length}");
            }
        }
        var comments = await api.GetUserCommentsAsync(profile.Id, token: timeout.Token);
        Console.WriteLine($"USER_COMMENTS_NETWORK_OK: count={comments.Comments.Length}, tracks={comments.Comments.Count(entry => !string.IsNullOrWhiteSpace(entry.Track?.Title))}, next={comments.NextHref != null}");
        if (comments.NextHref is { } commentCursor) await api.GetUserCommentsAsync(profile.Id, commentCursor, timeout.Token);
        Console.WriteLine("USER_PROFILE_NETWORK_OK: real website API, metadata, links and all sections; no account writes");
    }
}
