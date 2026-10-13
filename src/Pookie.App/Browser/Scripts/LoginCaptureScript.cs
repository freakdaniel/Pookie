using System.Reflection;
using System.Text.Json;

namespace Pookie.App.Browser;

internal static class LoginCaptureScript
{
    public static string Read(string origin)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Browser.Scripts.login-capture.js")!;
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd().Replace("'https://soundcloud.com'", JsonSerializer.Serialize(origin))
            .Replace("url.origin !== 'https://api-v2.soundcloud.com'",
                "url.origin !== " + JsonSerializer.Serialize(origin == "https://soundcloud.com" ? "https://api-v2.soundcloud.com" : origin));
    }
}
