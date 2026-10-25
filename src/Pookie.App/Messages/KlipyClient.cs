using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Pookie.App.Messages;

internal sealed record KlipyGif(string Slug, string Url, string PreviewUrl);
internal sealed class KlipyClient : IDisposable
{
    private readonly HttpClient http;
    private static string? developmentApiKey;
    internal string? ApiKey { get; set; } = developmentApiKey;
    private readonly string customer = Guid.NewGuid().ToString("N");
    internal bool Available => !string.IsNullOrWhiteSpace(ApiKey);
    // Read local development configuration before constructing the UI. Disable
    // file watching and dispose providers; credentials never become app resources.
    internal static void LoadDevelopmentApiKey()
    {
        try
        {
            using var configuration = new ConfigurationManager();
            configuration.AddUserSecrets(typeof(KlipyClient).Assembly, optional: true, reloadOnChange: false);
            developmentApiKey = configuration["Klipy:ApiKey"];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            developmentApiKey = null;
        }
    }
    internal KlipyClient(HttpMessageHandler? handler = null)
    {
        if (handler == null)
        {
            var native = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            if (Environment.GetEnvironmentVariable("POOKIE_PROXY") is { Length: > 0 } proxy) native.Proxy = new WebProxy(proxy);
            handler = native;
        }
        http = new(handler) { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Pookie/1.0");
    }
    internal async Task<KlipyGif[]> SearchAsync(string query, CancellationToken token)
    {
        if (!Available) throw new InvalidOperationException("Для поиска GIF нужен ключ KLIPY в .NET User Secrets (Klipy:ApiKey).");
        var endpoint = string.IsNullOrWhiteSpace(query) ? "trending" : "search";
        var url = $"https://api.klipy.com/api/v1/{Uri.EscapeDataString(ApiKey!)}/gifs/{endpoint}?page=1&per_page=12&customer_id={customer}&locale=ru_RU&q={Uri.EscapeDataString(query)}";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("KLIPY не вернул GIF. Проверь ключ или попробуй позже.");
        using var json = JsonDocument.Parse(await ChatMediaHosting.ReadBoundedAsync(response.Content, 1024 * 1024, token));
        return Parse(json.RootElement);
    }
    internal static KlipyGif[] Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)) return [];
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var nested)) data = nested;
        if (data.ValueKind != JsonValueKind.Array) return [];
        var result = new List<KlipyGif>();
        foreach (var item in data.EnumerateArray().Take(12))
        {
            if (!item.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String || slug.GetString() is not { Length: > 0 and <= 256 } id ||
                !item.TryGetProperty("file", out var file)) continue;
            var full = FindFormat(file, "md", "gif") ?? FindFormat(file, "sm", "gif") ?? FindFormat(file, "hd", "gif");
            var preview = FindFormat(file, "xs", "gif") ?? FindFormat(file, "sm", "gif") ?? full;
            if (full != null && preview != null) result.Add(new(id, full, preview));
        }
        return result.ToArray();
    }
    private static string? FindFormat(JsonElement file, string size, string type) =>
        file.ValueKind == JsonValueKind.Object && file.TryGetProperty(size, out var group) && group.ValueKind == JsonValueKind.Object &&
        group.TryGetProperty(type, out var media) && media.ValueKind == JsonValueKind.Object && media.TryGetProperty("url", out var url) &&
        url.ValueKind == JsonValueKind.String && IsMediaUrl(url.GetString()) ? url.GetString() : null;
    internal static bool IsMediaUrl(string? value) => value is { Length: > 0 and <= 8192 } && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo == "" && uri.Fragment == "" &&
        uri.Host is "static.klipy.com" or "static1.klipy.com" or "static2.klipy.com";
    internal async Task<byte[]> DownloadAsync(string url, CancellationToken token)
    {
        if (!IsMediaUrl(url)) throw new InvalidDataException("Недопустимый адрес GIF.");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return await ChatMediaHosting.ReadBoundedAsync(response.Content, 8 * 1024 * 1024, token);
    }
    internal async Task SharedAsync(string slug, CancellationToken token)
    {
        if (!Available || slug.Length > 256) return;
        using var response = await http.PostAsync($"https://api.klipy.com/api/v1/{Uri.EscapeDataString(ApiKey!)}/gifs/share/{Uri.EscapeDataString(slug)}?customer_id={customer}", null, token);
    }
    public void Dispose() => http.Dispose();
}
