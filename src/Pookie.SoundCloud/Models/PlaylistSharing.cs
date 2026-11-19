namespace Pookie.SoundCloud;

public static class PlaylistSharing
{
    public static string? Token(SoundCloudPlaylist playlist)
    {
        if (ValidToken(playlist.SecretToken)) return playlist.SecretToken;
        foreach (var address in new[] { playlist.PermalinkUrl, playlist.Uri })
        {
            if (System.Uri.TryCreate(address, UriKind.Absolute, out var uri) && TokenFromUri(uri) is { } token) return token;
        }
        return null;
    }

    internal static string? TokenFromUri(Uri uri)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || pair[0] != "secret_token") continue;
            var token = System.Uri.UnescapeDataString(pair[1]);
            if (ValidToken(token)) return token;
        }
        return uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, "sets", _, var last] && ValidToken(last) ? last : null;
    }

    public static Uri? ShareUri(SoundCloudPlaylist playlist)
    {
        if (playlist.IsSystemMix)
        {
            if (playlist.IsPublic != true) return null;
            if (System.Uri.TryCreate(playlist.PermalinkUrl, UriKind.Absolute, out var mix) &&
                mix.Scheme == "https" && mix.Host == "soundcloud.com" && mix.IsDefaultPort && mix.UserInfo.Length == 0 &&
                mix.AbsolutePath.StartsWith("/discover/sets/", StringComparison.Ordinal)) return mix;
            return playlist.Permalink is { Length: > 0 } slug && slug.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_')
                ? new Uri("https://soundcloud.com/discover/sets/" + slug) : null;
        }
        if (!System.Uri.TryCreate(playlist.PermalinkUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "soundcloud.com" || uri.UserInfo.Length != 0) return null;
        if (!playlist.IsPrivate) return uri;
        var token = Token(playlist);
        if (token == null) return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length is not (3 or 4) || parts[1] != "sets") return null;
        return new UriBuilder(uri) { Path = "/" + string.Join('/', parts.Take(3)) + "/" + token, Query = "", Fragment = "" }.Uri;
    }

    public static Uri? EmbedUri(SoundCloudPlaylist playlist) => playlist.IsSystemMix ? null : !playlist.IsPrivate ? ShareUri(playlist) :
        playlist.Id > 0 && Token(playlist) is { } token
            ? new Uri($"https://api.soundcloud.com/playlists/{playlist.Id}?secret_token={System.Uri.EscapeDataString(token)}") : null;

    private static bool ValidToken(string? token) => token is { Length: > 2 and <= 256 } && token.StartsWith("s-", StringComparison.Ordinal) &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
