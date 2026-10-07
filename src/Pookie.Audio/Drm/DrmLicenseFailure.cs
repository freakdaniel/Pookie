using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pookie.Audio;

// Never forward license-server error bodies: they may contain URLs or credentials.
internal static class DrmLicenseFailure
{
    internal static string Classify(ReadOnlySpan<byte> body)
    {
        var text = Encoding.UTF8.GetString(body);
        if (text.Contains("vmp validation", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("vmp_validation", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("vmp signature", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("host verification", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("host_verification", StringComparison.OrdinalIgnoreCase)) return "host-verification";
        if (text.Contains("token expired", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("expired token", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("token_expired", StringComparison.OrdinalIgnoreCase)) return "expired-authorization";
        if (text.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("invalid_token", StringComparison.OrdinalIgnoreCase)) return "invalid-authorization";
        if (text.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("captcha-delivery.com", StringComparison.OrdinalIgnoreCase)) return "browser-verification";
        return "unspecified";
    }

    internal static string NumericCode(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "code", "error_code", "errorCode", "status" })
                    if (document.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var code))
                        return code.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (JsonException) { return "none"; }
        return "none";
    }

    internal static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            var category = Classify(buffer.AsSpan(0, count));
            if (Environment.GetEnvironmentVariable("POOKIE_DRM_DIAGNOSTICS") == "1")
                Pookie.Logging.AppLog.For("Pookie.Audio.Drm").Debug(
                    "DRM-лицензия отклонена: HTTP {HttpStatus}; протокол {HttpVersion}; причина {Reason}; код {Code}; проверено байт {InspectedBytes}; CloudFront {CloudFront}",
                    (int)response.StatusCode, response.Version, category, NumericCode(buffer.AsMemory(0, count)), count, response.Headers.Contains("x-amz-cf-id"));
            return category == "host-verification" ? " Сервер сообщил об ошибке проверки DRM-хоста (VMP)." :
                category == "expired-authorization" ? " Сервер сообщил об истечении авторизации лицензии." :
                category == "invalid-authorization" ? " Сервер сообщил о некорректной авторизации лицензии." :
                category == "browser-verification" ? " Сервер запросил браузерную проверку." : "";
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
}
