using System.Text.RegularExpressions;

namespace Pookie.App.Messages;

internal static partial class ChatMusicLink
{
    internal static bool IsMusicUrl(string value) => value.Length <= 4096 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.UserInfo.Length == 0 && uri.IsDefaultPort && (uri.Host is "soundcloud.com" or "www.soundcloud.com"
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts &&
              parts[0] is not ("you" or "messages" or "search" or "settings" or "tags" or "pages")
            : uri.Host == "on.soundcloud.com" && uri.AbsolutePath.Length > 1);
    internal static string[] Find(string body) => Links().Matches(body).Select(m => m.Value.TrimEnd('.', ',', ';', ')', ']', '!', '?'))
        .Where(IsMusicUrl).Distinct(StringComparer.Ordinal).Take(4).ToArray();
    internal static string Caption(string body, string[] urls)
    { foreach (var url in urls) body = body.Replace(url, "", StringComparison.Ordinal); return body.Trim(); }
    [GeneratedRegex("https://(?:www\\.)?(?:soundcloud\\.com|on\\.soundcloud\\.com)/[^\\s<>\\\"]+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();
}
