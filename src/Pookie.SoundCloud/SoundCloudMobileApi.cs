using System.Collections.Frozen;
using System.Text.Json;

namespace Pookie.SoundCloud;

public interface ISoundCloudMobileApi
{
    Task<SoundCloudMobileConfiguration> GetMobileConfigurationAsync(CancellationToken cancellationToken = default);
    Task<JsonDocument> GetMobileSduiAsync(bool catalog = false, string? after = null, CancellationToken cancellationToken = default);
    Task<JsonDocument> ResolveMobileStreamAsync(Uri uri, CancellationToken cancellationToken = default);
}

public sealed class SoundCloudMobileConfiguration
{
    private readonly FrozenDictionary<string, bool> features;
    private SoundCloudMobileConfiguration(Dictionary<string, bool> features) => this.features = features.ToFrozenDictionary(StringComparer.Ordinal);
    public bool? Enabled(string name) => features.TryGetValue(name, out var enabled) ? enabled : null;
    public int Count => features.Count;
    public static SoundCloudMobileConfiguration Parse(JsonElement root)
    {
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("features", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new SoundCloudException("SoundCloud вернул некорректную мобильную конфигурацию.");
        foreach (var entry in entries.EnumerateArray())
        {
            if (flags.Count >= 4096) throw new SoundCloudException("Слишком большая мобильная конфигурация SoundCloud.");
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() is { Length: > 0 and <= 256 } key && entry.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                flags[key] = enabled.GetBoolean();
        }
        return new(flags);
    }
}

public sealed partial class SoundCloudHttpTransport : ISoundCloudMobileApi
{
    public Task<JsonDocument> ResolveMobileStreamAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (Account().MobileIdentity == null || !SoundCloudMobileRoutes.IsStream(uri))
            throw new SoundCloudException("Некорректный адрес мобильного аудиопотока.");
        return ExecuteAsync(HttpMethod.Get, uri, null, null, cancellationToken);
    }

    private readonly object configurationGate = new();
    private Task<SoundCloudMobileConfiguration>? configuration;
    private WebSession? configurationAccount;
    private DateTimeOffset configurationUntil;
    private bool homeSduiUnavailable;
    private bool catalogSduiUnavailable;

    public Task<SoundCloudMobileConfiguration> GetMobileConfigurationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var account = Account();
        if (account.MobileIdentity == null) throw new SoundCloudException("Для Android configuration нужен мобильный профиль.");
        lock (configurationGate)
        {
            if (configuration == null || configuration.IsFaulted || configuration.IsCanceled || !account.Equals(configurationAccount) ||
                configuration.IsCompleted && configurationUntil <= clock.GetUtcNow())
            {
                configurationAccount = account; configurationUntil = clock.GetUtcNow().AddHours(1);
                homeSduiUnavailable = catalogSduiUnavailable = false;
                configuration = LoadConfigurationAsync();
            }
            return configuration.WaitAsync(cancellationToken);
        }
    }

    private async Task<SoundCloudMobileConfiguration> LoadConfigurationAsync()
    {
        using var json = await ExecuteAsync(HttpMethod.Get, SoundCloudMobileRoutes.Uri("/configuration/android"), null, null, lifetime.Token).ConfigureAwait(false);
        return SoundCloudMobileConfiguration.Parse(json.RootElement);
    }

    public async Task<JsonDocument> GetMobileSduiAsync(bool catalog = false, string? after = null, CancellationToken cancellationToken = default)
    {
        if (after is { Length: > 2048 } || after?.Any(char.IsControl) == true || catalog && after != null) throw new ArgumentException("Некорректный курсор SDUI.");
        var flags = await GetMobileConfigurationAsync(cancellationToken).ConfigureAwait(false);
        lock (configurationGate)
            if (catalog ? catalogSduiUnavailable : homeSduiUnavailable) throw new NotSupportedException("Мобильный persisted query недоступен в текущей конфигурации.");
        if (flags.Enabled("sdui_persisted_queries") == false || !catalog && flags.Enabled("sdui_graph_home") == false)
            throw new NotSupportedException("Мобильный SDUI отключён конфигурацией сервера.");
        var body = Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("operationName", catalog ? "CatalogSduiQuery" : "HomeSduiQuery");
            writer.WriteStartObject("variables");
            if (!catalog)
            {
                writer.WriteStartObject("input"); writer.WriteBoolean("useLiteLayout", true); writer.WriteNumber("first", 20);
                if (after == null) writer.WriteNull("after"); else writer.WriteString("after", after);
                writer.WriteNull("actionParams"); writer.WriteEndObject();
            }
            writer.WriteEndObject(); writer.WriteStartObject("extensions"); writer.WriteStartObject("persistedQuery");
            writer.WriteNumber("version", 1); writer.WriteString("sha256Hash", catalog
                ? "34dd1fa2b0cfcf0763822051190d187b22af82dc7f643970a9beac0a4c74c084"
                : "a2e83962341cd01e02a0bda9141f3c2c47c9002e9110114c3b24565e5cedc248");
            writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndObject();
        });
        var result = await ExecuteAsync(HttpMethod.Post, new("https://graph.soundcloud.com/graphql"), body, null, cancellationToken).ConfigureAwait(false);
        if (result.RootElement.ValueKind == JsonValueKind.Object && result.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            result.Dispose();
            lock (configurationGate) { if (catalog) catalogSduiUnavailable = true; else homeSduiUnavailable = true; }
            throw new NotSupportedException("SoundCloud отклонил мобильный persisted query.");
        }
        return result;
    }
}
