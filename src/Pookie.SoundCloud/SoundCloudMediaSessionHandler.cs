using System.Net.Http.Headers;

namespace Pookie.SoundCloud;

// Audio authentication is restricted to CDN and exact Android stream routes.
// The inner HTTP handler must have automatic redirects disabled.
public sealed class SoundCloudMediaSessionHandler(Func<WebSession?> session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head || request.RequestUri is not { } uri ||
            !SoundCloudWebClient.IsPlaybackUri(uri) || session() is not { MobileIdentity: not null } account)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!account.IsValid()) throw new SoundCloudException("Некорректная мобильная сессия аудиопотока.");
        for (var hop = 0; hop <= 5; hop++)
        {
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
            var response = await base.SendAsync(audio, cancellationToken).ConfigureAwait(false);
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
}
