
namespace Pookie.Discord;

public sealed record PresenceTrack(string Title, string Artist, string? Artwork, string? Url,
    double Position, double Duration, bool Playing, string? ArtistUrl = null);

public sealed class PresenceService : IDisposable
{
    // Retain the existing Discord application's registered assets and client identity.
    private const string ApplicationId = "1090770350251458592";
    private readonly PresenceRpcClient client = new(ApplicationId);
    private string? lastTrack;
    private bool enabled = true;
    public PresenceService() => client.Initialize();

    public bool Enabled
    {
        get => enabled;
        set { enabled = value; if (!value) Clear(); }
    }

    public void Update(PresenceTrack track)
    {
        if (!enabled || !track.Playing) { Clear(); return; }
        var presence = CreatePresence(track, DateTime.UtcNow);
        if (presence == null) { Clear(); return; }
        // Discord does not need a new RPC message on every UI timer tick.
        var identity = $"{track.Url}\n{track.Title}\n{track.Artist}\n{track.ArtistUrl}";
        var expectedStart = presence.Timestamps!.Start!.Value;
        var previousStart = client.CurrentPresence?.Timestamps?.Start;
        if (identity == lastTrack && previousStart != null && Math.Abs((expectedStart - previousStart.Value).TotalSeconds) < 2) return;
        client.SetPresence(presence);
        lastTrack = identity;
    }

    public void Clear() { if (lastTrack == null) return; client.ClearPresence(); lastTrack = null; }

    internal static PresenceActivity? CreatePresence(PresenceTrack track, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || !double.IsFinite(track.Duration) || track.Duration <= 0
            || !double.IsFinite(track.Position)) return null;
        var start = now.AddSeconds(-Math.Clamp(track.Position, 0, track.Duration));
        // Field links allow 256 characters. Omit longer URLs so Discord can
        // still accept the activity.
        var fieldUrl = SoundCloudUrl(track.Url, 256);
        var artwork = Uri.TryCreate(track.Artwork, UriKind.Absolute, out var image) && image.Scheme == "https"
            && image.Host.EndsWith(".sndcdn.com", StringComparison.OrdinalIgnoreCase) && image.UserInfo.Length == 0 && image.IsDefaultPort
            ? image.AbsoluteUri : "soundcloud-logo";
        return new PresenceActivity
        {
            Type = PresenceActivityType.Listening,
            StatusDisplay = PresenceStatusDisplay.Details,
            Details = Short(track.Title), State = Short(track.Artist),
            DetailsUrl = fieldUrl,
            StateUrl = SoundCloudUrl(track.ArtistUrl, 256),
            Timestamps = new(start, start.AddSeconds(track.Duration)),
            Assets = new()
            {
                LargeImageKey = artwork, LargeImageUrl = fieldUrl,
                SmallImageKey = "soundcloud-logo", SmallImageText = "SoundCloud", SmallImageUrl = "https://soundcloud.com"
            }
        };
    }
    private static string? SoundCloudUrl(string? url, int maxLength) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.Host == "soundcloud.com" && uri.UserInfo.Length == 0 && uri.IsDefaultPort
        && uri.AbsoluteUri.Length <= maxLength ? uri.AbsoluteUri : null;

    private static string Short(string text)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length < 2 ? clean + "⠀⠀" : clean[..Math.Min(clean.Length, 128)];
    }
    public void Dispose() => client.Dispose();
}
