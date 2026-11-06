using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal static class MobileChallengeBridge
{
    public static bool Allows(string? address, SoundCloudMobileChallenge challenge, string? fixture)
    {
        if (fixture == null && challenge.Html != null && address == "about:blank") return true;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo != "" || uri.Fragment.Length > 4096) return false;
        if (fixture != null) return uri.GetLeftPart(UriPartial.Authority) == new Uri(fixture).GetLeftPart(UriPartial.Authority);
        var clean = new UriBuilder(uri) { Fragment = "" }.Uri;
        return SoundCloudMobileChallenge.IsChallengeTarget(new Uri(challenge.RequestUrl), clean) || challenge.Html != null &&
            clean.GetLeftPart(UriPartial.Authority) == new Uri(challenge.RequestUrl).GetLeftPart(UriPartial.Authority);
    }

    public static string Script(SoundCloudMobileChallenge challenge, string pairing, bool webkit, string? fixture)
    {
        var origin = new Uri(fixture ?? challenge.Url ?? challenge.RequestUrl).GetLeftPart(UriPartial.Authority);
        var send = webkit ? "window.webkit.messageHandlers.pookie.postMessage(JSON.stringify(message))" :
            "window.infiniframe.host.postData({id:'pookie:browser-message',command:'Post',version:2,data:message})";
        return $$"""
            (() => {
              const fixedOrigin = {{JsonSerializer.Serialize(origin)}};
              const local = {{JsonSerializer.Serialize(fixture != null)}};
              const apiOrigin = {{JsonSerializer.Serialize(new Uri(challenge.RequestUrl).GetLeftPart(UriPartial.Authority))}};
              const inline = {{JsonSerializer.Serialize(challenge.Html != null)}};
              const apiRedirect = {{JsonSerializer.Serialize(challenge.Url != null && SoundCloudMobileChallenge.IsApiOrigin(new Uri(challenge.Url)))}};
              const provider = location.protocol === 'https:' && (!location.port || location.port === '443') && (location.hostname === 'captcha-delivery.com' || location.hostname.endsWith('.captcha-delivery.com'));
              if (!(local ? location.origin === fixedOrigin : provider || apiRedirect && location.origin === fixedOrigin || inline && (location.origin === apiOrigin || location.href === 'about:blank')) || window.__pookieChallengeBridge) return;
              window.__pookieChallengeBridge = true;
              const pairing = {{JsonSerializer.Serialize(pairing)}};
              function post(kind, value) {
                const message = {kind, value, pairing};
                try { {{send}}; } catch { }
              }
              const previous = {{JsonSerializer.Serialize(challenge.PreviousCookie)}};
              if (inline && location.origin === apiOrigin && previous) document.cookie = 'datadome=' + previous + '; path=/; Secure; SameSite=None';
              window.Android = Object.freeze({
                onCaptchaSuccess(value) { if (typeof value === 'string' && value.length <= 8192) post('success', value); },
                onAdditionalChallengeRedirection(value) { if (Number.isInteger(value)) post('redirect', value); }
              });
              // The desktop renderer can receive DataDome's web page variant.
              // Its completed challenge is reported to parent with postMessage,
              // using the same passed/responseType/cookie envelope as tags.js.
              window.addEventListener('message', event => {
                if (!event.isTrusted || !event.source) return;
                try {
                  const source = new URL(event.origin);
                  const trusted = local ? source.origin === fixedOrigin : source.protocol === 'https:' &&
                    (!source.port || source.port === '443') && (source.hostname === 'captcha-delivery.com' || source.hostname.endsWith('.captcha-delivery.com'));
                  if (!trusted || typeof event.data !== 'string' || event.data.length > 16384) return;
                  const message = JSON.parse(event.data);
                  if (message?.eventType === 'passed' && typeof message.responseType === 'string' && message.responseType.length > 0 && message.responseType.length <= 64 &&
                      typeof message.cookie === 'string' && message.cookie.length <= 8192) post('success', message.cookie);
                } catch { }
              });
            })();
            """;
    }

    public static (SoundCloudMobileChallengeResult? Result, bool? Visible) Read(string raw, string? source,
        SoundCloudMobileChallenge challenge, string pairing, string? fixture)
    {
        if (raw.Length > 16384 || !Allows(source, challenge, fixture)) return default;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pairing", out var nonce) || nonce.ValueKind != JsonValueKind.String || nonce.GetString() != pairing ||
                !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || !root.TryGetProperty("value", out var value)) return default;
            if (kind.GetString() == "success" && value.ValueKind == JsonValueKind.String && SoundCloudMobileChallenge.ReadCookie(value.GetString()) is { } cookie &&
                cookie != challenge.PreviousCookie) return (new(cookie), null);
            if (kind.GetString() == "redirect" && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var code))
                return (code & 255) == 2 ? (new(null, Blocked: true), null) : (null, (code & 255) != 3 || (code & 256) == 0);
        }
        catch (JsonException) { }
        return default;
    }
}
