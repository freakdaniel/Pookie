using System.Net;

namespace Pookie.Browser.ElectronHost;

internal sealed record PrototypeOptions(string Profile, string Origin, string ApiOrigin, string StartUrl, bool Verify, TimeSpan Timeout)
{
    public static PrototypeOptions Parse(string[] args)
    {
        string? profile = null, fixture = null;
        var verify = false;
        var timeout = 300;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-unpackeddotnet": case "-dotnetpacked": break;
                case "--verify-session": verify = true; break;
                case "--profile" when index + 1 < args.Length: profile = args[++index]; break;
                case "--fixture" when index + 1 < args.Length: fixture = args[++index]; break;
                case "--timeout" when index + 1 < args.Length && int.TryParse(args[++index], out timeout) && timeout is >= 1 and <= 600: break;
                default: throw new ArgumentException("Поддерживаются --verify-session, --profile <путь>, --timeout <секунды> и --fixture <loopback URL>.");
            }
        }
        const string site = "https://soundcloud.com";
        var origin = site;
        if (fixture != null)
        {
            if (!Uri.TryCreate(fixture, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
                !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address) ||
                uri.UserInfo != "" || uri.Fragment != "")
                throw new ArgumentException("Фикстура должна использовать HTTP и числовой loopback-адрес.");
            origin = uri.GetLeftPart(UriPartial.Authority);
            if (profile == null) throw new ArgumentException("Для фикстуры нужен отдельный --profile.");
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataRoot = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support")
            : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { } xdg && Path.IsPathFullyQualified(xdg)
                ? xdg : Path.Combine(home, ".local", "share");
        return new(Path.GetFullPath(profile ?? Path.Combine(dataRoot, "Pookie", "Browser", "Electron")),
            origin, fixture == null ? "https://api-v2.soundcloud.com" : origin,
            fixture ?? site + "/you/likes", verify, TimeSpan.FromSeconds(timeout));
    }
}
