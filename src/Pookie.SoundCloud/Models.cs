using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

public sealed record WebSession(string ClientId, string AccessToken, string UserAgent)
{
    private string? dataDomeClientId;
    public string? DataDomeClientId { get => dataDomeClientId; init => dataDomeClientId = value; }
    public string? AppVersion { get; init; }
    public string? AppLocale { get; init; }
    public SoundCloudMobileIdentity? MobileIdentity { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? SessionId { get; init; }

    // The site's protection tag rotates this session value through x-set-cookie.
    // Keep it on the shared session, including clients used for cancellable requests.
    public void UpdateDataDomeClientId(string value)
    {
        if (IsDataDomeClientId(value)) dataDomeClientId = value;
    }

    internal static bool IsDataDomeClientId(string? value) => value is { Length: > 0 and <= 4096 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '+' or '/' or '=' or '%');

    // Rotating protection state must not change hash keys used by pending UI
    // actions. The account identity consists of immutable session credentials.
    public bool Equals(WebSession? other) => ReferenceEquals(this, other) || other != null &&
        ClientId == other.ClientId && (SessionId == null ? other.SessionId == null && AccessToken == other.AccessToken : SessionId == other.SessionId) && UserAgent == other.UserAgent &&
        AppVersion == other.AppVersion && AppLocale == other.AppLocale && MobileIdentity == other.MobileIdentity;
    public override int GetHashCode() => HashCode.Combine(ClientId, SessionId ?? AccessToken, UserAgent, AppVersion, AppLocale, MobileIdentity);

    public bool IsValid() => ClientId is { Length: > 0 and <= 256 } && ClientId.All(char.IsAsciiLetterOrDigit) &&
        AccessToken is { Length: > 0 and <= 4096 } && AccessToken.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') &&
        UserAgent is { Length: > 0 and <= 1024 } && !UserAgent.Any(c => c < 32 || c == 127) &&
        (DataDomeClientId == null || IsDataDomeClientId(DataDomeClientId)) &&
        (AppVersion == null || AppVersion is { Length: > 0 and <= 128 } && AppVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '!' or '-' or '_')) &&
        (AppLocale == null || AppLocale is { Length: > 0 and <= 35 } && AppLocale.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) &&
        (MobileIdentity == null || MobileIdentity.IsValid() && UserAgent == MobileIdentity.UserAgent) &&
        (SessionId == null || Guid.TryParseExact(SessionId, "N", out _)) &&
        (RefreshToken == null || MobileIdentity != null && RefreshToken is { Length: > 0 and <= 8192 } && RefreshToken.All(c => c is >= '!' and <= '~')) &&
        (ExpiresAtUtc == null || MobileIdentity != null && ExpiresAtUtc > DateTimeOffset.UnixEpoch);

    public override string ToString() => "SoundCloud web session (credentials redacted)";

    public WebSession BrowserSnapshot() => this with
        { DataDomeClientId = null, MobileIdentity = null, RefreshToken = null, ExpiresAtUtc = null };
}

public sealed record SoundCloudUser
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public long? FollowersCount { get; init; }
    public long? TrackCount { get; init; }
    public string? PermalinkUrl { get; init; }
    public string? FullName { get; init; }
    public string? Description { get; init; }
    public string? City { get; init; }
    public string? CountryCode { get; init; }
    public long? FollowingsCount { get; init; }
    public long? LikesCount { get; init; }
    public long? PlaylistCount { get; init; }
    public bool Verified { get; init; }
    public UserBadges? Badges { get; init; }
    public CreatorSubscription? CreatorSubscription { get; init; }
    public CreatorSubscription[]? CreatorSubscriptions { get; init; }
    [JsonIgnore] public bool IsVerified => Verified || Badges?.Verified == true;
    [JsonIgnore] public bool HasArtistPro => Badges?.ProUnlimited == true || IsArtistPro(CreatorSubscription) || CreatorSubscriptions?.Any(IsArtistPro) == true;
    private static bool IsArtistPro(CreatorSubscription? subscription) => subscription?.Product?.Id == "creator-pro-unlimited";
    public UserVisuals? Visuals { get; init; }
    [JsonIgnore] public string? BannerUrl => Visuals?.Visuals?.FirstOrDefault(visual => !string.IsNullOrWhiteSpace(visual.VisualUrl))?.VisualUrl;
}

public sealed record UserBadges(bool Pro = false, bool ProUnlimited = false, bool Verified = false, bool CreatorMidTier = false);
public sealed record CreatorSubscription(CreatorProduct? Product);
public sealed record CreatorProduct(string? Id);

public sealed record SoundCloudTrack
{
    public bool? Commentable { get; init; }
    public long Id { get; init; }
    public string Urn { get; init; } = "";
    public string Title { get; init; } = "";
    public double Duration { get; init; }
    public string? ArtworkUrl { get; init; }
    public string? WaveformUrl { get; init; }
    public TrackVisuals? Visuals { get; init; }
    [JsonIgnore] public string? TrackArtUrl => Visuals is { Enabled: true, Visuals: { } visuals }
        ? visuals.FirstOrDefault(visual => visual is { EntryTime: 0 } && !string.IsNullOrWhiteSpace(visual.VisualUrl))?.VisualUrl : null;
    public string? CreatedAt { get; init; }
    public string? Genre { get; init; }
    public string? Description { get; init; }
    public long? PlaybackCount { get; init; }
    public long? LikesCount { get; init; }
    public long? CommentCount { get; init; }
    public long? RepostsCount { get; init; }
    public string? License { get; init; }
    public string? PermalinkUrl { get; init; }
    public string? Access { get; init; }
    public string? Policy { get; init; }
    public double FullDuration { get; init; }
    public string? TrackAuthorization { get; init; }
    public SoundCloudUser? User { get; init; }
    public TrackMedia? Media { get; init; }
    public PublisherMetadata? PublisherMetadata { get; init; }
    public double DurationSeconds => Duration / 1000;
    public string Author => User?.Username ?? "Unknown artist";
}

public sealed record TrackVisuals(bool? Enabled, TrackVisual[]? Visuals);
public sealed record TrackVisual(string? VisualUrl, double? EntryTime = 0);

public sealed record PublisherMetadata
{
    public string? Artist { get; init; }
    public string? AlbumTitle { get; init; }
    public string? Isrc { get; init; }
}

public sealed record TrackMedia
{
    public Transcoding[] Transcodings { get; init; } = [];
}

public sealed record Transcoding
{
    public string Url { get; init; } = "";
    public string Preset { get; init; } = "";
    public string? Quality { get; init; }
    public bool Snipped { get; init; }
    public TranscodingFormat? Format { get; init; }
}

public sealed record TranscodingFormat
{
    public string Protocol { get; init; } = "";
    public string MimeType { get; init; } = "";
}

public sealed record TrackPage(SoundCloudTrack[] Tracks, string? NextHref);

public sealed record SoundCloudComment
{
    public long Id { get; init; }
    public string Body { get; init; } = "";
    public string? CreatedAt { get; init; }
    public double? Timestamp { get; init; }
    public SoundCloudUser? User { get; init; }
    public SoundCloudComment[] Replies { get; init; } = [];
    public int ReplyCount { get; init; }
    public string? RepliesCursor { get; init; }
}

public sealed record TrackCommentPage(SoundCloudComment[] Comments, string? NextHref);
public sealed record TrackReplyPage(SoundCloudComment[] Comments, int Total, string? Cursor);
public sealed record TrackFan(SoundCloudUser User, long Plays);
public sealed record TrackSidebar(TrackFan[] Fans, LibraryItem[] Playlists, LibraryItem[] Albums, bool FansHidden = false, bool HasMorePlaylists = false);

public sealed record SoundCloudStream(Uri Uri, string Protocol, double Duration)
{
    public string? LicenseAuthToken { get; init; }
    public bool Protected => Protocol is "ctr-encrypted-hls" or "cbc-encrypted-hls";
    public override string ToString() => $"SoundCloud {Protocol} stream (URL redacted)";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SoundCloudPlaylist))]
[JsonSerializable(typeof(SoundCloudTrack))]
[JsonSerializable(typeof(SoundCloudUser))]
[JsonSerializable(typeof(SoundCloudComment))]
[JsonSerializable(typeof(WebSession))]
[JsonSerializable(typeof(SoundCloudMobileIdentity))]
[JsonSerializable(typeof(SoundCloudMobileSignals))]
[JsonSerializable(typeof(SoundCloudMobileOAuthOptions))]
[JsonSerializable(typeof(SoundCloudTokenExchangeClient))]
[JsonSerializable(typeof(MobileAuthorizationRequest))]
[JsonSerializable(typeof(MobileAuthorizationResult))]
[JsonSerializable(typeof(BrowserRequestCommand))]
[JsonSerializable(typeof(TrackReadRequest))]
[JsonSerializable(typeof(CommentWriteRequest))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(BrowserRequestEvent))]
public partial class SoundCloudJson : JsonSerializerContext;

public sealed class SoundCloudException(string message, int statusCode = 0, bool requiresBrowserVerification = false, bool trackUnavailable = false) : Exception(message)
{
    public SoundCloudChallengeKind? ChallengeKind { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public int StatusCode { get; } = statusCode;
    public bool RequiresBrowserVerification { get; } = requiresBrowserVerification;
    // Only fresh track metadata can establish this; a missing/expired CDN URL cannot.
    public bool TrackUnavailable { get; } = trackUnavailable;
}

public enum SoundCloudChallengeKind { Unknown, Captcha, HardBlock, DeviceCheck, InvisibleDeviceCheck, Redirect }
