using System.Text.RegularExpressions;

namespace Pookie.SoundCloud;

// Android endpoints have their own host and URN-shaped path parameters. Keep
// this separate from the web/V2 allowlist: cursors must not select write routes.
public static class SoundCloudMobileRoutes
{
    public static Uri Uri(string path) => new("https://api-mobile.soundcloud.com" + path);
    public static string Urn(string kind, long id)
    {
        if (kind is not ("tracks" or "playlists" or "users") || id is <= 0 or > 9007199254740991) throw new ArgumentOutOfRangeException(nameof(id));
        return $"soundcloud:{kind}:{id}";
    }
    public static bool IsOrigin(System.Uri uri) => uri.Scheme == "https" && uri.Host == "api-mobile.soundcloud.com" &&
        uri.Port == 443 && uri.UserInfo == "" && uri.Fragment == "";
    public static bool IsStream(System.Uri uri) => IsOrigin(uri) && Regex.IsMatch(uri.AbsolutePath,
        @"^/tracks/soundcloud:tracks:[1-9][0-9]*/streams/(https|hls)$");
    public static bool IsRead(System.Uri uri) => IsOrigin(uri) && (uri.AbsolutePath == "/configuration/android" ||
        Regex.IsMatch(uri.AbsolutePath, @"^/followings/soundcloud:users:[1-9][0-9]*/users$") ||
        Regex.IsMatch(uri.AbsolutePath, @"^/users/soundcloud:users:[1-9][0-9]*/((playlists|albums)/posted|likes|reposts)$") ||
        Regex.IsMatch(uri.AbsolutePath, @"^/playlists/soundcloud:playlists:[1-9][0-9]*/(info|tracks)$"));
    public static bool IsAllowed(System.Uri uri, HttpMethod method)
    {
        if (!IsOrigin(uri)) return false;
        if (method == HttpMethod.Get || method == HttpMethod.Head) return IsRead(uri) || IsStream(uri);
        var path = uri.AbsolutePath;
        if (path == "/playlists") return method == HttpMethod.Post;
        if (Regex.IsMatch(path, @"^/playlists/soundcloud:playlists:[1-9][0-9]*$"))
            return method == HttpMethod.Put || method == HttpMethod.Delete;
        if (Regex.IsMatch(path, @"^/playlists/soundcloud:playlists:[1-9][0-9]*/artwork$")) return method == HttpMethod.Put;
        if (Regex.IsMatch(path, @"^/playlists/soundcloud:playlists:[1-9][0-9]*/tracks/soundcloud:tracks:[1-9][0-9]*$"))
            return method == HttpMethod.Delete;
        if (Regex.IsMatch(path, @"^/likes/(tracks|playlists)/(create|delete)$")) return method == HttpMethod.Post;
        if (Regex.IsMatch(path, @"^/follows/users/soundcloud:users:[1-9][0-9]*$") ||
            Regex.IsMatch(path, @"^/reposts/(tracks|playlists)/soundcloud:\1:[1-9][0-9]*$"))
            return method == HttpMethod.Post || method == HttpMethod.Delete;
        return method == HttpMethod.Post && Regex.IsMatch(path, @"^/playlists/soundcloud:playlists:[1-9][0-9]*/tracks$");
    }
}
