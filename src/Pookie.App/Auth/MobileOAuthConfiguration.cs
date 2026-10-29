using System.Text.Json;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

internal static class MobileOAuthConfiguration
{
    public static async Task<WebSession?> TryExchangeLegacyAsync(HttpClient http, SoundCloudMobileOAuthOptions options, CancellationToken token)
    {
        if (options.ExchangeClient == null) return null;
        using var sourceVault = SessionVault.Open(new AppDataPaths());
        var source = await Task.Run(sourceVault.Load, token);
        if (source?.ClientId != options.ExchangeClient.ClientId) return null;
        return await new SoundCloudMobileOAuth(http, options).ExchangeTokenAsync(source, token);
    }

    public static SoundCloudMobileOAuthOptions? Load()
    {
        if (Environment.GetEnvironmentVariable("POOKIE_SOUNDCLOUD_PROFILE") == "web") return null;
        var explicitPath = Environment.GetEnvironmentVariable("POOKIE_MOBILE_AUTH_FILE");
        var path = explicitPath ?? Path.Combine(AppDataPaths.DefaultRoot(), "Config", "mobile-oauth.json");
        if (explicitPath == null && !File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > 65536) throw new InvalidOperationException("Некорректный размер конфигурации мобильного OAuth.");
        var options = JsonSerializer.Deserialize(stream, SoundCloudJson.Default.SoundCloudMobileOAuthOptions);
        if (options?.IsValid() != true) throw new InvalidOperationException("Для мобильного входа нужны client_id, client_secret и mobile_identity.");
        return options;
    }
}
