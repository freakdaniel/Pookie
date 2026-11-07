using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SoundCloudPlaylist))]
[JsonSerializable(typeof(SoundCloudTrack))]
[JsonSerializable(typeof(SoundCloudUser))]
[JsonSerializable(typeof(SoundCloudComment))]
[JsonSerializable(typeof(WebSession))]
[JsonSerializable(typeof(BrowserRequestCommand))]
[JsonSerializable(typeof(TrackReadRequest))]
[JsonSerializable(typeof(CommentWriteRequest))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(BrowserRequestEvent))]
public partial class SoundCloudJson : JsonSerializerContext;
