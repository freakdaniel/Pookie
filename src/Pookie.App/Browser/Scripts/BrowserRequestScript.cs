using System.Reflection;
using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal static class BrowserRequestScript
{
    public static string Read(WebSession? account, string origin, bool includeAudio = true)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Browser.Scripts.browser-requests.js")!;
        using var reader = new StreamReader(input);
        return (includeAudio ? ReadAudio() + "\n" : "") + reader.ReadToEnd().Replace("const siteOrigin = 'https://soundcloud.com';", "const siteOrigin = " + JsonSerializer.Serialize(origin, Pookie.SoundCloud.SoundCloudJson.Default.String) + ";")
            .Replace("const apiOrigin = 'https://api-v2.soundcloud.com';", "const apiOrigin = " + JsonSerializer.Serialize(origin == "https://soundcloud.com" ? "https://api-v2.soundcloud.com" : origin, Pookie.SoundCloud.SoundCloudJson.Default.String) + ";")
            .Replace("__POOKIE_TRACK_COMMENTS__", JsonSerializer.Serialize(TrackReadQueries.Comments, Pookie.SoundCloud.SoundCloudJson.Default.String))
            .Replace("__POOKIE_TRACK_REPLIES__", JsonSerializer.Serialize(TrackReadQueries.Replies, Pookie.SoundCloud.SoundCloudJson.Default.String))
            .Replace("__POOKIE_TRACK_SIDEBAR__", JsonSerializer.Serialize(TrackReadQueries.Sidebar, Pookie.SoundCloud.SoundCloudJson.Default.String))
            .Replace("__POOKIE_COMMENT_CREATE__", JsonSerializer.Serialize(CommentWriteQueries.Create, Pookie.SoundCloud.SoundCloudJson.Default.String))
            .Replace("__POOKIE_COMMENT_DELETE__", JsonSerializer.Serialize(CommentWriteQueries.Delete, Pookie.SoundCloud.SoundCloudJson.Default.String))
            .Replace("const graphOrigin = 'https://graph.soundcloud.com';", "const graphOrigin = " + JsonSerializer.Serialize(origin == "https://soundcloud.com" ? "https://graph.soundcloud.com" : origin, Pookie.SoundCloud.SoundCloudJson.Default.String) + ";")
            .Replace("__POOKIE_ACCOUNT__", account == null ? "__POOKIE_ACCOUNT__" : JsonSerializer.Serialize(account.BrowserSnapshot(), SoundCloudJson.Default.WebSession));
    }
    internal static string ReadAudio()
    {
        using var audioInput = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Browser.Scripts.browser-audio.js")!;
        using var audioReader = new StreamReader(audioInput);
        using var normalizationInput = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Browser.Scripts.audio-normalization.js")!;
        using var normalizationReader = new StreamReader(normalizationInput);
        return audioReader.ReadToEnd().Replace("__POOKIE_NORMALIZATION_MODULE__", JsonSerializer.Serialize(normalizationReader.ReadToEnd(), Pookie.SoundCloud.SoundCloudJson.Default.String));
    }
}
