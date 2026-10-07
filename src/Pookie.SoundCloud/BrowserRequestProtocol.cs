using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

public sealed record BrowserRequestCommand(string Id, string Operation, long UserId = 0, long TrackId = 0, bool Liked = false, string? Url = null, BrowserAudioCommand? Audio = null)
{
    public const int MaxLength = 65536;
    public bool IsValid() => Id is { Length: 32 } && Id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        (Operation == "audio" ? Url == null && Audio?.IsValid() == true : Audio == null &&
        (Operation == "api-get" ? IsReadUrl(Url) : Url == null && (Operation is "me" or "liked-ids" or "cancel" ||
        Operation == "like" && UserId is > 0 and <= 9007199254740991 && TrackId is > 0 and <= 9007199254740991)));

    public static bool IsReadUrl(string? value)
    {
        if (value is not { Length: > 0 and <= 8192 } || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !SoundCloudWebClient.IsApiUri(uri) || uri.Fragment != "") return false;
        var path = uri.AbsolutePath;
        return path is "/me" or "/me/track_likes/ids" or "/search" or "/search/tracks" or "/search/users" or "/search/albums" or "/search/playlists" or "/search/playlists_without_albums" or "/stream" or "/resolve" or "/tracks" or "/me/library/all" or "/me/library/stations" or "/me/play-history/contexts" or "/me/play-history/tracks" ||
            path.StartsWith("/media/", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(path, @"^/system-playlists/soundcloud(%3A|:)system-playlists(%3A|:)[A-Za-z0-9%:_-]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(path, @"^/((tracks|playlists)/[1-9][0-9]*|users/[1-9][0-9]*/(likes|followings|tracks))$");
    }
    public override string ToString() => $"Browser command ({Operation}; URL and session redacted)";

}

public sealed record BrowserAudioCommand(string Action, string PlaybackId, string? Source = null, string? Authorization = null,
    double Position = 0, double Volume = 70, bool Paused = false)
{
    public bool IsValid() => Guid.TryParseExact(PlaybackId, "N", out _) && double.IsFinite(Position) && Position is >= 0 and <= 86400 &&
        double.IsFinite(Volume) && Volume is >= 0 and <= 100 && (Action == "start"
        ? Source is { Length: > 0 and <= 8192 } && Uri.TryCreate(Source, UriKind.Absolute, out var uri) &&
          SoundCloudWebClient.IsMediaUri(uri) && uri.Fragment == "" && Authorization is { Length: > 0 and <= 16384 } &&
          !Authorization.Any(c => c < 32 || c == 127)
        : Source == null && Authorization == null && Action is "stop" or "pause" or "volume" or "seek");
    public override string ToString() => $"Browser audio ({Action}; source and authorization redacted)";
}

public sealed record BrowserAudioState(double Position, double Duration, bool Playing, bool Buffering, bool Ended, string? Error = null, string? Stage = null,
    int MediaError = 0, int ReadyState = 0, int NetworkState = 0)
{
    public double BufferedStart { get; init; }
    public double BufferedEnd { get; init; }
    public bool IsValid() => double.IsFinite(Position) && Position is >= 0 and <= 86400 &&
        double.IsFinite(Duration) && Duration is >= 0 and <= 86400 &&
        double.IsFinite(BufferedStart) && double.IsFinite(BufferedEnd) &&
        BufferedStart >= 0 && BufferedEnd >= BufferedStart && BufferedEnd <= Duration &&
        (Error == null || Error is "unsupported" or "network" or "playlist" or "decode" or "license" or "expired" or "autoplay" or "closed") &&
        (Stage == null || Stage is "manifest" or "initialization" or "eme" or "license" or "source" or "buffer" or "segment" or "play" or "ready") &&
        MediaError is >= 0 and <= 4 && ReadyState is >= 0 and <= 4 && NetworkState is >= 0 and <= 3;
}

public sealed record BrowserRequestEvent(string Kind, string RequestId = "", int Status = 0, bool Interactive = false,
    SoundCloudUser? User = null, string? ChallengeType = null, long[]? Ids = null, string? DataDomeClientId = null, string? Chunk = null, int ChunkIndex = 0, BrowserAudioState? Audio = null)
{
    [JsonIgnore] public string? Json { get; init; }
    public override string ToString() => $"Browser event ({Kind}, HTTP {Status}; session redacted)";
}

public static class BrowserRequestProtocol
{
    public static BrowserRequestEvent? Parse(string raw, string? source, string origin, string? pairing = null)
    {
        if (raw.Length > 10000 || !Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.UserInfo != "" ||
            uri.GetLeftPart(UriPartial.Authority) != origin) return null;
        try
        {
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            if (root.GetProperty("id").GetString() != "pookie:web-api" || root.GetProperty("version").GetInt32() != 1) return null;
            if (pairing != null)
            {
                var value = root.GetProperty("pookie_pairing").GetString();
                if (value == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(pairing))) return null;
            }
            var message = root.GetProperty("data").Deserialize(SoundCloudJson.Default.BrowserRequestEvent);
            if (message == null || message.Status is < 0 or > 599) return null;
            if (message.ChallengeType != null && message.ChallengeType is not ("device_check" or "device_check_invisible_mode" or "block" or "hard_block" or "unknown")) return null;
            if (message.Kind == "protection-session" && !WebSession.IsDataDomeClientId(message.DataDomeClientId)) return null;
            if (message.Kind != "protection-session" && message.DataDomeClientId != null) return null;
            if (message.Kind == "blocked" && (message.ChallengeType != "hard_block" || !message.Interactive)) return null;
            if (message.Kind is "complete" or "ids" or "json-chunk" or "audio-state" && !Guid.TryParseExact(message.RequestId, "N", out _)) return null;
            if (message.Audio != null && (message.Kind is not ("audio-state" or "complete") || !message.Audio.IsValid())) return null;
            if (message.Kind == "audio-state" && message.Audio == null) return null;
            if (message.Kind == "json-chunk" && (message.Chunk is not { Length: > 0 and <= 1024 } || message.ChunkIndex is < 0 or >= 4096)) return null;
            if (message.Kind != "json-chunk" && message.Chunk != null) return null;
            if (message.Ids is { } ids && (ids.Length > 200 || ids.Any(id => id is <= 0 or > 9007199254740991))) return null;
            return message.Kind is "ready" or "complete" or "ids" or "json-chunk" or "audio-state" or "checking" or "blocked" or "passed" or "challenge-error" or "protection-session" ? message : null;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }
}
