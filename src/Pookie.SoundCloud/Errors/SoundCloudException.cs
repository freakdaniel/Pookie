namespace Pookie.SoundCloud;

public sealed class SoundCloudException(string message, int statusCode = 0, bool requiresBrowserVerification = false, bool trackUnavailable = false) : Exception(message)
{
    public SoundCloudChallengeKind? ChallengeKind { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public int StatusCode { get; } = statusCode;
    public bool RequiresBrowserVerification { get; } = requiresBrowserVerification;
    public bool SessionRejected { get; init; }
    // Only fresh track metadata can establish this; a missing/expired CDN URL cannot.
    public bool TrackUnavailable { get; } = trackUnavailable;
}

public enum SoundCloudChallengeKind { Unknown, Captcha, HardBlock, DeviceCheck, InvisibleDeviceCheck, Redirect }
