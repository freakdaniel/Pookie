namespace Pookie.SoundCloud;

public sealed record WebSession(string ClientId, string AccessToken, string UserAgent)
{
    private string? dataDomeClientId;
    public string? DataDomeClientId { get => Volatile.Read(ref dataDomeClientId); init => dataDomeClientId = value; }
    public string? AppVersion { get; init; }
    public string? AppLocale { get; init; }
    public string? SessionId { get; init; }

    // The site's protection tag rotates this session value through x-set-cookie.
    // Keep it on the shared session, including clients used for cancellable requests.
    public void UpdateDataDomeClientId(string value)
    {
        // A completed verification supersedes in-flight responses even when the
        // provider retains the CID. A fresh reference marks that state boundary.
        if (IsDataDomeClientId(value)) Volatile.Write(ref dataDomeClientId, new string(value.AsSpan()));
    }

    // A response only owns the protection state sent with its request. A newer
    // challenge or response may already have replaced that state in parallel.
    public bool TryUpdateDataDomeClientId(string value, string? expected) =>
        IsDataDomeClientId(value) && value != expected &&
        ReferenceEquals(Interlocked.CompareExchange(ref dataDomeClientId, value, expected), expected);

    internal void ShareProtectionFrom(WebSession current) => Volatile.Write(ref dataDomeClientId, current.DataDomeClientId);


    internal static bool IsDataDomeClientId(string? value) => value is { Length: > 0 and <= 4096 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '+' or '/' or '=' or '%');

    // Rotating protection state must not change hash keys used by pending UI
    // actions. The account identity consists of immutable session credentials.
    public bool Equals(WebSession? other) => ReferenceEquals(this, other) || other != null &&
        ClientId == other.ClientId && (SessionId == null ? other.SessionId == null && AccessToken == other.AccessToken : SessionId == other.SessionId) && UserAgent == other.UserAgent &&
        AppVersion == other.AppVersion && AppLocale == other.AppLocale;
    public override int GetHashCode() => HashCode.Combine(ClientId, SessionId ?? AccessToken, UserAgent, AppVersion, AppLocale);

    public bool IsValid() => ClientId is { Length: > 0 and <= 256 } && ClientId.All(char.IsAsciiLetterOrDigit) &&
        AccessToken is { Length: > 0 and <= 4096 } && AccessToken.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') &&
        UserAgent is { Length: > 0 and <= 1024 } && !UserAgent.Any(c => c < 32 || c == 127) &&
        !UserAgent.StartsWith("SoundCloud/", StringComparison.OrdinalIgnoreCase) &&
        (DataDomeClientId == null || IsDataDomeClientId(DataDomeClientId)) &&
        (AppVersion == null || AppVersion is { Length: > 0 and <= 128 } && AppVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '!' or '-' or '_')) &&
        (AppLocale == null || AppLocale is { Length: > 0 and <= 35 } && AppLocale.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) &&
        (SessionId == null || Guid.TryParseExact(SessionId, "N", out _));

    public override string ToString() => "SoundCloud web session (credentials redacted)";

    public WebSession BrowserSnapshot() => this with
        { DataDomeClientId = null };
}
