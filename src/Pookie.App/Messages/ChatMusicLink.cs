using System.Text.RegularExpressions;

namespace Pookie.App.Messages;

internal static partial class ChatMusicLink
{
    internal static bool IsMusicUrl(string value) => value.Length <= 4096 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.UserInfo.Length == 0 && uri.IsDefaultPort && (uri.Host is "soundcloud.com" or "www.soundcloud.com"
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts &&
              parts[0] is not ("you" or "messages" or "search" or "settings" or "tags" or "pages")
            : uri.Host == "on.soundcloud.com" && uri.AbsolutePath.Length > 1);
    private static string Url(Match match) => match.Value.TrimEnd('.', ',', ';', ')', ']', '!', '?');
    internal static string[] Find(string body) => Links().Matches(body).Select(Url)
        .Where(IsMusicUrl).Distinct(StringComparer.Ordinal).Take(32).ToArray();
    internal static IEnumerable<(int Start, string Url)> Occurrences(string body, IEnumerable<string> urls)
    {
        var allowed = urls.ToHashSet(StringComparer.Ordinal);
        foreach (Match match in Links().Matches(body))
        {
            var url = Url(match);
            if (allowed.Contains(url)) yield return (match.Index, url);
        }
    }
    internal static string Caption(string body, string[] urls)
        => string.Join('\n', body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return true;
                line = Links().Replace(line, match => urls.Contains(Url(match), StringComparer.Ordinal) ? match.Value[Url(match).Length..] : match.Value);
                return !string.IsNullOrWhiteSpace(line);
            })).Trim();
    [GeneratedRegex("https://(?:www\\.)?(?:soundcloud\\.com|on\\.soundcloud\\.com)/[^\\s<>\\\"]+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();
}
