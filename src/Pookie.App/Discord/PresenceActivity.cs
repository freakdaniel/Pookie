using System.Text.Json.Serialization;

namespace Pookie.Discord;

internal enum PresenceActivityType { Listening = 2 }
internal enum PresenceStatusDisplay { Details = 2 }
internal sealed class PresenceActivity
{
    public PresenceActivityType Type { get; init; }
    [JsonPropertyName("status_display_type")] public PresenceStatusDisplay StatusDisplay { get; init; }
    public string Details { get; init; } = "";
    public string State { get; init; } = "";
    public PresenceTimestamps? Timestamps { get; init; }
    public PresenceAssets Assets { get; init; } = new();
    public PresenceButton[]? Buttons { get; init; }
}
internal sealed record PresenceTimestamps([property: JsonIgnore] DateTime? Start, [property: JsonIgnore] DateTime? End)
{
    [JsonPropertyName("start")] public long? StartMilliseconds => Start is { } time ? new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeMilliseconds() : null;
    [JsonPropertyName("end")] public long? EndMilliseconds => End is { } time ? new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeMilliseconds() : null;
}
internal sealed class PresenceAssets
{
    [JsonPropertyName("large_image")] public string LargeImageKey { get; init; } = "";
    [JsonPropertyName("small_image")] public string SmallImageKey { get; init; } = "";
    public string SmallImageText { get; init; } = "";
}
internal sealed record PresenceButton
{
    public string Label { get; init; } = "";
    public string Url { get; init; } = "";
}
internal sealed record PresenceHandshake(int V, string ClientId);
internal sealed record PresenceArguments(int Pid, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] PresenceActivity? Activity);
internal sealed record PresenceRequest(string Cmd, PresenceArguments Args, string Nonce);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PresenceHandshake))]
[JsonSerializable(typeof(PresenceRequest))]
internal partial class PresenceJson : JsonSerializerContext;
