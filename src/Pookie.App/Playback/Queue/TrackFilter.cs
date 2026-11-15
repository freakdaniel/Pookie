using Pookie.SoundCloud;

namespace Pookie.App.Playback;

internal static class TrackFilter
{
    internal static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    internal static bool Matches(SoundCloudTrack track, string? text) => Normalize(text) is not { } filter ||
        track.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        track.Author.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
