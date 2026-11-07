namespace Pookie.SoundCloud;

public sealed record SoundCloudStream(Uri Uri, string Protocol, double Duration)
{
    public string? LicenseAuthToken { get; init; }
    public bool Protected => Protocol is "ctr-encrypted-hls" or "cbc-encrypted-hls";
    public override string ToString() => $"SoundCloud {Protocol} stream (URL redacted)";
}
