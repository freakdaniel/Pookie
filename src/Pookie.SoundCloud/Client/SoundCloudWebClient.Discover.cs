using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<SoundCloudDiscoverShelves> GetDiscoverShelvesAsync(long userId, CancellationToken token = default)
    {
        ValidateDetailTrackId(userId);
        using var document = await GetJsonAsync(Api("mixed-selections"), token);
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
        using var document = await GetJsonAsync(Api("mixed-selections"), token);
        return DiscoverData.PersonalMixes(document.RootElement, userId);
    }
}
