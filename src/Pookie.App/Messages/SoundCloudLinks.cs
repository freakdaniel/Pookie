using System.Text.RegularExpressions;
using Pookie.SoundCloud;

namespace Pookie.App.Messages;

internal static partial class SoundCloudLinks
{
    internal static string[] Find(string text) => Links().Matches(text).Select(match => match.Value.TrimEnd('.', ',', ';', ')', ']', '!', '?'))
        .Where(SoundCloudWebClient.IsSoundCloudUrl).Distinct(StringComparer.Ordinal).Take(32).ToArray();
    [GeneratedRegex("https://(?:www\\.)?(?:soundcloud\\.com|on\\.soundcloud\\.com)/[^\\s<>\\\"]+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();
}
