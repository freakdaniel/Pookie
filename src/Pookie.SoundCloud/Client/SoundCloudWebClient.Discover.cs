using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    internal static string SystemPlaylistPath(string urn)
    {
        if (!urn.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal) || !BrowserRequestCommand.IsHistoryContextUrn(urn))
            throw new ArgumentException("Некорректный идентификатор микса.", nameof(urn));
        return "system-playlists/" + Uri.EscapeDataString(urn);
    }

    public async Task<SoundCloudPlaylist> GetMixForSharingAsync(string urn, CancellationToken token = default)
    {
        using var json = await GetJsonAsync(Api(SystemPlaylistPath(urn)), token).ConfigureAwait(false);
        var mix = LibraryData.ParsePlaylist(json.RootElement);
        if (mix.Urn != urn || mix.IsPublic == null) throw new SoundCloudException("SoundCloud не подтвердил настройки доступа к миксу.");
        return mix;
    }

    public async Task<LibraryItem[]> GetBuzzingPlaylistsAsync(CancellationToken token = default)
    {
        var profile = await ResolveLinkAsync("https://soundcloud.com/buzzing-playlists", token).ConfigureAwait(false);
        if (profile.User == null) throw new SoundCloudException("Профиль SoundCloud Buzzing недоступен.");
        var page = await GetUserSectionAsync(profile.User.Id, "playlists", token: token).ConfigureAwait(false);
        return page.Items.Where(item => item.Playlist is { IsPrivate: false } playlist &&
            (playlist.TrackCount > 0 || playlist.Tracks.Length > 0)).Take(8).ToArray();
    }

    public async Task<SoundCloudDiscoverShelves> GetDiscoverShelvesAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        using var document = await GetJsonAsync(Api("mixed-selections"), token).ConfigureAwait(false);
        return DiscoverData.Shelves(document.RootElement, userId);
    }

    public async Task<LibraryItem[]> GetPersonalMixesAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        if (Session == null) throw new SoundCloudException("Для персональных подборок нужен вход в SoundCloud.");

        return await GetRestMixesAsync(userId, token);
    }

    private async Task<LibraryItem[]> GetRestMixesAsync(long userId, CancellationToken token)
    {
        using var document = await GetJsonAsync(Api("mixed-selections"), token).ConfigureAwait(false);
        return DiscoverData.PersonalMixes(document.RootElement, userId);
    }
}
