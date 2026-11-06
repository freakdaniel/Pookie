using System.Net;
using System.Text.Json;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;
using Pookie.App.Playback;
using Pookie.SoundCloud;

namespace Pookie.App.Diagnostics;

internal static class PlaylistOrderNetworkProbe
{
    internal static async Task RunAsync(bool editFixture = false, bool verifyHiddenMetadata = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var vault = SessionVault.Open(new AppDataPaths(Path.Combine(AppDataPaths.DefaultRoot(), "Profiles", "Android")));
        var account = vault.Load() ?? throw new InvalidOperationException("Нужен сохранённый мобильный профиль.");
        var options = MobileOAuthConfiguration.Load() ?? throw new InvalidOperationException("Нужна мобильная конфигурация.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
        var challenge = new NativeMobileChallenge();
        using var authorization = new SoundCloudMobileSessionManager(new SoundCloudMobileOAuth(http, options, mobileChallenge: challenge),
            () => account, updated => account = updated, (updated, token) => Task.Run(() => vault.Save(updated), token));
        using var transport = new SoundCloudHttpTransport(http, () => account, authorization: authorization, mobileChallenge: challenge);
        transport.SessionChanged += () => vault.Save(account);
        var api = new SoundCloudWebClient(http) { Session = account, Transport = transport };
        var me = await api.GetMeAsync(timeout.Token);
        var owned = await transport.GetMobileOwnedPlaylistsAsync(me.Id, timeout.Token);
        if (editFixture)
        {
            var seed = Array.Empty<long>();
            foreach (var entry in owned)
            {
                var playlist = await transport.GetMobilePlaylistAsync(entry.Playlist!.Id, timeout.Token);
                seed = playlist.Tracks.Select(track => track.Id).Distinct().Take(2).ToArray();
                if (seed.Length == 2) break;
            }
            if (seed.Length != 2) throw new InvalidOperationException("Нужны два доступных трека для временной проверки.");
            var title = "Pookie: проверка сохранения " + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            long createdId = 0;
            try
            {
                using var created = await transport.CreatePlaylistAsync(new(me.Id, seed[0], title, "private"), timeout.Token);
                createdId = created.RootElement.GetProperty("id").GetInt64();
                await transport.AddToPlaylistAsync(me.Id, createdId, seed[1], timeout.Token);
                var before = await transport.GetMobilePlaylistAsync(createdId, timeout.Token);
                var order = before.Tracks.Select(track => track.Id).ToArray();
                var after = await transport.UpdateMobilePlaylistAsync(new(me.Id, createdId, title + " ✓", "Проверка сохранения метаданных без списка треков.", "private", ExpectedTrackIds: order), timeout.Token);
                if (after.TrackCount != before.TrackCount || !after.Tracks.Select(track => track.Id).SequenceEqual(order))
                    throw new InvalidOperationException("Частичное изменение затронуло список треков временного плейлиста.");
                Console.WriteLine($"PLAYLIST_METADATA_PATCH_LIVE_OK tracks={after.TrackCount} order_preserved=True; private temporary fixture; identifiers omitted");
                var reversed = order.Reverse().ToArray();
                var reordered = await transport.UpdateMobilePlaylistAsync(new(me.Id, createdId, after.Title, after.Description ?? "", "private", reversed, order), timeout.Token);
                if (!reordered.Tracks.Select(track => track.Id).SequenceEqual(reversed)) throw new InvalidOperationException("Порядок временного плейлиста не подтверждён.");
                Console.WriteLine("PLAYLIST_REORDER_LIVE_OK; temporary fixture only; identifiers omitted");
            }
            catch (PlaylistCreationUnconfirmedException error) when (createdId == 0 && error.PlaylistId > 0)
            { createdId = error.PlaylistId; throw; }
            finally
            {
                if (createdId > 0)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    var fresh = await transport.GetMobilePlaylistAsync(createdId, cleanup.Token);
                    if (fresh.User?.Id != me.Id || fresh.Sharing != "private" || !fresh.Title.StartsWith(title, StringComparison.Ordinal))
                        throw new InvalidOperationException("Временный плейлист изменился; автоматическое удаление отменено.");
                    await transport.DeleteMobilePlaylistAsync(me.Id, createdId, cleanup.Token);
                    Console.WriteLine("PLAYLIST_TEMPORARY_FIXTURE_REMOVED; original playlists not modified");
                }
            }
            return;
        }
        foreach (var entry in owned)
        {
            var full = await transport.GetMobilePlaylistAsync(entry.Playlist!.Id, timeout.Token);
            if (full.Tracks.Length == full.TrackCount) continue;
            if (verifyHiddenMetadata)
            {
                var before = full.Tracks.Select(track => track.Id).ToArray();
                var confirmed = await transport.UpdateMobilePlaylistAsync(new(me.Id, full.Id, full.Title, full.Description ?? "",
                    full.Sharing ?? throw new InvalidOperationException("Неизвестная приватность."), ExpectedTrackIds: before), timeout.Token);
                if (confirmed.Title != full.Title || (confirmed.Description ?? "") != (full.Description ?? "") || confirmed.Sharing != full.Sharing ||
                    confirmed.TrackCount != full.TrackCount || !confirmed.Tracks.Select(track => track.Id).SequenceEqual(before) ||
                    confirmed.Genre != full.Genre || !(confirmed.Tags ?? []).SequenceEqual(full.Tags ?? []))
                    throw new InvalidOperationException("Сохранение текущих значений не подтвердило исходные данные плейлиста.");
                Console.WriteLine($"PLAYLIST_HIDDEN_METADATA_LIVE_OK declared={confirmed.TrackCount} available={confirmed.Tracks.Length} visible_order_preserved=True metadata_unchanged=True; all track fields omitted; identifiers omitted");
                return;
            }
            using var web = await transport.GetJsonAsync(new($"https://api-v2.soundcloud.com/playlists/{full.Id}?representation=full"), timeout.Token);
            var root = web.RootElement;
            var tracks = root.GetProperty("tracks").EnumerateArray().ToArray();
            var ids = tracks.Select(track => track.GetProperty("id").GetInt64()).ToArray();
            var mobileIds = full.Tracks.Select(track => track.Id).ToArray();
            var missing = tracks.Where(track => !mobileIds.Contains(track.GetProperty("id").GetInt64())).ToArray();
            Console.WriteLine($"PLAYLIST_ORDER_READ mobile={mobileIds.Length} declared={full.TrackCount} v2_count={root.GetProperty("track_count").GetInt32()} v2_ids={ids.Length} unique={ids.Distinct().Count()} mobile_order_matches={ids.Where(mobileIds.Contains).SequenceEqual(mobileIds)} missing={missing.Length} missing_with_title={missing.Count(track => track.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)}; no identifiers or edits");
            foreach (var query in new[] { "representation=id", "representation=id&access=playable,preview,blocked", "representation=full&access=playable,preview,blocked" })
            {
                using var candidate = await transport.GetJsonAsync(new($"https://api-v2.soundcloud.com/playlists/{full.Id}?{query}"), timeout.Token);
                var body = candidate.RootElement;
                var order = body.GetProperty("tracks").EnumerateArray().Select(track => track.ValueKind == JsonValueKind.Number ? track.GetInt64() : track.GetProperty("id").GetInt64()).ToArray();
                Console.WriteLine($"PLAYLIST_ORDER_VARIANT query={query} declared={body.GetProperty("track_count").GetInt32()} ids={order.Length} preserves_mobile_order={order.Where(mobileIds.Contains).SequenceEqual(mobileIds)}; identifiers omitted");
            }
            using var access = await transport.GetJsonAsync(SoundCloudMobileRoutes.Uri($"/playlists/{full.Urn}/info?access=playable,preview,blocked"), timeout.Token);
            Console.WriteLine($"PLAYLIST_ORDER_MOBILE_ACCESS ids={access.RootElement.GetProperty("tracks").GetProperty("collection").GetArrayLength()}; identifiers omitted");
            if (full.Sharing != "public")
            {
                Console.WriteLine("PLAYLIST_ORDER_REGIONAL_SKIPPED private=True; anonymous playback proxy cannot read private playlist membership");
                continue;
            }
            using var publicHttp = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(12) };
            var publicId = await new SoundCloudWebClient(publicHttp).GetClientIdAsync(timeout.Token);
            var publicUri = new Uri($"https://api-v2.soundcloud.com/playlists/{full.Id}?representation=id&client_id={Uri.EscapeDataString(publicId)}");
            async Task ReadAnonymous(HttpClient client, string label, CancellationToken token)
            {
                using var response = await client.GetAsync(publicUri, token);
                if (!response.IsSuccessStatusCode) { Console.WriteLine($"PLAYLIST_ORDER_ANONYMOUS source={label} status={(int)response.StatusCode}"); return; }
                using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
                var body = json.RootElement;
                var order = body.GetProperty("tracks").EnumerateArray().Select(track => track.ValueKind == JsonValueKind.Number ? track.GetInt64() : track.GetProperty("id").GetInt64()).ToArray();
                Console.WriteLine($"PLAYLIST_ORDER_ANONYMOUS source={label} declared={body.GetProperty("track_count").GetInt32()} ids={order.Length} preserves_mobile_order={order.Where(mobileIds.Contains).SequenceEqual(mobileIds)}; credentials absent; identifiers omitted");
            }
            await ReadAnonymous(publicHttp, "direct", timeout.Token);
            foreach (var uri in new[]
            {
                new Uri($"https://api.soundcloud.com/playlists/{full.Urn}?representation=id&access=playable,preview,blocked"),
                new Uri($"https://api.soundcloud.com/playlists/{full.Urn}/tracks?access=playable,preview,blocked&limit=200&linked_partitioning=true"),
                new Uri($"https://secure.soundcloud.com/playlists/{full.Id}?representation=id&access=playable,preview,blocked&client_id={Uri.EscapeDataString(account.ClientId)}")
            })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new("OAuth", account.AccessToken);
                request.Headers.UserAgent.ParseAdd(account.UserAgent);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await publicHttp.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) { Console.WriteLine($"PLAYLIST_ORDER_OFFICIAL host={uri.Host} tracks_route={uri.AbsolutePath.EndsWith("/tracks")} status={(int)response.StatusCode}; identifiers omitted"); continue; }
                if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
                { Console.WriteLine($"PLAYLIST_ORDER_OFFICIAL host={uri.Host} non_json=True; identifiers omitted"); continue; }
                using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
                var body = json.RootElement;
                var collection = body.ValueKind == JsonValueKind.Array ? body : body.TryGetProperty("tracks", out var list) ? list : body.GetProperty("collection");
                var order = collection.EnumerateArray().Select(track => track.ValueKind == JsonValueKind.Number ? track.GetInt64() : track.GetProperty("id").GetInt64()).ToArray();
                Console.WriteLine($"PLAYLIST_ORDER_OFFICIAL host={uri.Host} tracks_route={uri.AbsolutePath.EndsWith("/tracks")} ids={order.Length} has_next={body.ValueKind == JsonValueKind.Object && body.TryGetProperty("next_href", out var next) && next.ValueKind == JsonValueKind.String}; identifiers omitted");
            }
            var candidates = await new RegionalProxyCatalog(publicHttp).GetAsync(timeout.Token);
            await Parallel.ForEachAsync(candidates.Take(8), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = timeout.Token }, async (endpoint, token) =>
            {
                using var client = new HttpClient(new HttpClientHandler { Proxy = new WebProxy(endpoint.Address), UseProxy = true, UseCookies = false,
                    AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(8) };
                try { await ReadAnonymous(client, endpoint.MaintainedRelay ? "relay" : "pool", token); }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
                { Console.WriteLine("PLAYLIST_ORDER_ANONYMOUS source=pool unavailable; endpoint omitted"); }
            });
        }
        if (verifyHiddenMetadata) throw new InvalidOperationException("Плейлист со скрытыми треками не найден.");
        Console.WriteLine("PLAYLIST_ORDER_PROBE_OK; read-only; mobile OAuth; desktop session not used");
    }
}
