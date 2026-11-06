using System.Net.Http.Headers;

namespace Pookie.SoundCloud;

// Audio authentication is restricted to CDN and exact Android stream routes.
// The inner HTTP handler must have automatic redirects disabled.
public sealed class SoundCloudMediaSessionHandler(Func<WebSession?> session, ISoundCloudMobileChallengeHandler? mobileChallenge = null,
    Action? protectionChanged = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head || request.RequestUri is not { } uri ||
            !SoundCloudWebClient.IsPlaybackUri(uri) || session() is not { MobileIdentity: not null } account)
            return await SendNetworkAsync(request, cancellationToken).ConfigureAwait(false);
        if (!account.IsValid()) throw new SoundCloudException("Некорректная мобильная сессия аудиопотока.");
        var challengeRetried = false;
        for (var hop = 0; hop <= 5; hop++)
        {
            if (!account.Equals(session())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
            using var audio = new HttpRequestMessage(request.Method, uri)
                { Version = System.Net.HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            foreach (var name in new[] { "Range", "If-Range", "Accept", "Accept-Encoding" })
                if (request.Headers.TryGetValues(name, out var values)) audio.Headers.TryAddWithoutValidation(name, values);
            audio.Headers.Authorization = new AuthenticationHeaderValue("OAuth", account.AccessToken);
            audio.Headers.UserAgent.ParseAdd(account.UserAgent);
            if (SoundCloudMobileRoutes.IsStream(uri))
            {
                var mobile = account.MobileIdentity!;
                audio.Headers.Add("App-Version", mobile.BuildString);
                audio.Headers.Add("App-Locale", account.AppLocale ?? "en");
                audio.Headers.Add("App-Environment", "prod");
                audio.Headers.Add("UDID", mobile.Udid);
                audio.Headers.Add("App-Requested-Features", mobile.FeaturesHeader);
                if (account.DataDomeClientId is { } cookie) audio.Headers.Add("Cookie", "datadome=" + cookie);
            }
            var response = await SendNetworkAsync(audio, cancellationToken).ConfigureAwait(false);
            if (SoundCloudMobileRoutes.IsStream(uri) && account.Equals(session()))
                foreach (var header in new[] { "Set-Cookie", "x-set-cookie" })
                    if (response.Headers.TryGetValues(header, out var cookies))
                        foreach (var value in cookies)
                            if (SoundCloudMobileChallenge.ReadCookie(value) is { } rotated && rotated != account.DataDomeClientId)
                            {
                                account.UpdateDataDomeClientId(rotated); session()!.UpdateDataDomeClientId(rotated); protectionChanged?.Invoke();
                            }
            if (SoundCloudMobileRoutes.IsStream(uri) && (response.StatusCode == System.Net.HttpStatusCode.Forbidden || SoundCloudHttpTransport.Challenge(response, [], uri) != null))
            {
                byte[] bytes;
                try
                {
                    await response.Content.LoadIntoBufferAsync(SoundCloudMobileChallenge.MaxHtmlLength, cancellationToken).ConfigureAwait(false);
                    bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }
                catch { response.Dispose(); throw; }
                if (SoundCloudHttpTransport.Challenge(response, bytes, uri) is { } kind)
                {
                    using (response)
                    {
                        var challenge = SoundCloudMobileChallenge.Parse(response, bytes, uri, account, kind);
                        var denied = new SoundCloudException(kind == SoundCloudChallengeKind.HardBlock ? "SoundCloud заблокировал доступ к мобильному потоку." :
                            "SoundCloud запросил проверку мобильного потока.", (int)response.StatusCode, requiresBrowserVerification: true)
                            { ChallengeKind = kind, MobileChallenge = challenge };
                        if (mobileChallenge == null || challenge == null || challengeRetried) throw denied;
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(TimeSpan.FromMinutes(3));
                        var cookie = await mobileChallenge.SolveAsync(challenge, timeout.Token).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!account.Equals(session())) throw new SoundCloudException("Сессия SoundCloud изменилась. Повтори действие.");
                        if (!WebSession.IsDataDomeClientId(cookie) || cookie == challenge.PreviousCookie) throw denied;
                        account.UpdateDataDomeClientId(cookie!); session()!.UpdateDataDomeClientId(cookie!); protectionChanged?.Invoke();
                    }
                    challengeRetried = true; hop--; continue;
                }
            }
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (hop == 5 || location == null) throw new SoundCloudException("SoundCloud не разрешил редирект аудиопотока.");
            var next = new Uri(uri, location);
            if (!SoundCloudWebClient.IsPlaybackUri(next) || next.AbsolutePath.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                SoundCloudMobileRoutes.IsStream(next) && next.GetLeftPart(UriPartial.Path) != uri.GetLeftPart(UriPartial.Path))
                throw new SoundCloudException("SoundCloud вернул недопустимый редирект аудиопотока.");
            uri = next;
        }
        throw new SoundCloudException("Слишком много редиректов аудиопотока.");
    }

    private async Task<HttpResponseMessage> SendNetworkAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await base.SendAsync(request, timeout.Token).ConfigureAwait(false);
    }
}
