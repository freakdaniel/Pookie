namespace Pookie.SoundCloud;

public static class PlaylistSharing
{
    public static string? Token(SoundCloudPlaylist playlist)
    {
        if (ValidToken(playlist.SecretToken)) return playlist.SecretToken;
        foreach (var address in new[] { playlist.PermalinkUrl, playlist.Uri })
        {
            if (!System.Uri.TryCreate(address, UriKind.Absolute, out var uri)) continue;
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == "secret_token" && ValidToken(System.Uri.UnescapeDataString(pair[1])))
                    return System.Uri.UnescapeDataString(pair[1]);
            }
            var last = uri.AbsolutePath.TrimEnd('/').Split('/').Last();
            if (uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 4 && ValidToken(last)) return last;
        }
        return null;
    }

    public static Uri? ShareUri(SoundCloudPlaylist playlist)
    {
        if (!System.Uri.TryCreate(playlist.PermalinkUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "soundcloud.com" || uri.UserInfo.Length != 0) return null;
        if (!playlist.IsPrivate) return uri;
        var token = Token(playlist);
        if (token == null) return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length is not (3 or 4) || parts[1] != "sets") return null;
        return new UriBuilder(uri) { Path = "/" + string.Join('/', parts.Take(3)) + "/" + token, Query = "", Fragment = "" }.Uri;
    }

    public static Uri? EmbedUri(SoundCloudPlaylist playlist) => !playlist.IsPrivate ? ShareUri(playlist) :
        playlist.Id > 0 && Token(playlist) is { } token
            ? new Uri($"https://api.soundcloud.com/playlists/{playlist.Id}?secret_token={System.Uri.EscapeDataString(token)}") : null;

    private static bool ValidToken(string? token) => token is { Length: > 2 and <= 256 } && token.StartsWith("s-", StringComparison.Ordinal) &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
