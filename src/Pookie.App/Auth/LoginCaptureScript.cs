using System.Reflection;
using System.Text.Json;

namespace Pookie.App.Auth;

internal static class LoginCaptureScript
{
    public static string Read(string origin)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pookie.App.Auth.login-capture.js")!;
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd().Replace("'https://soundcloud.com'", JsonSerializer.Serialize(origin));
    }
}
