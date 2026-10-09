using System.Reflection;
using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal static class BrowserRequestScript
{
    public static string Read(WebSession account, string origin)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Browser.Scripts.browser-requests.js")!;
        using var reader = new StreamReader(input);
        using var audioInput = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Browser.Scripts.browser-audio.js")!;
        using var audioReader = new StreamReader(audioInput);
        using var normalizationInput = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Browser.Scripts.audio-normalization.js")!;
        using var normalizationReader = new StreamReader(normalizationInput);
        var audioScript = audioReader.ReadToEnd().Replace("__POOKIE_NORMALIZATION_MODULE__", JsonSerializer.Serialize(normalizationReader.ReadToEnd()));
        return audioScript + "\n" + reader.ReadToEnd().Replace("const siteOrigin = 'https://soundcloud.com';", "const siteOrigin = " + JsonSerializer.Serialize(origin) + ";")
            .Replace("const apiOrigin = 'https://api-v2.soundcloud.com';", "const apiOrigin = " + JsonSerializer.Serialize(origin == "https://soundcloud.com" ? "https://api-v2.soundcloud.com" : origin) + ";")
            .Replace("__POOKIE_TRACK_COMMENTS__", JsonSerializer.Serialize(TrackReadQueries.Comments))
            .Replace("__POOKIE_TRACK_REPLIES__", JsonSerializer.Serialize(TrackReadQueries.Replies))
            .Replace("__POOKIE_TRACK_SIDEBAR__", JsonSerializer.Serialize(TrackReadQueries.Sidebar))
            .Replace("const graphOrigin = 'https://graph.soundcloud.com';", "const graphOrigin = " + JsonSerializer.Serialize(origin == "https://soundcloud.com" ? "https://graph.soundcloud.com" : origin) + ";")
            .Replace("__POOKIE_ACCOUNT__", JsonSerializer.Serialize(account with { DataDomeClientId = null }, SoundCloudJson.Default.WebSession));
    }
}
