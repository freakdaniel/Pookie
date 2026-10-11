using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class PlaylistPageNetworkProbe
{
    internal static async Task RunAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "soundcloud.com" || uri.UserInfo != "")
            throw new ArgumentException("Expected a SoundCloud playlist URL.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var vault = SessionVault.Open(new AppDataPaths());
        var account = vault.Load() ?? await NativeWebLogin.TryRestoreAsync(timeout.Token) ?? throw new InvalidOperationException("No saved SoundCloud session.");
        await using var browser = new NativeBrowserSession(account);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var api = new SoundCloudWebClient(http) { Session = account, BrowserTransport = browser, RequireBrowserTransport = true };
        await browser.GetMeAsync(timeout.Token);
        using var json = await browser.GetJsonAsync(new Uri("https://api-v2.soundcloud.com/resolve?url=" + Uri.EscapeDataString(url)), timeout.Token);
        var item = LibraryData.FromPlaylist(LibraryData.ParsePlaylist(json.RootElement));
        var detail = await api.GetCollectionDetailAsync(item, timeout.Token);
        if (detail.Item.Playlist is not { Id: > 0 } playlist || detail.Tracks.Tracks.Length == 0) throw new InvalidOperationException("Incomplete playlist metadata.");
        Console.WriteLine($"PLAYLIST_METADATA_OK: advertised={playlist.TrackCount}; loaded={detail.Tracks.Tracks.Length}; modified={playlist.LastModified != null}; description={playlist.Description?.Length ?? 0}");
        await api.GetPlaylistActionIdsAsync(false, timeout.Token);
        Console.WriteLine("PLAYLIST_LIKES_READ_OK");
        await api.GetPlaylistActionIdsAsync(true, timeout.Token);
        Console.WriteLine("PLAYLIST_REPOSTS_READ_OK");
        Console.WriteLine("PLAYLIST_NETWORK_OK: playlist metadata, ordered tracks and account action reads; no account writes");
    }
}
