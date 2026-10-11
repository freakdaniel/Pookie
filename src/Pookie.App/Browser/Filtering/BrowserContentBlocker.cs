using Pookie.App.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pookie.App.Browser;

internal static class BrowserContentBlocker
{
    private sealed record Rule(string Host, string PathPrefix);
    private static readonly Rule[] rules = ReadRules();

    public static bool ShouldBlock(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return false;
        var host = uri.IdnHost.TrimEnd('.');
        return rules.Any(rule =>
            (host.Equals(rule.Host, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + rule.Host, StringComparison.OrdinalIgnoreCase)) &&
            uri.PathAndQuery.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static Rule[] ReadRules()
    {
        using var source = typeof(BrowserContentBlocker).Assembly.GetManifestResourceStream("Pookie.App.Browser.Filtering.blacklist.json")
            ?? throw new InvalidOperationException("Missing browser content filters.");
        using var document = JsonDocument.Parse(source);
        return document.RootElement.EnumerateArray().Select(item =>
        {
            var host = item.GetProperty("host").GetString()!;
            var path = item.TryGetProperty("pathPrefix", out var value) ? value.GetString()! : "";
            if (Uri.CheckHostName(host) != UriHostNameType.Dns || (path.Length > 0 && !path.StartsWith('/')))
                throw new InvalidOperationException("Invalid embedded browser content filter.");
            return new Rule(host, path);
        }).ToArray();
    }
}
