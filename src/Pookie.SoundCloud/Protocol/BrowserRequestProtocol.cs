using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

public sealed record BrowserRequestCommand(string Id, string Operation, long UserId = 0, long TrackId = 0, bool Liked = false, string? Url = null, BrowserAudioCommand? Audio = null, TrackReadRequest? Detail = null, bool Reposted = false, long ArtistId = 0, bool Following = false, long PlaylistId = 0, CommentWriteRequest? Comment = null, string? ContextUrn = null, ChatRequest? Chat = null, WebSession? Session = null, PlaylistCreateRequest? Playlist = null)
{
    // 20,000 UTF-16 chat characters may expand to 120 KB in escaped JSON.
    public const int MaxLength = 2097152;
    public bool IsValid() => Id is { Length: 32 } && Id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        (Operation == "playlist-edit" ? Edit?.IsValid() == true && Playlist == null && Session == null && Url == null && Audio == null && Detail == null && Comment == null && ContextUrn == null && Chat == null &&
            UserId == 0 && TrackId == 0 && ArtistId == 0 && PlaylistId == 0 : Edit == null && IsStandardValid());

    public PlaylistEditRequest? Edit { get; init; }
    private bool IsStandardValid() =>
        (Operation == "playlist-create" ? Playlist?.IsValid() == true && Session == null && Url == null && Audio == null && Detail == null && Comment == null && ContextUrn == null && Chat == null &&
            UserId == 0 && TrackId == 0 && ArtistId == 0 && PlaylistId == 0 : Playlist == null &&
        (Operation == "session-update" ? Session?.IsValid() == true &&
            Url == null && Audio == null && Detail == null && Comment == null && ContextUrn == null && Chat == null &&
            UserId == 0 && TrackId == 0 && ArtistId == 0 && PlaylistId == 0 : Session == null &&
        (Operation == "chat" ? Chat?.IsValid() == true && Url == null && Audio == null && Detail == null && Comment == null && ContextUrn == null &&
            UserId == 0 && TrackId == 0 && ArtistId == 0 && PlaylistId == 0 : Chat == null && IsOtherValid())));

    private bool IsOtherValid() => (ContextUrn == null || Operation == "play-history") &&
        (Operation == "comment-write" ? Url == null && Audio == null && Detail == null && UserId == 0 && TrackId == 0 && ArtistId == 0 && PlaylistId == 0 && Comment?.IsValid() == true : Comment == null &&
        (Operation == "track-read" ? Url == null && Audio == null && Detail?.IsValid() == true : Detail == null &&
        (Operation == "audio" ? Url == null && Audio?.IsValid() == true : Audio == null &&
        (Operation == "api-get" ? IsReadUrl(Url) : Url == null && (Operation is "me" or "liked-ids" or "cancel" ||
        Operation == "like" && UserId is > 0 and <= 9007199254740991 && TrackId is > 0 and <= 9007199254740991 ||
        Operation == "repost" && UserId == 0 && TrackId is > 0 and <= 9007199254740991 ||
        Operation == "play-history" && UserId == 0 && ArtistId == 0 && PlaylistId == 0 && TrackId is > 0 and <= 9007199254740991 && IsHistoryContextUrn(ContextUrn) ||
        Operation == "playlist-like" && UserId is > 0 and <= 9007199254740991 && PlaylistId is > 0 and <= 9007199254740991 && TrackId == 0 ||
        Operation == "playlist-repost" && UserId == 0 && PlaylistId is > 0 and <= 9007199254740991 && TrackId == 0 ||
        Operation == "follow" && UserId is > 0 and <= 9007199254740991 && ArtistId is > 0 and <= 9007199254740991 && ArtistId != UserId && TrackId == 0 ||
        Operation == "follow-signature" && UserId is > 0 and <= 9007199254740991 && ArtistId is > 0 and <= 9007199254740991 && ArtistId != UserId && TrackId == 0 && PlaylistId == 0 ||
        Operation == "playlist-add" && UserId is > 0 and <= 9007199254740991 && PlaylistId is > 0 and <= 9007199254740991 && TrackId is > 0 and <= 9007199254740991)))));

    public static bool IsHistoryContextUrn(string? value)
    {
        if (value == null) return true;
        if (value.Length is 0 or > 2048) return false;
        if (value.StartsWith("soundcloud:system-playlists:", StringComparison.Ordinal))
            return value.Length > "soundcloud:system-playlists:".Length && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '-');
        var prefix = value.StartsWith("soundcloud:playlists:", StringComparison.Ordinal) ? "soundcloud:playlists:" : "soundcloud:users:";
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
            long.TryParse(value.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) &&
            id is > 0 and <= 9007199254740991;
    }

    public static bool IsReadUrl(string? value)
    {
        if (value is not { Length: > 0 and <= 8192 } || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !SoundCloudWebClient.IsApiUri(uri) || uri.Fragment != "") return false;
        var path = uri.AbsolutePath;
        return path is "/mixed-selections" or "/me" or "/me/track_likes/ids" or "/me/track_reposts/ids" or "/me/playlist_likes/ids" or "/me/playlist_reposts/ids" or "/search" or "/search/tracks" or "/search/users" or "/search/albums" or "/search/playlists" or "/search/playlists_without_albums" or "/stream" or "/resolve" or "/tracks" or "/me/library/all" or "/me/library/stations" or "/me/play-history/contexts" or "/me/play-history/tracks" ||
            path.StartsWith("/media/", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(path, @"^/users/soundcloud(%3[Aa]|:)users(%3[Aa]|:)[1-9][0-9]*/web-profiles$") ||
            System.Text.RegularExpressions.Regex.IsMatch(path, @"^/system-playlists/soundcloud(%3A|:)system-playlists(%3A|:)[A-Za-z0-9%:_-]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(path, @"^/((tracks|playlists)/[1-9][0-9]*|tracks/[1-9][0-9]*/(comments|related|reposters|albums|playlists_without_albums)|users/[1-9][0-9]*(/(likes|followings|followings/ids|followers|tracks|playlists|playlists_without_albums|albums|comments))?|stream/users/[1-9][0-9]*(/reposts)?)$");
    }
    public override string ToString() => $"Browser command ({Operation}; URL and session redacted)";

}

public sealed record BrowserAudioCommand(string Action, string PlaybackId, string? Source = null, string? Authorization = null,
    double Position = 0, double Volume = 70, bool Paused = false, double NormalizationGainDb = 0, string? PageUrl = null)
{
    public bool IsValid() => (PageUrl == null || Action == "start" && PageUrl.Length <= 8192 && Uri.TryCreate(PageUrl, UriKind.Absolute, out var page) &&
        page.GetLeftPart(UriPartial.Authority) == "https://soundcloud.com" && page.UserInfo == "" && page.Query == "" && page.Fragment == "") &&
        Guid.TryParseExact(PlaybackId, "N", out _) && double.IsFinite(Position) && Position is >= 0 and <= 86400 &&
        double.IsFinite(Volume) && Volume is >= 0 and <= 100 && double.IsFinite(NormalizationGainDb) && NormalizationGainDb is >= -24 and <= 6 && (Action == "start"
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
    public double? NormalizationGainDb { get; init; }
    public int LicenseHttpStatus { get; init; }
    public string? LicenseMessageType { get; init; }
    public string? CdmError { get; init; }
    public string? LicenseFailure { get; init; }
    public bool IsValid() => double.IsFinite(Position) && Position is >= 0 and <= 86400 &&
        double.IsFinite(Duration) && Duration is >= 0 and <= 86400 &&
        double.IsFinite(BufferedStart) && double.IsFinite(BufferedEnd) &&
        BufferedStart >= 0 && BufferedEnd >= BufferedStart && BufferedEnd <= Duration &&
        (NormalizationGainDb == null || double.IsFinite(NormalizationGainDb.Value) && NormalizationGainDb is >= -24 and <= 6) &&
        LicenseHttpStatus is >= 0 and <= 599 &&
        (LicenseMessageType == null || LicenseMessageType is "license-request" or "license-renewal" or "individualization-request" or "license-release") &&
        (CdmError == null || CdmError is "NotSupportedError" or "InvalidStateError" or "InvalidAccessError" or "QuotaExceededError" or "TypeError" or "OperationError" or "NotAllowedError") &&
        (LicenseFailure == null || LicenseFailure is "client-validation" or "authorization" or "protection" or "unknown") &&
        (Error == null || Error is "unsupported" or "network" or "playlist" or "decode" or "license" or "expired" or "autoplay" or "closed") &&
        (Stage == null || Stage is "manifest" or "initialization" or "eme" or "license" or "source" or "buffer" or "segment" or "play" or "ready") &&
        MediaError is >= 0 and <= 4 && ReadyState is >= 0 and <= 4 && NetworkState is >= 0 and <= 3;
}

// Diagnostic metadata is deliberately finite: no URL, header, body, cookie or exception text.
public sealed record BrowserRequestTrace(string Stage, string Method, int FetchNumber, int Attempt,
    int Generation, bool ChallengeActive, bool HasProtection, string Reason = "none")
{
    public bool IsValid() => Stage is "wait-ready" or "wait-check" or "send" or "response" or "retry" or "fetch-error" &&
        Method is "GET" or "POST" or "PUT" or "DELETE" or "PATCH" &&
        FetchNumber is > 0 and <= 1000000 && Attempt is >= 0 and <= 2 && Generation is >= 0 and <= 1000000 &&
        Reason is "none" or "challenge-response" or "challenge-fetch-error";
}

public sealed record BrowserRequestEvent(string Kind, string RequestId = "", int Status = 0, bool Interactive = false,
    SoundCloudUser? User = null, string? ChallengeType = null, long[]? Ids = null, string? DataDomeClientId = null, string? Chunk = null, int ChunkIndex = 0, BrowserAudioState? Audio = null)
{
    public BrowserRequestTrace? Trace { get; init; }
    [JsonIgnore] public string? Json { get; init; }
    public WebSession? Session { get; init; }
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
            if (message.Session is { } session && (string.IsNullOrEmpty(pairing) || message.Kind != "complete" || message.Status is < 200 or >= 300 ||
                message.User is not { Id: > 0 } user || session.VerifiedUserId != user.Id || session.DataDomeClientId != null || !session.IsValid())) return null;
            if (message.ChallengeType != null && message.ChallengeType is not ("device_check" or "device_check_invisible_mode" or "block" or "hard_block" or "unknown")) return null;
            if (message.Kind == "protection-session" && !WebSession.IsDataDomeClientId(message.DataDomeClientId)) return null;
            if (message.Kind != "protection-session" && message.DataDomeClientId != null) return null;
            if (message.Kind == "pass-ignored" && message.ChallengeType != "hard_block") return null;
            if (message.Kind == "blocked" && (message.ChallengeType != "hard_block" || !message.Interactive)) return null;
            if (message.Kind is "complete" or "ids" or "json-chunk" or "audio-state" && !Guid.TryParseExact(message.RequestId, "N", out _)) return null;
            if (message.Kind == "request-trace" && (!Guid.TryParseExact(message.RequestId, "N", out _) || message.Trace?.IsValid() != true)) return null;
            if (message.Kind != "request-trace" && message.Trace != null) return null;
            if (message.Audio != null && (message.Kind is not ("audio-state" or "complete") || !message.Audio.IsValid())) return null;
            if (message.Kind == "audio-state" && message.Audio == null) return null;
            if (message.Kind == "json-chunk" && (message.Chunk is not { Length: > 0 and <= 1024 } || message.ChunkIndex is < 0 or >= 4096)) return null;
            if (message.Kind != "json-chunk" && message.Chunk != null) return null;
            if (message.Ids is { } ids && (ids.Length > 200 || ids.Any(id => id is <= 0 or > 9007199254740991))) return null;
            return message.Kind is "ready" or "complete" or "ids" or "json-chunk" or "audio-state" or "checking" or "blocked" or "passed" or "challenge-error" or "protection-session" or "request-trace" or "pass-ignored" ? message : null;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }
}
