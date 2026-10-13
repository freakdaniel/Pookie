using Pookie.App.Diagnostics;
using InfiniFrame;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pookie.App.Browser;

internal static class BrowserContentBlocker
{
    private sealed record Rule(string Host, string PathPrefix);
    private static readonly Rule[] rules = ReadRules();

    public static void Attach(IInfiniFrameWindow window)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Register after environment creation: HTTP(S) are built-in protocols, not
        // new WebView2 custom protocols. Null content preserves the original request.
        window.RegisterCustomSchemeHandler("https", Filter);
        window.RegisterCustomSchemeHandler("http", Filter);
        StartupLog.Event("browser.content-filter-ready");
    }

    private static (Stream? Data, string? ContentType) Filter(IInfiniFrameWindow window, string url) =>
        ShouldBlock(url)
            ? (new MemoryStream("[]"u8.ToArray(), writable: false), "application/json") : (null, null);

    public static bool ShouldBlock(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return false;
        var host = uri.IdnHost.TrimEnd('.');
        return rules.Any(rule =>
            (host.Equals(rule.Host, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + rule.Host, StringComparison.OrdinalIgnoreCase)) &&
            uri.PathAndQuery.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase));
    }

    // One reviewed rule set drives both platform transports; it never downloads at startup.
    public static byte[] CreateWebKitRules()
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartArray();
            foreach (var rule in rules)
            {
                writer.WriteStartObject();
                writer.WriteStartObject("trigger");
                var path = rule.PathPrefix.Length == 0 ? "" : Regex.Escape(rule.PathPrefix[1..]);
                writer.WriteString("url-filter", "^https?://([^/]+\\.)?" + Regex.Escape(rule.Host) + "\\.?(:[0-9]+)?/" + path);
                writer.WriteBoolean("url-filter-is-case-sensitive", false);
                writer.WriteEndObject();
                writer.WriteStartObject("action");
                writer.WriteString("type", "block");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return output.ToArray();
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
