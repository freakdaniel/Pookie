namespace Pookie.SoundCloud;

public sealed partial class SoundCloudWebClient
{
    public async Task<HashSet<long>> GetRepostedIdsAsync(CancellationToken token = default)
    {
        if (Session == null) throw new SoundCloudException("Для репостов нужен вход в SoundCloud.");
        var ids = new HashSet<long>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Uri? target = Api("me/track_reposts/ids?limit=200&linked_partitioning=1");
        for (var page = 0; target != null; page++)
        {
            if (page >= 1000 || !BrowserRequestCommand.IsReadUrl(target.AbsoluteUri) ||
                target.AbsolutePath != "/me/track_reposts/ids" || !seen.Add(target.AbsoluteUri))
                throw new SoundCloudException("Неверный адрес страницы репостов.");
            using var document = await GetJsonAsync(target, token);
            var root = document.RootElement;
            var collection = root.ValueKind == System.Text.Json.JsonValueKind.Array ? root : root.GetProperty("collection");
            foreach (var item in collection.EnumerateArray())
            {
                long id = 0;
                if (item.ValueKind == System.Text.Json.JsonValueKind.Number) item.TryGetInt64(out id);
                else if (item.ValueKind == System.Text.Json.JsonValueKind.String) long.TryParse(item.GetString(), out id);
                if (id is > 0 and <= 9007199254740991) ids.Add(id);
            }
            target = null;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("next_href", out var next) &&
                next.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (!Uri.TryCreate(next.GetString(), UriKind.Absolute, out var uri)) throw new SoundCloudException("Неверный адрес страницы репостов.");
                target = uri;
            }
        }
        return ids;
    }

    public async Task SetRepostedAsync(long trackId, bool reposted, CancellationToken token = default)
    {
        ValidateDetailTrackId(trackId);
        if (Session == null) throw new SoundCloudException("Войди в SoundCloud, чтобы сделать репост.");
        if (BrowserTransport is { } browser) { await browser.SetRepostedAsync(trackId, reposted, token); return; }
        if (RequireBrowserTransport) throw new SoundCloudException("Браузерное соединение SoundCloud ещё не подключено.");
        using var document = await GetJsonAsync(Api($"me/track_reposts/{trackId}"), token, reposted ? HttpMethod.Put : HttpMethod.Delete);
    }
}
