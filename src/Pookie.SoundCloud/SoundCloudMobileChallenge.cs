using System.Net;
using System.Text;
using System.Text.Json;

namespace Pookie.SoundCloud;

public interface ISoundCloudMobileChallengeHandler
{
    Task<string?> SolveAsync(SoundCloudMobileChallenge challenge, CancellationToken cancellationToken);
}

public sealed record SoundCloudMobileChallengeResult(string? Cookie, bool Blocked = false)
{
    public bool IsValid() => Blocked ? Cookie == null : WebSession.IsDataDomeClientId(Cookie);
    public override string ToString() => "SoundCloud mobile challenge result (cookie redacted)";
}

// Contains only the challenge page and its context. OAuth headers, tokens and
// mutation bodies must never be passed to the challenge renderer.
public sealed record SoundCloudMobileChallenge(SoundCloudChallengeKind Kind, string RequestUrl, string? Url,
    string? Html, string? Language, string? PreviousCookie)
{
    public const int MaxHtmlLength = 524288;
    public bool Interactive => Kind != SoundCloudChallengeKind.InvisibleDeviceCheck;
    public bool IsValid() => Enum.IsDefined(Kind) && Kind != SoundCloudChallengeKind.HardBlock &&
        RequestUrl is { Length: > 0 and <= 16384 } && Uri.TryCreate(RequestUrl, UriKind.Absolute, out var request) && IsApiOrigin(request) &&
        (Language == null || Language is { Length: > 0 and <= 35 } && Language.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) &&
        (PreviousCookie == null || WebSession.IsDataDomeClientId(PreviousCookie)) &&
        (Html == null ? Url is { Length: > 0 and <= 16384 } && Uri.TryCreate(Url, UriKind.Absolute, out var target) && IsChallengeTarget(request, target)
            : Url == null && Html is { Length: > 0 and <= MaxHtmlLength });

    public static bool IsChallengeUri(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo == "" && uri.Fragment == "" &&
        (uri.Host == "captcha-delivery.com" || uri.Host.EndsWith(".captcha-delivery.com", StringComparison.OrdinalIgnoreCase));
    public static bool IsApiOrigin(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo == "" && uri.Fragment == "" &&
        uri.Host is "api-mobile.soundcloud.com" or "api-v2.soundcloud.com" or "graph.soundcloud.com" or "api-auth.soundcloud.com" or "secure.soundcloud.com";
    public static bool IsChallengeTarget(Uri request, Uri target) => IsChallengeUri(target) || IsApiOrigin(target) &&
        request.GetLeftPart(UriPartial.Authority) == target.GetLeftPart(UriPartial.Authority) &&
        target.AbsolutePath.Contains("dduser-challenge", StringComparison.OrdinalIgnoreCase);

    internal static SoundCloudMobileChallenge? Parse(HttpResponseMessage response, byte[] body, Uri request, WebSession account, SoundCloudChallengeKind kind)
    {
        if (kind == SoundCloudChallengeKind.HardBlock) return null;
        string? url = null, html = null;
        if (response.Headers.Location is { } location)
        {
            var target = new Uri(request, location);
            if (IsChallengeTarget(request, target)) url = target.AbsoluteUri;
        }
        if (url == null && response.Content.Headers.ContentType?.MediaType?.Equals("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (body.Length > MaxHtmlLength) return null;
            html = Encoding.UTF8.GetString(body);
        }
        else if (url == null)
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("url", out var value) && value.ValueKind == JsonValueKind.String)
                    url = value.GetString();
            }
            catch (JsonException) { return null; }
        }
        var challenge = new SoundCloudMobileChallenge(kind, request.AbsoluteUri, url, html, account.AppLocale ?? account.MobileIdentity?.AcceptLanguage,
            account.DataDomeClientId);
        return challenge.IsValid() ? challenge : null;
    }

    // The Android SDK calls Android.onCaptchaSuccess with a Set-Cookie string.
    public static string? ReadCookie(string? value)
    {
        if (value is not { Length: > 9 and <= 8192 } || value.Any(c => c is '\r' or '\n') || !value.StartsWith("datadome=", StringComparison.Ordinal)) return null;
        var cookie = value[9..].Split(';', 2)[0];
        return WebSession.IsDataDomeClientId(cookie) ? cookie : null;
    }
    public override string ToString() => "SoundCloud mobile challenge (page and cookie redacted)";
}
