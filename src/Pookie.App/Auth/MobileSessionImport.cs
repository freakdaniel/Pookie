using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

internal static class MobileSessionImport
{
    // Opt-in to a separately obtained mobile session. Never relabel a website
    // token or ship the official application's client secret in Pookie.
    public static WebSession? Load()
    {
        if (Environment.GetEnvironmentVariable("POOKIE_SOUNDCLOUD_PROFILE") == "web") return null;
        if (Environment.GetEnvironmentVariable("POOKIE_MOBILE_SESSION_FILE") is not { Length: > 0 } path) return null;
        using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > 65536) throw new InvalidOperationException("Некорректный размер мобильной сессии SoundCloud.");
        var session = JsonSerializer.Deserialize(stream, SoundCloudJson.Default.WebSession);
        if (session?.MobileIdentity == null || !session.IsValid())
            throw new InvalidOperationException("Мобильной сессии нужны собственные client_id, access_token и согласованная mobile_identity.");
        return session;
    }
}
