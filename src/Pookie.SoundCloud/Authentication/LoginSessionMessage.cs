using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Pookie.SoundCloud;

public static class LoginSessionMessage
{
    public static WebSession? Parse(string raw, string? source, string allowedOrigin = "https://soundcloud.com", string? expectedPairing = null)
    {
        if (raw.Length > 10000 || !Uri.TryCreate(source, UriKind.Absolute, out var sender) ||
            sender.GetLeftPart(UriPartial.Authority) != allowedOrigin || sender.UserInfo.Length != 0) return null;
        try
        {
            using var json = JsonDocument.Parse(raw);
            var envelope = json.RootElement;
            if (expectedPairing != null)
            {
                var pairing = envelope.GetProperty("pookie_pairing").GetString();
                if (pairing == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pairing), Encoding.UTF8.GetBytes(expectedPairing))) return null;
            }
            if (envelope.GetProperty("id").GetString() != "pookie:web-session" || envelope.GetProperty("command").GetString() != "Post" ||
                envelope.GetProperty("version").GetInt32() != 2) return null;
            var session = envelope.GetProperty("data").Deserialize(SoundCloudJson.Default.WebSession);
            return session != null && session.IsValid() ? session : null;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

}
